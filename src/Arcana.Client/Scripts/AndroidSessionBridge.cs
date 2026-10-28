using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Retiene el proceso de la partida; no ejecuta ni reanuda la simulación.</summary>
internal sealed class AndroidSessionBridge : IDisposable
{
    private readonly GodotObject? _plugin;
    private readonly Callable _status;
    private (bool Active, bool Online)? _last;

    internal AndroidSessionBridge()
    {
        if (!OS.HasFeature("android")) return;
        if (!Engine.HasSingleton("ArcanaSession"))
        {
            Diagnostics.Warn("android.session.unavailable", "Esta APK no incluye la retención de partidas en segundo plano.");
            return;
        }
        _plugin = Engine.GetSingleton("ArcanaSession");
        _status = Callable.From<string, string>((state, message) =>
        {
            if (state == "failed") Diagnostics.Warn("android.session.failed", message);
            else Diagnostics.Info("android.session." + state, message);
        });
        _plugin.Connect("session_status", _status);
    }

    internal void Synchronize(bool active, bool online)
    {
        if (_plugin == null || !GodotObject.IsInstanceValid(_plugin) || _last == (active, online)) return;
        _plugin.Call("setSessionActive", active, online);
        _last = (active, online);
    }

    public void Dispose()
    {
        Synchronize(false, false);
        if (_plugin != null && GodotObject.IsInstanceValid(_plugin)) _plugin.Disconnect("session_status", _status);
    }
}
