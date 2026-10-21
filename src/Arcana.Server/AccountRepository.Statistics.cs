using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    public async Task SaveMatchStatistics(MatchStatisticsReport report, CancellationToken token = default)
    {
        string json = report.ToJson();
        var reference = database.Collection("matchStatistics").Document(report.RunId);
        await database.RunTransactionAsync(async transaction =>
        {
            var previous = await transaction.GetSnapshotAsync(reference, token);
            if (previous.Exists && !MatchStatisticsReport.IsNewer(report, MatchStatisticsReport.Parse(previous.GetValue<string>("payload")))) return;
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["schemaVersion"] = report.SchemaVersion, ["runId"] = report.RunId, ["revision"] = report.Revision,
                ["day"] = report.Day, ["mode"] = report.Mode.ToString(), ["map"] = report.Map.ToString(),
                ["gameVersion"] = report.GameVersion, ["complete"] = report.Complete, ["payload"] = json
            });
        }, cancellationToken: token);
    }
}
