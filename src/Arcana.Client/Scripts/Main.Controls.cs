using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MobileSettingsPanel? _controlsSettings;
    private TouchSettings? _touchSettings;
    private bool ControlsOpen => GodotObject.IsInstanceValid(_controlsSettings);
    internal void OpenTouchSettings()
    {
        if (ControlsOpen) return;
        StopInput();
        _touchSettings ??= (DevicePreferences.Read<TouchSettings>("touch-controls") ?? new()).Normalize();
        _controlsSettings = new MobileSettingsPanel(); _ui.AddChild(_controlsSettings);
        _controlsSettings.Build(_touchSettings, _snapshot, _localId, AppendEditorTouchAreas, _playing ? null : this);
        _controlsSettings.Saved += settings => { _touchSettings = settings; _touch?.Configure(settings); CloseTouchSettings(); };
        _controlsSettings.Canceled += CloseTouchSettings;
        UpdateTouchVisibility();
    }
    private void CloseTouchSettings()
    {
        _controlsSettings?.QueueFree(); _controlsSettings = null;
        _touch?.Reset(); UpdateTouchVisibility();
    }
}
