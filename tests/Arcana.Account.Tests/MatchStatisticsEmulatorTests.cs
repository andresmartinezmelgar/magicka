using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

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

        // Probar la ruta real de ingesta frente al emulador; el cliente no recibe
        // credenciales Admin ni escribe directamente en la base.
        string folder = Path.Combine(Path.GetTempPath(), "arcana-ingest-" + Guid.NewGuid().ToString("N"));
        var accounts = new AccountService(new(database.ProjectId, "test-only", "demo.firebaseapp.com", "http://127.0.0.1"), new NoLogin(), repository);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(accounts); builder.Services.AddArcanaProtection();
        builder.Services.AddSingleton(new MatchStatisticsService(accounts, NullLogger<MatchStatisticsService>.Instance, folder));
        builder.Services.AddHostedService(p => p.GetRequiredService<MatchStatisticsService>());
        await using var app = builder.Build(); app.UseArcanaProtection(); app.MapMatchStatistics();
        await app.StartAsync();
        try
        {
            var address = new Uri(app.Urls.Single());
            using var handler = new IngestionHandler(address); using var http = new HttpClient(handler);
            using var transport = new MatchStatisticsTransport(database.ProjectId, http);
            var incoming = final with { RunId = Guid.NewGuid().ToString("N") };
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => transport.Upload(incoming)));
            await transport.Upload(report with { RunId = incoming.RunId, Status = MatchRecordStatus.Left });
            async Task<DocumentSnapshot> WaitFor(string id)
            {
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    var snapshot = await database.Collection("matchStatistics").Document(id).GetSnapshotAsync();
                    if (snapshot.Exists && MatchStatisticsReport.Parse(snapshot.GetValue<string>("payload")).Complete) return snapshot;
                    await Task.Delay(50);
                }
                throw new Exception("La ingesta no entregó el informe al emulador.");
            }
            var received = await WaitFor(incoming.RunId);
            var copies = await database.Collection("matchStatistics").WhereEqualTo("runId", incoming.RunId).GetSnapshotAsync();
            if (copies.Count != 1 || handler.Requests != 4 || received.ToDictionary().Keys.Any(k => k.Contains("uid", StringComparison.OrdinalIgnoreCase)))
                throw new Exception("La ingesta duplica revisiones o incorpora identidad.");
            using var raw = new HttpClient();
            using var forged = await raw.PostAsync(new Uri(address, "analytics/matches"), new StringContent((incoming with { Network = true }).ToJson(), System.Text.Encoding.UTF8, "application/json"));
            if (forged.StatusCode != HttpStatusCode.BadRequest) throw new Exception("Un cliente pudo declarar estadísticas autoritativas.");
            var authoritative = incoming with { RunId = Guid.NewGuid().ToString("N"), Network = true };
            await repository.SaveMatchStatistics(authoritative);
            await repository.SaveMatchStatistics(authoritative with { Network = false, Revision = 1200, Seconds = 20 });
            var protectedReport = MatchStatisticsReport.Parse((await database.Collection("matchStatistics").Document(authoritative.RunId).GetSnapshotAsync()).GetValue<string>("payload"));
            if (!protectedReport.Network || protectedReport.Revision != 600) throw new Exception("La ingesta local sustituyó un resultado del servidor.");
            Console.WriteLine("MATCH_STATISTICS_INGESTION_OK: HTTP real, cuotas, diario, duplicados y rechazo de informes online falsos.");
        }
        finally { await app.StopAsync(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class NoLogin : IAccountIdentityVerifier
    {
        public Task<AccountIdentity> Verify(string idToken, CancellationToken token) => Task.FromException<AccountIdentity>(new UnauthorizedAccessException());
    }
    private sealed class IngestionHandler(Uri target) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri?.Host != "familiamelgar.duckdns.org" || request.Headers.Authorization != null || !target.IsLoopback)
                throw new InvalidOperationException("La prueba intenta usar identidad o una dirección no local.");
            Interlocked.Increment(ref Requests); request.RequestUri = new Uri(target, "analytics/matches");
            return base.SendAsync(request, token);
        }
    }
}
