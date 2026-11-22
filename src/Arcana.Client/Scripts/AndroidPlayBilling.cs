using Arcana.Core;
using Godot;
using System.IO;

namespace Arcana.Client;

internal sealed class AndroidPlayBilling : IDisposable
{
    private readonly GodotObject? _plugin;
    private readonly Callable _listener;
    private readonly Callable _catalogueListener;
    private string _current = "";
    private TaskCompletionSource<string>? _waiting;
    private TaskCompletionSource<StorePrice[]>? _catalogueWaiting;
    internal bool Available => _plugin != null;
    internal AndroidPlayBilling()
    {
        if (!OS.HasFeature("android") || !Engine.HasSingleton("ArcanaBilling")) return;
        _plugin = Engine.GetSingleton("ArcanaBilling");
        _listener = Callable.From<string, string, string>((id, state, receipt) =>
        {
            if (_current != id || _waiting == null) return;
            if (state == "purchased") { _waiting.TrySetResult(receipt); return; }
            _waiting.TrySetException(new InvalidOperationException(state switch
            {
                "closed" => "Compra cancelada.",
                "none" => "Google Play no encuentra una compra para esta cuenta.",
                "pending" => "Pago pendiente en Google Play. Puedes restaurarlo cuando se complete.",
                "other_account" => "La compra está vinculada a otra cuenta del juego.",
                _ => "Google Play no está disponible para este producto en el dispositivo."
            }));
        });
        _plugin.Connect("billing_result", _listener);
        _catalogueListener = Callable.From<string, string>((id, json) =>
        {
            if (_current != id || _catalogueWaiting == null) return;
            try
            {
                var prices = Protocol.Deserialize<StorePrice[]>(json) ?? [];
                if (prices.Length > 12 || prices.Any(p => p == null || p.Key.Length > 40 || p.Price.Length is < 1 or > 80))
                    throw new InvalidDataException("Precios de Google Play inválidos.");
                _catalogueWaiting.TrySetResult(prices);
            }
            catch (Exception error) { _catalogueWaiting.TrySetException(error); }
        });
        _plugin.Connect("billing_catalogue", _catalogueListener);
    }
    internal Task<string> Request(string product, string uid, bool restore, CancellationToken token) =>
        Request(StoreCatalogue.Products(product).Single(p => p.Key == "no_ads_forever"), uid, restore, token);

    internal async Task<string> Request(StoreProduct product, string uid, bool restore, CancellationToken token)
    {
        if (_plugin == null || _waiting != null || _catalogueWaiting != null) throw new InvalidOperationException("Google Play no está disponible.");
        var result = _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = Guid.NewGuid().ToString("N");
        try
        {
            _plugin.Call("beginStorePurchase", _current, product.ProductId, product.ProductType, product.BasePlanId, PlayNoAdsRequest.Owner(uid), restore);
            return await result.Task.WaitAsync(TimeSpan.FromMinutes(10), token);
        }
        finally { _plugin.Call("cancelPurchase", _current); _waiting = null; _current = ""; }
    }
    internal async Task<StorePrice[]> Prices(StoreProduct[] products, CancellationToken token)
    {
        if (_plugin == null || _waiting != null || _catalogueWaiting != null || products.Length == 0) return [];
        var result = _catalogueWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = Guid.NewGuid().ToString("N");
        try
        {
            _plugin.Call("queryCatalogue", _current, Protocol.Serialize(products));
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        finally { _plugin.Call("cancelPurchase", _current); _catalogueWaiting = null; _current = ""; }
    }
    public void Dispose()
    {
        if (_plugin == null) return;
        _plugin.Disconnect("billing_result", _listener);
        _plugin.Disconnect("billing_catalogue", _catalogueListener);
        _waiting?.TrySetCanceled();
        _catalogueWaiting?.TrySetCanceled();
    }
}
