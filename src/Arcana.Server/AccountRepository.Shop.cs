using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
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
        ItemShop? initial = null;
        return await database.RunTransactionAsync(async tx =>
        {
            var existing = await tx.GetSnapshotAsync(doc, token);
            if (existing.Exists) return ReadShop(existing);
            initial ??= ShopRules.Generate(_loot);
            tx.Create(doc, FirestoreData.Map(initial));
            return initial;
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
            if (shop.Id != request.ShopId) throw new InvalidOperationException("El catálogo ha cambiado. Vuelve a consultar la tienda.");
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
            var now = DateTimeOffset.UtcNow;
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
            var now = DateTimeOffset.UtcNow;
            if (now < state.Ticket.ReadyAt) throw new InvalidOperationException("El anuncio simulado todavía no ha terminado.");
            if (now >= state.Ticket.ExpiresAt) throw new InvalidOperationException("El anuncio simulado ha caducado. Inícialo de nuevo.");
            if (shop.Id != request.ShopId) throw new InvalidOperationException("El catálogo ha cambiado. Vuelve a consultar la tienda.");
            replacement ??= ShopRules.Generate(_loot, checked(shop.Revision + 1));
            tx.Set(shopDoc, FirestoreData.Map(replacement));
            tx.Set(adDoc, FirestoreData.Map(state with { Completed = true }));
            return replacement;
        }, cancellationToken: token);
    }
}
