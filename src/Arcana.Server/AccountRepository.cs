using System.Text.Json;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public interface IAccountRepository
{
    Task<AccountProfile> Profile(string uid, string name, CancellationToken token = default);
    Task Equip(string uid, InventoryRequest request, CancellationToken token = default);
    Task<int> Sell(string uid, string itemId, CancellationToken token = default);
    Task Award(RewardMatch match, CancellationToken token = default);
}

internal static class FirestoreData
{
    public static Dictionary<string, object> Map<T>(T value) => (Dictionary<string, object>)Convert(JsonSerializer.SerializeToElement(value, Protocol.Json));
    private static object Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToArray(),
        JsonValueKind.String => value.GetString()!, JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out long n) ? (object)n : value.GetDouble(), _ => null!
    };
    public static T Read<T>(DocumentSnapshot snapshot) => Protocol.Deserialize<T>(JsonSerializer.Serialize(snapshot.ToDictionary(), Protocol.Json))
        ?? throw new InvalidDataException("Documento de cuenta inválido.");
}
internal sealed record AccountHeader(string Uid, string Name, long Gold = 0, int Wins = 0, int Losses = 0, int ItemCount = 0, int SchemaVersion = 1);

/// <summary>Las escrituras cruzadas usan transacciones: inventario, saldo, equipamiento y recibo no divergen.</summary>
public sealed class FirestoreAccountRepository(FirestoreDb database) : IAccountRepository
{
    private readonly LootRandom _loot = new();
    private readonly SemaphoreSlim _catalogueGate = new(1, 1);
    private bool _catalogueReady;
    private async Task PublishCatalogue(CancellationToken token)
    {
        await _catalogueGate.WaitAsync(token);
        try
        {
            if (_catalogueReady) return;
            var batch = database.StartBatch();
            foreach (var definition in EquipmentCatalogue.Definitions())
            {
                var data = FirestoreData.Map(definition.Data);
                batch.Set(database.Collection("bonusDefinitions").Document(definition.Id), data);
                batch.Set(database.Collection("catalogues").Document(EquipmentCatalogue.GenerationVersion.ToString())
                    .Collection("bonusDefinitions").Document(definition.Id), data);
            }
            await batch.CommitAsync(token); _catalogueReady = true;
        }
        finally { _catalogueGate.Release(); }
    }
    private DocumentReference User(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 128 || uid.Contains('/') || uid.Any(char.IsControl)) throw new ArgumentException("Identificador de cuenta inválido.");
        return database.Collection("users").Document(uid);
    }
    private static AccountHeader Header(DocumentSnapshot doc)
    {
        var value = FirestoreData.Read<AccountHeader>(doc);
        if (value.SchemaVersion != 1 || value.Gold < 0 || value.ItemCount is < 0 or > EquipmentCatalogue.InventoryLimit)
            throw new InvalidDataException("Versión o saldo de cuenta inválido.");
        return value;
    }
    private async Task EnsureUser(string uid, string name, CancellationToken token)
    {
        await PublishCatalogue(token);
        var doc = User(uid);
        await database.RunTransactionAsync(async transaction =>
        {
            var existing = await transaction.GetSnapshotAsync(doc, token);
            if (!existing.Exists) transaction.Create(doc, FirestoreData.Map(new AccountHeader(uid, name)));
            else _ = Header(existing);
        }, cancellationToken: token);
    }
    public async Task<AccountProfile> Profile(string uid, string name, CancellationToken token = default)
    {
        await EnsureUser(uid, name, token);
        var user = User(uid);
        return await database.RunTransactionAsync(async transaction =>
        {
            var h = Header(await transaction.GetSnapshotAsync(user, token));
            var inventory = await transaction.GetSnapshotAsync(user.Collection("items").Limit(EquipmentCatalogue.InventoryLimit + 1), token);
            var saved = await transaction.GetSnapshotAsync(user.Collection("loadouts").Document("active"), token);
            var items = inventory.Documents.Select(FirestoreData.Read<EquipmentItem>).OrderByDescending(i => i.CreatedAt).ToArray();
            if (items.Length > EquipmentCatalogue.InventoryLimit || items.Any(i => !EquipmentCatalogue.Valid(i))) throw new InvalidDataException("Inventario inválido.");
            var profile = new AccountProfile(uid, h.Name, h.Gold, h.Wins, h.Losses,
                saved.Exists ? FirestoreData.Read<EquipmentLoadout>(saved) : new(), items);
            _ = EquipmentModifiers.Calculate(profile); return profile;
        }, cancellationToken: token);
    }
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
    public async Task<int> Sell(string uid, string itemId, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(itemId, "N", out _)) throw new ArgumentException("Objeto inválido.");
        var user = User(uid); var itemRef = user.Collection("items").Document(itemId);
        var active = user.Collection("loadouts").Document("active"); var receipt = user.Collection("sales").Document(itemId);
        return await database.RunTransactionAsync(async transaction =>
        {
            var previous = await transaction.GetSnapshotAsync(receipt, token);
            if (previous.Exists) return 0; // Reintentar una respuesta perdida no vuelve a pagar oro.
            var itemDoc = await transaction.GetSnapshotAsync(itemRef, token);
            var accountDoc = await transaction.GetSnapshotAsync(user, token);
            var equippedDoc = await transaction.GetSnapshotAsync(active, token);
            if (!itemDoc.Exists || !accountDoc.Exists) throw new ArgumentException("El objeto ya no está en tu inventario.");
            var item = FirestoreData.Read<EquipmentItem>(itemDoc); var h = Header(accountDoc);
            if (!EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Objeto inválido.");
            var loadout = equippedDoc.Exists ? FirestoreData.Read<EquipmentLoadout>(equippedDoc) : new();
            if (loadout.Get(item.Slot) == itemId) transaction.Set(active, FirestoreData.Map(loadout.With(item.Slot, "")));
            transaction.Delete(itemRef);
            transaction.Update(user, new Dictionary<string, object> { ["gold"] = checked(h.Gold + item.SaleGold), ["itemCount"] = Math.Max(0, h.ItemCount - 1) });
            transaction.Create(receipt, new Dictionary<string, object> { ["gold"] = item.SaleGold, ["soldAt"] = Timestamp.GetCurrentTimestamp() });
            return item.SaleGold;
        }, cancellationToken: token);
    }
    public async Task Award(RewardMatch match, CancellationToken token = default)
    {
        var eligible = RewardRules.Eligible(match);
        if (eligible.Length == 0) return;
        var matchRef = database.Collection("matches").Document(match.Id);
        await database.RunTransactionAsync(async transaction =>
        {
            var existing = await transaction.GetSnapshotAsync(matchRef, token);
            if (!existing.Exists) transaction.Create(matchRef, FirestoreData.Map(match));
        }, cancellationToken: token);
        foreach (var player in eligible)
        {
            // La tirada se mantiene durante todos los reintentos de esta transacción.
            var item = player.Winner ? EquipmentCatalogue.Generate(_loot, match.Id) : null;
            var user = User(player.Uid); var receipt = user.Collection("rewards").Document(match.Id);
            await database.RunTransactionAsync(async transaction =>
            {
                var previous = await transaction.GetSnapshotAsync(receipt, token);
                var account = await transaction.GetSnapshotAsync(user, token);
                if (previous.Exists) return;
                if (!account.Exists) throw new InvalidDataException("Falta la cuenta del participante.");
                var h = Header(account);
                bool full = h.ItemCount >= EquipmentCatalogue.InventoryLimit;
                int gold = item != null && full ? item.SaleGold : 0;
                if (item != null && !full) transaction.Create(user.Collection("items").Document(item.Id), FirestoreData.Map(item));
                transaction.Update(user, new Dictionary<string, object>
                {
                    ["wins"] = checked(h.Wins + (player.Winner ? 1 : 0)), ["losses"] = checked(h.Losses + (player.Winner ? 0 : 1)),
                    ["itemCount"] = h.ItemCount + (item != null && !full ? 1 : 0), ["gold"] = checked(h.Gold + gold)
                });
                transaction.Create(receipt, new Dictionary<string, object>
                { ["winner"] = player.Winner, ["itemId"] = item != null && !full ? item.Id : "", ["gold"] = gold, ["grantedAt"] = Timestamp.GetCurrentTimestamp() });
            }, cancellationToken: token);
        }
    }
}
