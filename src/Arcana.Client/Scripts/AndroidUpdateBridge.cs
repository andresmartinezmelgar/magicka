using Godot;

namespace Arcana.Client;

public sealed class AndroidUpdateBridge : IDisposable
{
    private readonly GodotObject? _plugin;
    private readonly Callable _status;
    public event Action<string, string>? Status;
    public AndroidUpdateBridge()
    {
        if (!OS.HasFeature("android") || !Engine.HasSingleton("ArcanaUpdates")) return;
        _plugin = Engine.GetSingleton("ArcanaUpdates");
        _status = Callable.From<string, string>((state, message) => Status?.Invoke(state, message));
        _plugin.Connect("update_status", _status);
    }
    public void Install(string path, string version, string sha256)
    {
        if (_plugin == null) throw new InvalidOperationException("Esta APK no incluye el instalador de actualizaciones.");
        var parts = version.Split('.').Select(int.Parse).ToArray();
        int code = checked(parts[0] * 1_000_000 + parts[1] * 1000 + parts[2]);
        _plugin.Call("installUpdate", path, code.ToString(System.Globalization.CultureInfo.InvariantCulture), sha256);
    }
    public void Dispose()
    {
        if (_plugin != null && GodotObject.IsInstanceValid(_plugin)) _plugin.Disconnect("update_status", _status);
    }
}
