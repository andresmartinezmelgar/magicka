using Arcana.Core;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    internal async Task<int> PurgeOldStatistics(int maximum, CancellationToken token)
    {
        string before = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30).ToString("yyyy-MM-dd");
        int removed = 0;
        foreach (string name in new[] { "matchStatistics", "dashboardTelemetry" })
        {
            var old = await database.Collection(name).WhereLessThan("day", before).Limit(Math.Clamp(maximum, 1, 400)).GetSnapshotAsync(token);
            if (old.Count == 0) continue;
            var batch = database.StartBatch(); foreach (var doc in old.Documents) batch.Delete(doc.Reference);
            await batch.CommitAsync(token); removed += old.Count;
        }
        return removed;
    }
}

/// <summary>Limpieza optativa, acotada y sin TTL de pago. No toca recibos, oro ni inventarios.</summary>
public sealed class StatisticsRetentionService(AccountService accounts, ILogger<StatisticsRetentionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Environment.GetEnvironmentVariable("ARCANA_STATISTICS_CLEANUP_ENABLED") != "true" ||
            accounts.Repository is not FirestoreAccountRepository repository) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { int count = await repository.PurgeOldStatistics(100, stoppingToken); logger.LogInformation("Telemetría caducada eliminada: {Count} documentos.", count); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Limpieza de telemetría pendiente ({Type}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

public static class StatisticsMaintenance
{
    // Ejecutar sin --apply muestra el volumen previsto y no cambia Firebase.
    public static async Task Run(string[] args)
    {
        bool apply = false; string? cursor = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--apply") apply = true;
            else if (args[index] == "--after" && index + 1 < args.Length &&
                System.Text.RegularExpressions.Regex.IsMatch(args[index + 1], "^[A-Za-z0-9_-]{1,128}$")) cursor = args[++index];
            else throw new ArgumentException("Uso: --statistics-maintenance [--apply] [--after ID]");
        }
        var options = AccountOptions.FromEnvironment();
        if (!options.Enabled) throw new InvalidOperationException("Configura el proyecto Firebase del servidor.");
        var database = new FirestoreDbBuilder { ProjectId = options.ProjectId, Credential = GoogleCredential.GetApplicationDefault() }.Build();
        var repository = new FirestoreAccountRepository(database);
        int examined = 0, candidates = 0;
        while (examined < 10000)
        {
            Query query = database.Collection("matchStatistics").OrderBy(FieldPath.DocumentId).Limit(100);
            if (cursor != null) query = query.StartAfter(cursor);
            var page = await query.GetSnapshotAsync(); if (page.Count == 0) break;
            foreach (var doc in page.Documents)
            {
                examined++; cursor = doc.Id;
                try
                {
                    var report = MatchStatisticsReport.Parse(doc.GetValue<string>("payload"));
                    if (doc.Id == report.RunId) continue;
                    candidates++;
                    if (apply)
                    {
                        await repository.SaveMatchStatistics(report with { Network = false });
                        await doc.Reference.DeleteAsync();
                    }
                }
                catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or InvalidOperationException)
                { /* Los documentos ilegibles no se promueven ni se borran. */ }
            }
        }
        Console.WriteLine($"Revisados: {examined}; revisiones antiguas a compactar: {candidates}; aplicado: {apply}. Máximo por ejecución: 10000 documentos.");
        if (examined >= 10000) Console.WriteLine($"Continuar desde: --after {cursor}");
    }
}
