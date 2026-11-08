using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class PublicDashboardEmulatorTests
{
    public static async Task Run(FirestoreDb database)
    {
        string uid = "PRIVATE_UID_" + Guid.NewGuid().ToString("N");
        var world = new GameWorld(new WorldOptions { Bots = 0 });
        var player = world.AddPlayer("private@example.test", 0)!;
        for (int i = 0; i < 60; i++) world.Step();
        var report = world.MatchStatistics(MatchRecordStatus.Completed);
        var repository = new FirestoreAccountRepository(database);
        await repository.SaveMatchStatistics(report);
        var telemetry = DashboardRunReport.Capture(report, world.Snapshot(), player.Id, null);
        var payload = JsonNode.Parse(telemetry.ToJson())!;
        payload["privateAccountField"] = uid;
        await database.Collection("dashboardTelemetry").Document(report.RunId + "-" + uid).SetAsync(new Dictionary<string, object>
        {
            ["day"] = report.Day, ["ownerUid"] = uid, ["payload"] = payload.ToJsonString(),
            ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow), ["privateKey"] = "NEVER_EXPORT_THIS"
        });
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var options = new AccountOptions("demo-arcana-accounts", "test-only-key", "demo-arcana-accounts.firebaseapp.com", "http://127.0.0.1:8080");
        builder.Services.AddSingleton(new AccountService(options, new TestVerifier(), repository));
        builder.Services.AddPublicDashboard();
        await using var app = builder.Build(); app.MapPublicDashboard(); await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            HttpResponseMessage? response = null;
            for (int i = 0; i < 40; i++)
            {
                response?.Dispose(); response = await http.GetAsync("/api/dashboard/history");
                if (response.IsSuccessStatusCode) break;
                await Task.Delay(250);
            }
            using var ready = response!; ready.EnsureSuccessStatusCode();
            string body = await ready.Content.ReadAsStringAsync();
            using var parsed = JsonDocument.Parse(body);
            if (!parsed.RootElement.GetProperty("reports").EnumerateArray().Any(r => r.GetProperty("runId").GetString() == report.RunId) ||
                !parsed.RootElement.GetProperty("telemetry").EnumerateArray().Any(t => t.GetProperty("report").GetProperty("runId").GetString() == report.RunId))
                throw new Exception("El dashboard público no contiene el histórico validado.");
            if (body.Contains(uid) || body.Contains("ownerUid") || body.Contains("privateAccountField") || body.Contains("NEVER_EXPORT_THIS") || body.Contains("private@example.test"))
                throw new Exception("Se publican datos ajenos a la proyección de estadísticas.");
            string etag = ready.Headers.ETag?.ToString() ?? throw new Exception("Falta ETag.");
            using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/history");
            conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
            using var unchanged = await http.SendAsync(conditional);
            if ((int)unchanged.StatusCode != 304 || (await unchanged.Content.ReadAsByteArrayAsync()).Length != 0)
                throw new Exception("La caché vuelve a transferir el histórico sin cambios.");
            using var zipped = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/history");
            zipped.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
            using var compressed = await http.SendAsync(zipped); compressed.EnsureSuccessStatusCode();
            if (!compressed.Content.Headers.ContentEncoding.Contains("gzip")) throw new Exception("Falta compresión HTTP.");
            await using var stream = await compressed.Content.ReadAsStreamAsync();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress); using var reader = new StreamReader(gzip);
            if (await reader.ReadToEndAsync() != body) throw new Exception("La compresión HTTP modifica los datos públicos.");
            Console.WriteLine("PUBLIC_DASHBOARD_OK: GET anónimo, lista blanca, correo redactado, ETag 304 y gzip sin pérdida.");
        }
        finally { await app.StopAsync(); }
    }
}
