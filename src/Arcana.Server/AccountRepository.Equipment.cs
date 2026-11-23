using System.Text.Json;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    public async Task Equip(string uid, InventoryRequest request, CancellationToken token = default)
    {
        if (!Enum.IsDefined(request.Slot) || request.ItemId == null || request.ItemId.Length > 32) throw new ArgumentException("Selección inválida.");
        var user = User(uid); var active = user.Collection("loadouts").Document("active");
        await database.RunTransactionAsync(async transaction =>
        {
            var header = await transaction.GetSnapshotAsync(user, token);
            if (!header.Exists) throw new ArgumentException("Cuenta desconocida.");
            _ = Header(header);
            var saved = await transaction.GetSnapshotAsync(active, token);
            var loadout = saved.Exists ? FirestoreData.Read<EquipmentLoadout>(saved) : new();
            if (request.ItemId.Length > 0)
            {
                if (!Guid.TryParseExact(request.ItemId, "N", out _)) throw new ArgumentException("Objeto inválido.");
                var item = await transaction.GetSnapshotAsync(user.Collection("items").Document(request.ItemId), token);
                if (!item.Exists) throw new ArgumentException("El objeto no está en tu inventario.");
                var value = FirestoreData.Read<EquipmentItem>(item);
                if (value.Slot != request.Slot || !EquipmentCatalogue.Valid(value)) throw new ArgumentException("El objeto no corresponde a esta posición.");
            }
            transaction.Set(active, FirestoreData.Map(loadout.With(request.Slot, request.ItemId)));
        }, cancellationToken: token);
    }

    public async Task SaveLoadout(string uid, EquipmentLoadout loadout, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            string id = loadout.Get(slot);
            if (id == null || (id.Length > 0 && !Guid.TryParseExact(id, "N", out _)))
                throw new ArgumentException("Selección inválida.");
        }
        var user = User(uid);
        await database.RunTransactionAsync(async transaction =>
        {
            var header = await transaction.GetSnapshotAsync(user, token);
            if (!header.Exists) throw new ArgumentException("Cuenta desconocida.");
            _ = Header(header);
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                string id = loadout.Get(slot);
                if (id.Length == 0) continue;
                var item = await transaction.GetSnapshotAsync(user.Collection("items").Document(id), token);
                if (!item.Exists) throw new ArgumentException("El objeto no está en tu inventario.");
                var value = FirestoreData.Read<EquipmentItem>(item);
                if (value.Slot != slot || !EquipmentCatalogue.Valid(value))
                    throw new ArgumentException("El objeto no corresponde a esta posición.");
            }
            transaction.Set(user.Collection("loadouts").Document("active"), FirestoreData.Map(loadout));
        }, cancellationToken: token);
    }

    public async Task UpgradeRing(string uid, RingUpgradeRequest request, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(request.ItemId, "N", out _)) throw new ArgumentException("Anillo inválido.");
        var user = User(uid); var itemRef = user.Collection("items").Document(request.ItemId);
        await database.RunTransactionAsync(async transaction =>
        {
            var account = await transaction.GetSnapshotAsync(user, token);
            var stored = await transaction.GetSnapshotAsync(itemRef, token);
            if (!account.Exists || !stored.Exists) throw new ArgumentException("El anillo ya no está en tu inventario.");
            var header = Header(account); var item = FirestoreData.Read<EquipmentItem>(stored);
            var profile = new AccountProfile(uid, header.Name, header.Gold, header.Wins, header.Losses, new(), [item]);
            var upgraded = AccountInventory.UpgradeRing(profile, request);
            if (upgraded == profile) return;
            transaction.Set(itemRef, FirestoreData.Map(upgraded.Items.Single()));
            transaction.Update(user, new Dictionary<string, object> { ["inventoryUpdatedAt"] = FieldValue.ServerTimestamp, ["gold"] = upgraded.Gold });
        }, cancellationToken: token);
    }

    public async Task<int> Sell(string uid, string itemId, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(itemId, "N", out _)) throw new ArgumentException("Objeto inválido.");
        var user = User(uid); var itemRef = user.Collection("items").Document(itemId);
        var active = user.Collection("loadouts").Document("active"); var receipt = user.Collection("sales").Document(itemId);
        Timestamp? soldFrom = null; EquipmentItem? soldItem = null;
        int result = await database.RunTransactionAsync(async transaction =>
        {
            soldFrom = null; soldItem = null;
            var previous = await transaction.GetSnapshotAsync(receipt, token);
            if (previous.Exists) return 0; // Reintentar una respuesta perdida no vuelve a pagar oro.
            var itemDoc = await transaction.GetSnapshotAsync(itemRef, token);
            var accountDoc = await transaction.GetSnapshotAsync(user, token);
            var equippedDoc = await transaction.GetSnapshotAsync(active, token);
            if (!itemDoc.Exists || !accountDoc.Exists) throw new ArgumentException("El objeto ya no está en tu inventario.");
            var item = FirestoreData.Read<EquipmentItem>(itemDoc); var h = Header(accountDoc);
            if (!EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Objeto inválido.");
            var loadout = equippedDoc.Exists ? FirestoreData.Read<EquipmentLoadout>(equippedDoc) : new();
            if (loadout.Get(item.Slot) == itemId) throw new ArgumentException("Desequipa y guarda el equipo antes de vender este objeto.");
            transaction.Delete(itemRef);
            transaction.Update(user, new Dictionary<string, object> { ["inventoryUpdatedAt"] = FieldValue.ServerTimestamp, ["gold"] = checked(h.Gold + item.SaleGold), ["itemCount"] = Math.Max(0, h.ItemCount - 1) });
            transaction.Create(receipt, new Dictionary<string, object> { ["gold"] = item.SaleGold, ["soldAt"] = Timestamp.GetCurrentTimestamp() });
            soldFrom = accountDoc.UpdateTime; soldItem = item;
            return item.SaleGold;
        }, cancellationToken: token);
        if (soldFrom is { } before && soldItem != null)
        {
            try
            {
                // Recibo y cabecera se escriben en el mismo commit. Una
                // mutación posterior cambia UpdateTime e invalida esta caché.
                var delivered = await receipt.GetSnapshotAsync(token);
                if (delivered.UpdateTime is { } after) CacheSale(uid, before, after, soldItem);
            }
            catch (Exception) { _inventoryCache.TryRemove(uid, out _); }
        }
        return result;
    }
}
