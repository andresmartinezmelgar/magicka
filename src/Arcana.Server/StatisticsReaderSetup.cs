using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public static class StatisticsReaderSetup
{
    public static async Task Run(string[] readers)
    {
        if (readers.Length != 2) throw new ArgumentException("Indica exactamente dos correos Google o uid:UID.");
        string project = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? "arcana-arena-f2997";
        var credential = GoogleCredential.GetApplicationDefault();
        var app = FirebaseApp.Create(new() { ProjectId = project, Credential = credential }, "statistics-readers");
        try
        {
            var auth = FirebaseAuth.GetAuth(app); var uids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string reader in readers)
            {
                var user = reader.StartsWith("uid:", StringComparison.Ordinal) ? await auth.GetUserAsync(reader[4..]) : await auth.GetUserByEmailAsync(reader);
                if (user.Disabled || !user.ProviderData.Any(p => p.ProviderId == "google.com") || !uids.Add(user.Uid))
                    throw new ArgumentException("Se necesitan dos cuentas Google distintas y activas.");
            }
            var db = new FirestoreDbBuilder { ProjectId = project, Credential = credential }.Build();
            var existing = await db.Collection("statisticsReaders").GetSnapshotAsync();
            if (existing.Count > 450) throw new InvalidOperationException("Revisa los permisos existentes antes de continuar.");
            var batch = db.StartBatch();
            foreach (var doc in existing.Documents) if (!uids.Contains(doc.Id)) batch.Delete(doc.Reference);
            foreach (string uid in uids) batch.Set(db.Collection("statisticsReaders").Document(uid), new Dictionary<string, object> { ["enabled"] = true });
            await batch.CommitAsync();
            Console.WriteLine("Estadísticas: autorizadas exactamente dos cuentas Google.");
        }
        finally { app.Delete(); }
    }
}
