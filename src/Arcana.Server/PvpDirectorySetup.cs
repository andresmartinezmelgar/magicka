using Arcana.Core;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace Arcana.Server;

internal sealed class PvpDirectorySetup(AccountOptions options, GameService game, ILogger<PvpDirectorySetup> log) : BackgroundService
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            !origin.IsDefaultPort || origin.AbsolutePath != "/" || origin.IsLoopback ||
            origin.IdnHost.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)) return;
        string url;
        try { url = PublicAccountServer.ValidateAddress("wss://" + origin.IdnHost + "/play"); }
        catch (InvalidDataException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var db = await new FirestoreDbBuilder { ProjectId = options.ProjectId }.BuildAsync(stoppingToken);
                var doc = db.Document(PublicAccountServer.DocumentPath);
                if ((await doc.GetSnapshotAsync(stoppingToken)).Exists) return;
                var rooms = await RoomBrowser.ListAsync(url, stoppingToken);
                if (!PublicAccountServer.SameInstance(game.Info(), rooms.Server))
                {
                    log.LogInformation("La URL pública no identifica esta instancia. El directorio PvP sigue disponible para configuración manual.");
                    return;
                }
                await doc.CreateAsync(new Dictionary<string, object> { ["url"] = url }, stoppingToken);
                log.LogInformation("Creado el directorio PvP en Firestore.");
                return;
            }
            catch (RpcException error) when (error.StatusCode == StatusCode.AlreadyExists) { return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { log.LogWarning("No se pudo inicializar el directorio PvP ({Kind}). Se reintentará.", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
