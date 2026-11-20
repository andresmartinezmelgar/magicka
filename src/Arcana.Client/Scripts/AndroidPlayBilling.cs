using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal sealed class AndroidPlayBilling : IDisposable
{
    private readonly GodotObject? _plugin;
    private readonly Callable _listener;
    private string _current = "";
    private TaskCompletionSource<string>? _waiting;
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
    }
    internal async Task<string> Request(string product, string uid, bool restore, CancellationToken token)
    {
        if (_plugin == null || _waiting != null) throw new InvalidOperationException("Google Play no está disponible.");
        var result = _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _current = Guid.NewGuid().ToString("N");
        try
        {
            _plugin.Call("beginPurchase", _current, product, PlayNoAdsRequest.Owner(uid), restore);
            return await result.Task.WaitAsync(TimeSpan.FromMinutes(10), token);
        }
        finally { _plugin.Call("cancelPurchase", _current); _waiting = null; _current = ""; }
    }
    public void Dispose()
    {
        if (_plugin == null) return;
        _plugin.Disconnect("billing_result", _listener);
        _waiting?.TrySetCanceled();
    }
}
