using Arcana.Core;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

/// <summary>Migración explícita con credenciales de servidor; ningún UID se incluye en el juego.</summary>
public static class AccountAdminSetup
{
    public static async Task Run(string[] accounts)
    {
        if (accounts.Length != 3) throw new ArgumentException("Indica las tres cuentas Google como correo o uid:UID.");
        string project = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? "arcana-arena-f2997";
        var credential = GoogleCredential.GetApplicationDefault();
        var app = FirebaseApp.Create(new() { ProjectId = project, Credential = credential }, "account-admin-setup");
        try
        {
            var auth = FirebaseAuth.GetAuth(app);
            var uids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string account in accounts)
            {
                var user = account.StartsWith("uid:", StringComparison.Ordinal)
                    ? await auth.GetUserAsync(account[4..]) : await auth.GetUserByEmailAsync(account);
                if (user.Disabled || !user.ProviderData.Any(p => p.ProviderId == "google.com") || !uids.Add(user.Uid))
                    throw new ArgumentException("Se necesitan tres cuentas Google distintas y activas.");
            }
            var db = new FirestoreDbBuilder { ProjectId = project, Credential = credential }.Build();
            await db.RunTransactionAsync(async tx =>
            {
                var configurations = new List<(DocumentReference Reference, Dictionary<string, object> Changes)>();
                foreach (string uid in uids)
                {
                    var reference = db.Collection("users").Document(uid).Collection("configuration").Document("account");
                    var existing = await tx.GetSnapshotAsync(reference);
                    var legacy = await tx.GetSnapshotAsync(db.Collection("statisticsReaders").Document(uid));
                    var changes = new Dictionary<string, object> { ["isAdmin"] = true, ["previewUpdates"] = true,
                        ["updatedAt"] = FieldValue.ServerTimestamp };
                    // Repetir la migración conserva compras y permisos posteriores.
                    if (!existing.Exists || !existing.ContainsField("matchStatistics"))
                        changes["matchStatistics"] = legacy.Exists && legacy.TryGetValue<bool>("enabled", out var enabled) && enabled;
                    if (!existing.Exists || !existing.ContainsField("adsRemoved")) changes["adsRemoved"] = false;
                    configurations.Add((reference, changes));
                }
                foreach (var entry in configurations) tx.Set(entry.Reference, entry.Changes, SetOptions.MergeAll);
            });
            Console.WriteLine("Migradas las tres cuentas administradoras. Compras y permisos de estadísticas conservados.");
        }
        finally { app.Delete(); }
    }
}
