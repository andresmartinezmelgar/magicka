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
    }
}
