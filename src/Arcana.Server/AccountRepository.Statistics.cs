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
            if (previous.Exists)
            {
                var old = MatchStatisticsReport.Parse(previous.GetValue<string>("payload"));
                // Una ingesta local no puede sustituir resultados autoritativos
                // del servidor aunque copie un runId visto en el dashboard.
                if (old.Network && !report.Network || old.Network == report.Network && !MatchStatisticsReport.IsNewer(report, old)) return;
            }
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["schemaVersion"] = report.SchemaVersion, ["runId"] = report.RunId, ["revision"] = report.Revision,
                ["day"] = report.Day, ["mode"] = report.Mode.ToString(), ["map"] = report.Map.ToString(),
                ["gameVersion"] = report.GameVersion, ["complete"] = report.Complete, ["payload"] = json,
                ["expiresAt"] = Timestamp.FromDateTime(DateOnly.ParseExact(report.Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    .AddDays(31).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            });
        }, cancellationToken: token);
    }
}
