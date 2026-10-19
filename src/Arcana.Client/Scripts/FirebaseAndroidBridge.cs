using Godot;

namespace Arcana.Client;

/// <summary>Godot Android plugin v2. FirebaseAuth SDK stores refresh credentials in Android private storage.</summary>
public sealed class FirebaseAndroidBridge : IDisposable
{
    public sealed record Session(string Uid, string Name, string IdToken, DateTimeOffset ExpiresAt);
    private readonly GodotObject? _plugin;
    private readonly Callable _sessionCallable, _errorCallable, _logoutCallable;
    public event Action<Session>? SessionReceived;
    public event Action<string>? Error;
    public event Action? SignedOut;
    public bool Available => _plugin != null;
    public FirebaseAndroidBridge()
    {
        if (!OS.HasFeature("android") || !Engine.HasSingleton("ArcanaFirebase")) return;
        _plugin = Engine.GetSingleton("ArcanaFirebase");
        _sessionCallable = Callable.From<string, string, string, string, string>((uid, name, token, expiry, generation) =>
        {
            if (!Current(generation)) return;
            if (long.TryParse(expiry, out long seconds) && seconds > 0)
                SessionReceived?.Invoke(new Session(uid, name, token, DateTimeOffset.FromUnixTimeSeconds(seconds)));
            else Error?.Invoke("Firebase ha devuelto una fecha de caducidad incorrecta.");
        });
        _errorCallable = Callable.From<string, string>((error, generation) => { if (Current(generation)) Error?.Invoke(error); });
        _logoutCallable = Callable.From<string>(generation => { if (Current(generation)) SignedOut?.Invoke(); });
        _plugin.Connect("session_ready", _sessionCallable);
        _plugin.Connect("login_failed", _errorCallable);
        _plugin.Connect("session_cleared", _logoutCallable);
    }
    private bool Current(string generation) => _plugin?.Call("sessionGeneration").AsString() == generation;
    public void Restore() => _plugin?.Call("requestSession");
    public void Login() => _plugin?.Call("signIn");
    public void Renew() => _plugin?.Call("refreshSession");
    public void Logout() => _plugin?.Call("signOut");
    public void Dispose()
    {
        if (_plugin == null) return;
        if (GodotObject.IsInstanceValid(_plugin))
        {
            _plugin.Disconnect("session_ready", _sessionCallable);
            _plugin.Disconnect("login_failed", _errorCallable);
            _plugin.Disconnect("session_cleared", _logoutCallable);
        }
    }
}
