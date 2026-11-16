using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal sealed class AndroidRewardedAds : IDisposable
{
    private readonly GodotObject? _plugin;
    private readonly Callable _result;
    private TaskCompletionSource<bool>? _waiting;
    private bool _privacyPrepared;
    internal AndroidRewardedAds()
    {
        if (!OS.HasFeature("android") || !Engine.HasSingleton("ArcanaAds")) return;
        _plugin = Engine.GetSingleton("ArcanaAds");
        _result = Callable.From<string, string>((status, message) =>
        {
            if (status == "rewarded") _waiting?.TrySetResult(true);
            else if (status == "closed") _waiting?.TrySetResult(false);
            else _waiting?.TrySetException(new InvalidOperationException(message));
        });
        _plugin.Connect("rewarded_result", _result);
    }
    internal bool Supports(RewardedAdsConfiguration? config)
    {
        if (_plugin == null || config == null || _plugin.Call("configuredAppId").AsString() != config.AppId) return false;
        if (!_privacyPrepared) { _privacyPrepared = true; _plugin.Call("preparePrivacy"); }
        return true;
    }
    internal void PrivacyOptions() => _plugin?.Call("showPrivacyOptions");
    internal bool PrivacyRequired => _plugin != null && _plugin.Call("privacyOptionsRequired").AsBool();
    internal async Task<bool> Show(ShopAdTicket ticket, CancellationToken token)
    {
        if (_plugin == null || ticket.AdUnitId == null || ticket.UserId == null) throw new InvalidOperationException("Anuncios no disponibles en este dispositivo.");
        if (_waiting != null) throw new InvalidOperationException("Ya hay un anuncio en curso.");
        var waiting = _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = token.Register(() => waiting.TrySetCanceled(token));
        try
        {
            _plugin.Call("showRewarded", ticket.AdUnitId, ticket.UserId, ticket.Id);
            return await waiting.Task.WaitAsync(TimeSpan.FromMinutes(30), token);
        }
        finally
        {
            if (_waiting == waiting) _waiting = null;
            if (GodotObject.IsInstanceValid(_plugin)) _plugin.CallDeferred("cancelRewarded");
        }
    }
    public void Dispose()
    {
        _waiting?.TrySetCanceled();
        if (_plugin != null && GodotObject.IsInstanceValid(_plugin)) _plugin.Disconnect("rewarded_result", _result);
    }
}
