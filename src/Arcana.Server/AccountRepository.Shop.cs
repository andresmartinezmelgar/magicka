using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    private readonly TimeProvider _shopTime = timeProvider ?? TimeProvider.System;
    private sealed record AdState(ShopAdTicket Ticket, bool Completed = false);
    private static void ValidId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Identificador de tienda inválido.");
    }
    private static ItemShop ReadShop(DocumentSnapshot doc)
    {
        if (!doc.Exists) throw new InvalidOperationException("Abre la tienda para consultar sus ofertas.");
        var shop = FirestoreData.Read<ItemShop>(doc);
        if (!ShopRules.Valid(shop)) throw new InvalidDataException("Catálogo de tienda inválido.");
        return shop;
    }
    private DocumentReference ShopDocument(string uid) => User(uid).Collection("shop").Document("current");

    public async Task<ItemShop> Shop(string uid, string name, CancellationToken token = default)
    {
        await EnsureUser(uid, name, token);
        var doc = ShopDocument(uid);
        ItemShop? replacement = null;
        return await database.RunTransactionAsync(async tx =>
        {
            var existing = await tx.GetSnapshotAsync(doc, token);
            var shop = existing.Exists ? ReadShop(existing) : null;
            var now = _shopTime.GetUtcNow();
            if (shop != null && !ShopRules.NeedsRefresh(shop, now)) return shop;
            int revision = shop == null ? 1 : checked(shop.Revision + 1);
            // Conserva la tirada en los reintentos, salvo que otro dispositivo haya
            // avanzado la revisión o el reintento cruce otra medianoche.
            if (replacement == null || replacement.Revision != revision || replacement.RefreshAt != ShopRules.NextRefreshAt(now))
                replacement = ShopRules.Generate(_loot, revision, now);
            tx.Set(doc, FirestoreData.Map(replacement));
            return replacement;
        }, cancellationToken: token);
    }

    public async Task<bool> Buy(string uid, ShopPurchaseRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId); ValidId(request.ItemId);
        var user = User(uid); var shopDoc = ShopDocument(uid);
        var receipt = user.Collection("shopPurchases").Document(request.ItemId);
        return await database.RunTransactionAsync(async tx =>
        {
            var previous = await tx.GetSnapshotAsync(receipt, token);
            if (previous.Exists)
            {
                if (previous.GetValue<string>("shopId") != request.ShopId) throw new ArgumentException("La oferta no pertenece a este catálogo.");
                return false;
            }
            var account = await tx.GetSnapshotAsync(user, token);
            var shop = ReadShop(await tx.GetSnapshotAsync(shopDoc, token));
            if (!account.Exists) throw new ArgumentException("Cuenta desconocida.");
            var header = Header(account);
            if (shop.Id != request.ShopId || ShopRules.NeedsRefresh(shop, _shopTime.GetUtcNow()))
                throw new InvalidOperationException("Las ofertas han cambiado. Vuelve a consultar la tienda.");
            var offer = shop.Offers.SingleOrDefault(o => o.Item.Id == request.ItemId);
            if (offer == null || offer.Sold) throw new InvalidOperationException("Esta oferta ya no está disponible.");
            if (header.ItemCount >= EquipmentCatalogue.InventoryLimit) throw new InvalidOperationException("El inventario está lleno. Vende alguna pieza antes de comprar.");
            if (header.Gold < offer.Price) throw new InvalidOperationException($"Oro insuficiente. Faltan {offer.Price - header.Gold} de oro.");
            tx.Create(user.Collection("items").Document(offer.Item.Id), FirestoreData.Map(offer.Item));
            tx.Update(user, new Dictionary<string, object> { ["gold"] = header.Gold - offer.Price, ["itemCount"] = header.ItemCount + 1 });
            tx.Set(shopDoc, FirestoreData.Map(shop with { Offers = shop.Offers.Select(o => o.Item.Id == offer.Item.Id ? o with { Sold = true } : o).ToArray() }));
            tx.Create(receipt, new Dictionary<string, object> { ["shopId"] = shop.Id, ["price"] = offer.Price, ["boughtAt"] = Timestamp.GetCurrentTimestamp() });
            return true;
        }, cancellationToken: token);
    }

    public async Task<ShopAdTicket> StartShopAd(string uid, ShopAdRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId);
        var adDoc = User(uid).Collection("shop").Document("simulatedAd");
        return await database.RunTransactionAsync(async tx =>
        {
            var shop = ReadShop(await tx.GetSnapshotAsync(ShopDocument(uid), token));
            var existing = await tx.GetSnapshotAsync(adDoc, token);
            if (shop.Id != request.ShopId) throw new InvalidOperationException("El catálogo ha cambiado. Vuelve a consultar la tienda.");
            var now = _shopTime.GetUtcNow();
            if (ShopRules.NeedsRefresh(shop, now)) throw new InvalidOperationException("Las ofertas han cambiado. Vuelve a consultar la tienda.");
            if (existing.Exists)
            {
                var previous = FirestoreData.Read<AdState>(existing);
                if (!previous.Completed && previous.Ticket.ShopId == shop.Id && previous.Ticket.ExpiresAt > now) return previous.Ticket;
            }
            var ticket = new ShopAdTicket(Guid.NewGuid().ToString("N"), shop.Id, now.AddSeconds(ShopRules.SimulatedAdSeconds), now.AddMinutes(5));
            tx.Set(adDoc, FirestoreData.Map(new AdState(ticket)));
            return ticket;
        }, cancellationToken: token);
    }

    public async Task<ItemShop> CompleteShopAd(string uid, ShopAdCompleteRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId); ValidId(request.TicketId);
        var shopDoc = ShopDocument(uid); var adDoc = User(uid).Collection("shop").Document("simulatedAd");
        // Mantiene la misma tirada durante los reintentos automáticos de la transacción.
        ItemShop? replacement = null;
        return await database.RunTransactionAsync(async tx =>
        {
            var shop = ReadShop(await tx.GetSnapshotAsync(shopDoc, token));
            var ad = await tx.GetSnapshotAsync(adDoc, token);
            if (!ad.Exists) throw new InvalidOperationException("Inicia el anuncio simulado para renovar la tienda.");
            var state = FirestoreData.Read<AdState>(ad);
            if (state.Ticket.Id != request.TicketId || state.Ticket.ShopId != request.ShopId)
                throw new InvalidOperationException("El anuncio no pertenece a esta renovación.");
            if (state.Completed) return shop; // La respuesta se puede perder sin renovar dos veces.
            var now = _shopTime.GetUtcNow();
            if (now < state.Ticket.ReadyAt) throw new InvalidOperationException("El anuncio simulado todavía no ha terminado.");
            if (now >= state.Ticket.ExpiresAt) throw new InvalidOperationException("El anuncio simulado ha caducado. Inícialo de nuevo.");
            if (shop.Id != request.ShopId || ShopRules.NeedsRefresh(shop, now))
                throw new InvalidOperationException("Las ofertas han cambiado. Vuelve a consultar la tienda.");
            replacement ??= ShopRules.Generate(_loot, checked(shop.Revision + 1), now);
            tx.Set(shopDoc, FirestoreData.Map(replacement));
            tx.Set(adDoc, FirestoreData.Map(state with { Completed = true }));
            return replacement;
        }, cancellationToken: token);
    }
}
