using System.Text.Json;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public interface IAccountRepository
{
    Task<AdvertisingSettings> Advertising(CancellationToken token = default) => Task.FromResult(new AdvertisingSettings());
    Task<AdvertisingSettings> UpdateAdvertising(string uid, AdvertisingUpdate update, CancellationToken token = default) =>
        Task.FromException<AdvertisingSettings>(new NotSupportedException("El control de anuncios no está disponible."));
    Task<bool> IsDeletionRequested(string uid, CancellationToken token = default) => Task.FromResult(false);
    Task RequestDeletion(string uid, CancellationToken token = default) => Task.FromException(new NotSupportedException("Borrado no disponible."));
    Task SaveMatchStatistics(MatchStatisticsReport report, CancellationToken token = default) =>
        Task.FromException(new NotSupportedException("El repositorio no admite estadísticas."));
    Task<BestiaryTotals> SaveBestiary(BestiaryProgress progress, CancellationToken token = default) =>
        Task.FromException<BestiaryTotals>(new NotSupportedException("El repositorio no admite bestiario."));
    Task<AccountProfile> Profile(string uid, string name, CancellationToken token = default);
    Task Equip(string uid, InventoryRequest request, CancellationToken token = default);
    Task SaveLoadout(string uid, EquipmentLoadout loadout, CancellationToken token = default);
    Task UpgradeRing(string uid, RingUpgradeRequest request, CancellationToken token = default) =>
        Task.FromException(new NotSupportedException("El repositorio no admite mejoras de anillos."));
    Task<int> Sell(string uid, string itemId, CancellationToken token = default);
    Task Award(RewardMatch match, CancellationToken token = default);
    Task<ItemShop> Shop(string uid, string name, CancellationToken token = default);
    Task<bool> Buy(string uid, ShopPurchaseRequest request, CancellationToken token = default);
    Task<ShopAdTicket> StartShopAd(string uid, ShopAdRequest request, CancellationToken token = default);
    Task<ItemShop> CompleteShopAd(string uid, ShopAdCompleteRequest request, CancellationToken token = default);
    Task<ShopAdTicket> StartVerifiedShopAd(string uid, ShopAdRequest request, CancellationToken token = default) =>
        Task.FromException<ShopAdTicket>(new NotSupportedException("Anuncios no disponibles."));
    Task<ItemShop> CompleteVerifiedShopAd(string uid, ShopAdCompleteRequest request, CancellationToken token = default) =>
        Task.FromException<ItemShop>(new NotSupportedException("Anuncios no disponibles."));
    Task ConfirmVerifiedShopAd(VerifiedAdMobReward reward, CancellationToken token = default) =>
        Task.FromException(new NotSupportedException("Anuncios no disponibles."));
    Task<ItemShop> RefreshShopWithoutAd(string uid, ShopAdRequest request, CancellationToken token = default) =>
        Task.FromException<ItemShop>(new NotSupportedException("Renovación sin anuncios no disponible."));
}

internal sealed record AccountHeader(string Uid, string Name, long Gold = 0, int Wins = 0, int Losses = 0, int ItemCount = 0, int SchemaVersion = 1)
{
    public Dictionary<string, long> Bestiary { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Las escrituras cruzadas usan transacciones: inventario, saldo, equipamiento y recibo no divergen.</summary>
public sealed partial class FirestoreAccountRepository(FirestoreDb database, TimeProvider? timeProvider = null) : IAccountRepository
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
            var deleted = await transaction.GetSnapshotAsync(Deletion(uid), token);
            if (deleted.Exists) throw new UnauthorizedAccessException("La cuenta se está eliminando.");
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
            var headerSnapshot = await transaction.GetSnapshotAsync(user, token);
            var h = Header(headerSnapshot);
            var inventoryRevision = headerSnapshot.UpdateTime ?? throw new InvalidDataException("Falta la revisión del perfil.");
            EquipmentItem[] items;
            if (!CachedItems(uid, inventoryRevision, out items))
            {
                var inventory = await transaction.GetSnapshotAsync(user.Collection("items").Limit(EquipmentCatalogue.InventoryLimit + 1), token);
                items = inventory.Documents.Select(FirestoreData.Read<EquipmentItem>).OrderByDescending(i => i.CreatedAt).ToArray();
                if (items.Length > EquipmentCatalogue.InventoryLimit || items.Any(i => !EquipmentCatalogue.Valid(i))) throw new InvalidDataException("Inventario inválido.");
                CacheItems(uid, inventoryRevision, items);
            }
            var saved = await transaction.GetSnapshotAsync(user.Collection("loadouts").Document("active"), token);
            var access = await transaction.GetSnapshotAsync(AccountConfigurationDocument(uid), token);
            if (items.Length > EquipmentCatalogue.InventoryLimit || items.Any(i => !EquipmentCatalogue.Valid(i))) throw new InvalidDataException("Inventario inválido.");
            var profile = new AccountProfile(uid, h.Name, h.Gold, h.Wins, h.Losses,
                saved.Exists ? FirestoreData.Read<EquipmentLoadout>(saved) : new(), items)
                { Bestiary = h.Bestiary, Access = access.Exists ? FirestoreData.Read<AccountAccess>(access) : new() };
            _ = EquipmentModifiers.Calculate(profile); return profile;
        }, cancellationToken: token);
    }
}
