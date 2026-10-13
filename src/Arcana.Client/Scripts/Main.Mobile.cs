using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MobileControls? _touch;

    private void PrepareMobileWindow()
    {
        if (!Ui.IsMobile) return;
        GetTree().AutoAcceptQuit = false;
        if (!OS.HasFeature("mobile"))
        {
            int logicalWidth = 1440, logicalHeight = 720, physicalWidth = 1600;
            // Solo pruebas headless: simular pantallas más estrechas sin alterar la APK.
            string? requested = OS.GetCmdlineUserArgs().FirstOrDefault(a =>
                a.StartsWith("--smoke-viewport=", StringComparison.Ordinal));
            if (requested != null && OS.GetCmdlineUserArgs().Contains("--mobile-menu-smoke"))
            {
                string[] parts = requested["--smoke-viewport=".Length..].Split('x');
                if (parts.Length != 2 || !int.TryParse(parts[0], out logicalWidth) ||
                    !int.TryParse(parts[1], out logicalHeight) ||
                    logicalWidth is < 640 or > 2560 || logicalHeight is < 450 or > 1440)
                    throw new ArgumentException("Tamaño de pantalla de prueba inválido.");
                physicalWidth = logicalWidth;
            }
            GetWindow().ContentScaleSize = new(logicalWidth, logicalHeight);
            GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            GetWindow().Size = new(physicalWidth, logicalHeight);
        }
    }
    private void BuildTouchControls()
    {
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        if (!Ui.IsMobile) return;
        _touch = new MobileControls(); _ui.AddChild(_touch);
        _touch.ActionRequested += message =>
        {
            if (!_playing || _paused || _help.Visible || InspectingEquipment) return;
            UpdateTouchAim();
            if (message.Type == "magick") UseMagick(message.Slot);
            else { Submit(InputMessage()); Submit(message); }
        };
        _touch.AimChanged += () => { if (_playing && !_paused && !_help.Visible && !InspectingEquipment) { UpdateTouchAim(); Submit(InputMessage()); } };
        _touch.HelpRequested += ToggleHelp;
        _touch.MenuRequested += TogglePause;
        _touch.ScoreRequested += () => { if (_hud != null) { StopInput(); _hud.ScoreOpen = !_hud.ScoreOpen; } };
    }
    private void UpdateTouchAim()
    {
        if (_touch == null) return;
        var player = (_connection?.Latest ?? _snapshot)?.Players.FirstOrDefault(p => p.Id == _localId);
        if (player == null) return;
        Vector2 center = GetViewport().GetVisibleRect().Size / 2;
        V2 direction = _renderer.Cursor(center + _touch.AimDirection * 100) - _renderer.Cursor(center);
        if (direction.Length > 0.01f) _aim = player.Position + direction.Normalized * 14;
    }
    private void UpdateTouchVisibility()
    { if (_touch != null) _touch.Visible = _playing && !_paused && !_help.Visible && !InspectingEquipment; }
    public override void _Notification(int what)
    {
        if (!Ui.IsMobile || _launcher == null) return;
        if (Loading && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { ReturnToLauncher(); return; }
        if (_profiler?.Running == true && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { _profiler.Cancel(); return; }
        if (what == NotificationWMGoBackRequest)
        {
            if (InspectingEquipment) ClosePlayerEquipment();
            else if (GodotObject.IsInstanceValid(_equipmentPanel)) { _equipmentPanel!.QueueFree(); _equipmentPanel = null; }
            else if (GodotObject.IsInstanceValid(_optionAHelp)) CloseOptionAHelp();
            else if (_help.Visible) { _help.Visible = false; UpdateTouchVisibility(); }
            else if (_playing) TogglePause();
            else if (!_launcher.HandleBack()) GetTree().Quit();
        }
        if ((long)what is NotificationApplicationPaused or NotificationApplicationFocusOut && _playing)
        {
            StopInput();
            if (!_paused && !_help.Visible) TogglePause();
        }
    }
}
