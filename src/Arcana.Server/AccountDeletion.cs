using Arcana.Core;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    private DocumentReference Deletion(string uid) { _ = User(uid); return database.Collection("accountDeletions").Document(uid); }
    public async Task<bool> IsDeletionRequested(string uid, CancellationToken token = default) =>
        (await Deletion(uid).GetSnapshotAsync(token)).Exists;
    public Task RequestDeletion(string uid, CancellationToken token = default) => database.RunTransactionAsync(async tx =>
    {
        var entry = Deletion(uid);
        if (!(await tx.GetSnapshotAsync(entry, token)).Exists)
            tx.Create(entry, new Dictionary<string, object> { ["status"] = "pending", ["requestedAt"] = Timestamp.GetCurrentTimestamp() });
    }, cancellationToken: token);
    internal async Task<string[]> PendingDeletions(CancellationToken token) =>
        (await database.Collection("accountDeletions").WhereEqualTo("status", "pending").Limit(20).GetSnapshotAsync(token)).Documents.Select(d => d.Id).ToArray();
    internal async Task DeleteAccountData(string uid, CancellationToken token)
    {
        // Los recibos históricos anteriores a esta revisión incluían UID en el
        // archivo global. Se anonimiza solo el participante solicitado, sin
        // borrar resultados o premios de otros jugadores.
        string? cursor = null;
        while (true)
        {
            Query query = database.Collection("matches").OrderBy(FieldPath.DocumentId).Limit(100);
            if (cursor != null) query = query.StartAfter(cursor);
            var page = await query.GetSnapshotAsync(token); if (page.Count == 0) break;
            foreach (var document in page.Documents)
            {
                cursor = document.Id;
                await database.RunTransactionAsync(async tx =>
                {
                    var current = await tx.GetSnapshotAsync(document.Reference, token);
                    if (!current.Exists) return;
                    var match = FirestoreData.Read<RewardMatch>(current);
                    if (!match.Participants.Any(p => p.Uid == uid)) return;
                    tx.Set(document.Reference, FirestoreData.Map(match with
                        { Participants = match.Participants.Select(p => p.Uid == uid ? p with { Uid = "" } : p).ToArray() }));
                }, cancellationToken: token);
            }
        }
        await DeleteTree(User(uid), token);
        _inventoryCache.TryRemove(uid, out _);
        await database.Collection("statisticsReaders").Document(uid).DeleteAsync(cancellationToken: token);
        // Telemetría en directo sí lleva propietario; los informes de combate no contienen UID.
        while (true)
        {
            var telemetry = await database.Collection("dashboardTelemetry").WhereEqualTo("ownerUid", uid).Limit(100).GetSnapshotAsync(token);
            if (telemetry.Count == 0) break;
            var batch = database.StartBatch(); foreach (var doc in telemetry.Documents) batch.Delete(doc.Reference); await batch.CommitAsync(token);
        }
    }
    private static async Task DeleteTree(DocumentReference document, CancellationToken token)
    {
        await foreach (var collection in document.ListCollectionsAsync().WithCancellation(token))
            while (true)
            {
                var page = await collection.Limit(100).GetSnapshotAsync(token);
                if (page.Count == 0) break;
                foreach (var child in page.Documents) await DeleteTree(child.Reference, token);
            }
        await document.DeleteAsync(cancellationToken: token);
    }
    internal Task CompleteDeletion(string uid, CancellationToken token) => Deletion(uid).UpdateAsync(new Dictionary<string, object>
        { ["status"] = "completed", ["completedAt"] = Timestamp.GetCurrentTimestamp() }, cancellationToken: token);
}

public sealed class AccountDeletionService(AccountService accounts, ILogger<AccountDeletionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await accounts.ProcessDeletions(stoppingToken, error => logger.LogWarning("Borrado de cuenta pendiente de reintento ({Type}).", error.GetType().Name)); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Borrado de cuenta pendiente de reintento ({Type}).", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

public static class AccountDeletionEndpoints
{
    public static void MapAccountDeletion(this WebApplication app)
    {
        app.MapPost("/account/delete", async (AccountDeletionRequest request, HttpContext context, AccountService accounts) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!accounts.Enabled) return Results.StatusCode(503);
            var user = await accounts.AuthenticateHttp(context);
            if (user == null) return Results.Unauthorized();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (request.Confirmation != "ELIMINAR" || user.AuthTime <= 0 || now - user.AuthTime > 300 || user.AuthTime > now + 30)
                return Results.Json(new ApiError("Cierra sesión y vuelve a entrar con Google antes de eliminar tu cuenta."), Protocol.Json, statusCode: 403);
            await accounts.Repository!.RequestDeletion(user.Uid, context.RequestAborted);
            return Results.Json(new ApiError("Eliminación solicitada. El perfil, inventario y acceso Google se eliminarán en el servidor."), Protocol.Json, statusCode: 202);
        });
    }
}
