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
            GetWindow().ContentScaleSize = new(1440, 720);
            GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            GetWindow().Size = new(1600, 720);
        }
    }
    private void BuildTouchControls()
    {
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        if (!Ui.IsMobile) return;
        _touch = new MobileControls(); _ui.AddChild(_touch);
        _touch.ActionRequested += message =>
        {
            if (!_playing || _paused || _help.Visible) return;
            UpdateTouchAim();
            if (message.Type == "magick") UseMagick(message.Slot);
            else { Submit(InputMessage()); Submit(message); }
        };
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
    { if (_touch != null) _touch.Visible = _playing && !_paused && !_help.Visible; }
    public override void _Notification(int what)
    {
        if (!Ui.IsMobile || _launcher == null) return;
        if (Loading && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { ReturnToLauncher(); return; }
        if (_profiler?.Running == true && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { _profiler.Cancel(); return; }
        if (what == NotificationWMGoBackRequest)
        {
            if (GodotObject.IsInstanceValid(_equipmentPanel)) { _equipmentPanel!.QueueFree(); _equipmentPanel = null; }
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
