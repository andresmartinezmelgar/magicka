using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;

internal static class MatchStatisticsEmulatorTests
{
    public static async Task Run(FirestoreDb database)
    {
        var repository = new FirestoreAccountRepository(database);
        var world = new GameWorld(); world.AddPlayer("PRIVATE_NAME"); world.Step();
        var report = world.MatchStatistics();
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.SaveMatchStatistics(report)));
        var final = report with { Revision = 600, Seconds = 10, Status = MatchRecordStatus.Completed };
        await repository.SaveMatchStatistics(final);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.SaveMatchStatistics(report)));
        var doc = await database.Collection("matchStatistics").Document(report.RunId).GetSnapshotAsync();
        var saved = MatchStatisticsReport.Parse(doc.GetValue<string>("payload"));
        if (!saved.Complete || saved.Revision != 600 || doc.ToDictionary().Keys.Any(k => k.Contains("uid", StringComparison.OrdinalIgnoreCase)) || saved.ToJson().Contains("PRIVATE_NAME"))
            throw new InvalidOperationException("Informe duplicado, degradado o con identidad.");
        Console.WriteLine("MATCH_STATISTICS_TRANSACTIONS_OK: reintentos concurrentes, final inmutable e informe sin identidad.");

        // La ruta de producción se ejecuta sin tokens y con las reglas reales
        // del proyecto de pruebas cargadas por Firebase CLI. Solo cambia el host.
        var emulator = new Uri("http://" + Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"));
        string testProject = Environment.GetEnvironmentVariable("GCLOUD_PROJECT") is { Length: > 0 } configured ? configured : database.ProjectId;
        if (!testProject.StartsWith("demo-", StringComparison.Ordinal)) throw new InvalidOperationException("El envío directo se prueba solo en proyectos demo.");
        using var handler = new DirectFirestoreHandler(emulator);
        using var http = new HttpClient(handler);
        using var transport = new MatchStatisticsTransport(testProject, http);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => transport.Upload(final)));
        await transport.Upload(report with { Status = MatchRecordStatus.Left });
        var directDatabase = new FirestoreDbBuilder { ProjectId = testProject, EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly }.Build();
        var copies = await directDatabase.Collection("matchStatistics").WhereEqualTo("runId", final.RunId).GetSnapshotAsync();
        if (copies.Count != 2 || handler.Requests != 4 || copies.Documents.Any(d => d.ToDictionary().Keys.Any(k => k.Contains("uid", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("El envío directo duplica revisiones o incorpora identidad.");
        var reports = copies.Documents.Select(d => MatchStatisticsReport.Parse(d.GetValue<string>("payload"))).ToArray();
        if (reports.Count(r => r.Complete && r.Revision == 600) != 1 || reports.Any(r => r.ToJson().Contains("PRIVATE_NAME")))
            throw new InvalidOperationException("El informe tardío sobrescribió el final.");
        Console.WriteLine("MATCH_STATISTICS_DIRECT_FIRESTORE_OK: REST anónimo real, reglas, reintentos concurrentes y final conservado sin relé.");

        var outbox = new MatchStatisticsOutbox();
        if (outbox.Enqueue(report)) throw new InvalidOperationException("Se encoló una partida en curso.");
        var queued = final with { RunId = Guid.NewGuid().ToString("N") };
        outbox.Enqueue(queued);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => outbox.Flush(transport.Upload)));
        var uploaded = await directDatabase.Collection("matchStatistics").WhereEqualTo("runId", queued.RunId).GetSnapshotAsync();
        if (uploaded.Count != 1 || outbox.PendingCount != 0 || !MatchStatisticsReport.Parse(uploaded.Documents.Single().GetValue<string>("payload")).Complete)
            throw new InvalidOperationException("La cola en memoria perdió el final o duplicó el envío.");
        Console.WriteLine("MATCH_STATISTICS_MEMORY_OUTBOX_OK: solo cierre, envío REST real, confirmación y vaciado de memoria sin historial local.");
    }

    private sealed class DirectFirestoreHandler(Uri emulator) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "firestore.googleapis.com" || request.Headers.Authorization != null || request.Headers.Contains("Cookie") || !emulator.IsLoopback)
                throw new InvalidOperationException("La prueba directa intenta usar identidad o una dirección no local.");
            Interlocked.Increment(ref Requests);
            request.RequestUri = new UriBuilder(request.RequestUri) { Scheme = "http", Host = emulator.Host, Port = emulator.Port }.Uri;
            return base.SendAsync(request, token);
        }
    }
}
