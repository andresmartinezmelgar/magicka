using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

internal static class PvpDirectorySetup
{
    internal static async Task Publish(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Indica una URL wss:// con ruta /play.");
        string url = PublicAccountServer.ValidateAddress(args[0]);
        string project = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID") ?? PublicAccountServer.Project;
        var db = await FirestoreDb.CreateAsync(project);
        await db.Document(PublicAccountServer.DocumentPath).SetAsync(new Dictionary<string, object> { ["url"] = url });
        Console.WriteLine("Directorio PvP actualizado en " + project + "/" + PublicAccountServer.DocumentPath);
    }
}
