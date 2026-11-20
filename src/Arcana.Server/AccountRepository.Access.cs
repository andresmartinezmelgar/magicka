using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    private DocumentReference AccountConfigurationDocument(string uid) => User(uid).Collection("configuration").Document("account");

    public Task<ItemShop> RefreshShopWithoutAd(string uid, ShopAdRequest request, CancellationToken token = default)
    {
        ValidId(request.ShopId);
        ItemShop? replacement = null;
        return database.RunTransactionAsync(async tx =>
        {
            // Leer la exención dentro de la transacción. El perfil o el teléfono
            // nunca pueden afirmar que se ha pagado ni elegir el límite diario.
            var access = await tx.GetSnapshotAsync(AccountConfigurationDocument(uid), token);
            if (!access.Exists || !FirestoreData.Read<AccountAccess>(access).WithoutAds(_shopTime.GetUtcNow()))
                throw new InvalidOperationException("Esta cuenta no tiene activada la opción sin anuncios.");
            var doc = ShopDocument(uid);
            var shop = ReadShop(await tx.GetSnapshotAsync(doc, token));
            var now = _shopTime.GetUtcNow();
            // Idempotencia ante pérdida de respuesta o dos dispositivos. Solo la
            // renovación que sustituyó ese catálogo se puede volver a confirmar.
            if (shop.Id != request.ShopId)
            {
                if (shop.RewardedRefreshAvailableAt > now && shop.PreviousRefreshShopId == request.ShopId) return shop;
                throw new InvalidOperationException("Las ofertas han cambiado. Consulta de nuevo la tienda.");
            }
            if (ShopRules.NeedsRefresh(shop, now))
                throw new InvalidOperationException("Las ofertas han cambiado. Consulta de nuevo la tienda.");
            if (shop.RewardedRefreshAvailableAt > now)
                throw new InvalidOperationException("Ya has utilizado la renovación extra de hoy.");
            if (replacement == null || replacement.RefreshAt != ShopRules.NextRefreshAt(now))
                replacement = ShopRules.Generate(_loot, checked(shop.Revision + 1), now) with
                { RewardedRefreshAvailableAt = ShopRules.NextRefreshAt(now), PreviousRefreshShopId = shop.Id };
            tx.Set(doc, FirestoreData.Map(replacement));
            return replacement;
        }, cancellationToken: token);
    }
}
