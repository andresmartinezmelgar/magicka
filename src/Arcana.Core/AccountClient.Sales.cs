using System.Text.Json;

namespace Arcana.Core;

public sealed partial class AccountClient
{
    private bool _inventoryNeedsRefresh;
    private long _inventoryRevision;
    private sealed class AccountApiException(System.Net.HttpStatusCode status, string message) : InvalidOperationException(message)
    { public System.Net.HttpStatusCode Status { get; } = status; }
    public bool InventoryNeedsRefresh { get { lock (_identityGate) return _inventoryNeedsRefresh; } }
    private void ResetInventorySync() { _inventoryNeedsRefresh = false; _inventoryRevision++; }

    public async Task<InventoryResult> UpgradeRing(string itemId, int expectedLevel, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        lock (_identityGate)
        {
            RequireIdentity(identity.Generation);
            if (_inventoryNeedsRefresh) throw new InvalidOperationException("Actualiza el inventario antes de volver a mejorar.");
        }
        try
        {
            var result = await Request<InventoryResult>(HttpMethod.Post, "account/ring/upgrade", new RingUpgradeRequest(itemId, expectedLevel), token, identity).ConfigureAwait(false);
            PublishProfile(result.Profile, identity.Generation); return result;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException ||
            error is AccountApiException api && (int)api.Status >= 500)
        {
            lock (_identityGate)
                if (_identityGeneration == identity.Generation) { _inventoryNeedsRefresh = true; _inventoryRevision++; }
            throw;
        }
    }

    public Task<InventoryResult> Sell(string itemId, CancellationToken token = default) => SellChecked(itemId, null, null, token);

    internal Task<InventoryResult> SellAutomatically(string itemId, string owner, string session, CancellationToken token) =>
        SellChecked(itemId, owner, session, token);

    private async Task<InventoryResult> SellChecked(string itemId, string? owner, string? session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var identity = CaptureIdentity();
        lock (_identityGate)
        {
            RequireIdentity(identity.Generation);
            if (owner != null && (Profile?.Uid != owner || Token != session))
                throw new InvalidOperationException("La cuenta ha cambiado durante la venta automática.");
            if (owner != null && Profile?.Items.Any(i => i.Id == itemId && i.Tier == ItemTier.Mythic) == true)
                throw new InvalidOperationException("Los objetos Míticos solo se venden manualmente.");
            if (_inventoryNeedsRefresh) throw new InvalidOperationException("Actualiza el inventario para confirmar la venta anterior antes de volver a vender.");
        }
        try
        {
            var result = await Request<InventoryResult>(HttpMethod.Post, "account/sell", new InventoryRequest(itemId), token, identity).ConfigureAwait(false);
            PublishProfile(result.Profile, identity.Generation); return result;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException ||
            error is AccountApiException api && (int)api.Status >= 500)
        {
            // El servidor puede haber aplicado la venta aunque no llegue la respuesta.
            // Nunca reenviar una escritura con ese inventario hasta leerlo de nuevo.
            lock (_identityGate)
                if (_identityGeneration == identity.Generation) { _inventoryNeedsRefresh = true; _inventoryRevision++; }
            throw;
        }
    }
}
