using Arcana.Core;
using Google.Cloud.Firestore;
using System.Security.Cryptography;
using System.Text;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    private sealed record VerifiedAdState(ShopAdTicket Ticket, bool Completed = false, string? Transaction = null);
    private DocumentReference RewardChallenge(string uid) => User(uid).Collection("shop").Document("rewardedAd");

    public Task<ShopAdTicket> StartVerifiedShopAd(string uid, ShopAdRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId);
        return database.RunTransactionAsync(async tx =>
        {
            var access = await tx.GetSnapshotAsync(AccountConfigurationDocument(uid), token);
            var advertising = ReadAdvertising(await tx.GetSnapshotAsync(AdvertisingDocument, token));
            if (!advertising.RequiresAd(access.Exists ? FirestoreData.Read<AccountAccess>(access) : new(), _shopTime.GetUtcNow()))
                throw new InvalidOperationException("No necesitas ver un anuncio. Consulta la tienda para renovar directamente.");
            var shop = ReadShop(await tx.GetSnapshotAsync(ShopDocument(uid), token));
            var challenge = RewardChallenge(uid); var previous = await tx.GetSnapshotAsync(challenge, token);
            var now = _shopTime.GetUtcNow();
            if (shop.Id != request.ShopId || ShopRules.NeedsRefresh(shop, now))
                throw new InvalidOperationException("Las ofertas han cambiado. Consulta de nuevo la tienda.");
            if (shop.RewardedRefreshAvailableAt > now) throw new InvalidOperationException("Ya has renovado las ofertas con un anuncio hoy.");
            if (previous.Exists)
            {
                var state = FirestoreData.Read<VerifiedAdState>(previous);
                if (!state.Completed && state.Ticket.ShopId == shop.Id && state.Ticket.ExpiresAt > now) return state.Ticket;
            }
            var ticket = new ShopAdTicket(Guid.NewGuid().ToString("N"), shop.Id, now, now.AddMinutes(30)) { UserId = uid };
            tx.Set(challenge, FirestoreData.Map(new VerifiedAdState(ticket))); return ticket;
        }, cancellationToken: token);
    }

    public async Task<ItemShop> CompleteVerifiedShopAd(string uid, ShopAdCompleteRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId); ValidId(request.TicketId);
        var doc = await RewardChallenge(uid).GetSnapshotAsync(token);
        if (!doc.Exists) throw new InvalidOperationException("Inicia el anuncio para renovar las ofertas.");
        var state = FirestoreData.Read<VerifiedAdState>(doc);
        if (state.Ticket.Id != request.TicketId || state.Ticket.ShopId != request.ShopId)
            throw new InvalidOperationException("El anuncio no pertenece a esta cuenta o renovación.");
        if (!state.Completed) throw new InvalidOperationException("Esperando la confirmación del anuncio.");
        return ReadShop(await ShopDocument(uid).GetSnapshotAsync(token));
    }

    public async Task ConfirmVerifiedShopAd(VerifiedAdMobReward reward, CancellationToken token = default)
    {
        ValidId(reward.TicketId);
        if (string.IsNullOrWhiteSpace(reward.Uid) || reward.Uid.Length > 128 || reward.Uid.Contains('/')) throw new ArgumentException("Cuenta inválida.");
        var receipt = database.Collection("admobTransactions").Document(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reward.TransactionId))));
        ItemShop? replacement = null;
        await database.RunTransactionAsync(async tx =>
        {
            if ((await tx.GetSnapshotAsync(Deletion(reward.Uid), token)).Exists) return;
            var previous = await tx.GetSnapshotAsync(receipt, token);
            var challenge = RewardChallenge(reward.Uid); var ad = await tx.GetSnapshotAsync(challenge, token);
            var shopDoc = ShopDocument(reward.Uid); var shop = ReadShop(await tx.GetSnapshotAsync(shopDoc, token));
            if (previous.Exists)
            {
                if (previous.GetValue<string>("uid") != reward.Uid || previous.GetValue<string>("ticket") != reward.TicketId)
                    throw new InvalidOperationException("La transacción ya está asignada a otra recompensa.");
                return; // Reintento de Google: nunca vuelve a generar ofertas.
            }
            if (!ad.Exists) throw new InvalidOperationException("Anuncio desconocido.");
            var state = FirestoreData.Read<VerifiedAdState>(ad); var now = _shopTime.GetUtcNow();
            if (state.Ticket.Id != reward.TicketId || state.Ticket.UserId != reward.Uid || state.Completed ||
                reward.WatchedAt < state.Ticket.ReadyAt.AddSeconds(-30) || reward.WatchedAt > state.Ticket.ExpiresAt)
                throw new InvalidOperationException("El anuncio no corresponde a una renovación pendiente.");
            if (shop.RewardedRefreshAvailableAt > now) throw new InvalidOperationException("Ya has renovado las ofertas con un anuncio hoy.");
            replacement ??= ShopRules.Generate(_loot, checked(shop.Revision + 1), now) with
                { RewardedRefreshAvailableAt = ShopRules.NextRefreshAt(now) };
            tx.Set(shopDoc, FirestoreData.Map(replacement));
            tx.Set(challenge, FirestoreData.Map(state with { Completed = true, Transaction = reward.TransactionId }));
            tx.Create(receipt, new Dictionary<string, object> { ["uid"] = reward.Uid, ["ticket"] = reward.TicketId,
                ["confirmedAt"] = Timestamp.FromDateTimeOffset(now) });
        }, cancellationToken: token);
    }
}
