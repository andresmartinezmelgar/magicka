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
            // Simular pantallas de prueba más estrechas sin alterar la APK.
            string? requested = OS.GetCmdlineUserArgs().FirstOrDefault(a =>
                a.StartsWith("--smoke-viewport=", StringComparison.Ordinal));
            if (requested != null && OS.GetCmdlineUserArgs().Any(a => a is "--defense-smoke" or "--mobile-menu-smoke"
                or "--touch-layout-smoke" or "--practice-smoke"
                or "--aim-marker-smoke"
                or "--regression-smoke" or "--shop-smoke" or "--sale-smoke" or "--usability-smoke"
                or "--equipment-smoke" or "--preview-equipment-picker"
                or "--preview-spells=stone" or "--preview-spells=stone-close" or "--preview-spells=stone-auras"
                or "--preview-spells=areas" or "--preview-spells=auras" or "--preview-spells=passives"))
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
        _touchSettings ??= (DevicePreferences.Read<TouchSettings>("touch-controls") ?? new()).Normalize();
        _touch.Configure(_touchSettings);
        _touch.MagickAvailable = slot => CheckMagickAvailable(slot, checkTarget: false);
        _touch.ActionRequested += message =>
        {
            if (!_playing || _paused || _help.Visible || InspectingEquipment || ControlsOpen || _waitingPanel?.Visible == true) return;
            UpdateTouchAim();
            if (message.Type == "magick") UseMagick(message.Slot);
            else { Submit(InputMessage()); Submit(message); }
        };
        _touch.AimChanged += () => { if (_playing && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && _waitingPanel?.Visible != true) { UpdateTouchAim(); Submit(InputMessage()); } };
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
        if (direction.Length > 0.01f) _aim = player.Position + direction.Normalized * (_touch.AimRange * _touch.AimStrength);
    }
    private void UpdateTouchVisibility()
    {
        if (_touch == null) return;
        bool visible = _playing && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && _waitingPanel?.Visible != true;
        if (_touch.Visible && !visible) _touch.Reset();
        _touch.Visible = visible;
    }
    public override void _Notification(int what)
    {
        if (!Ui.IsMobile || _launcher == null) return;
        if (Loading && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { ReturnToLauncher(); return; }
        if (_profiler?.Running == true && (long)what is NotificationWMGoBackRequest or NotificationApplicationPaused or NotificationApplicationFocusOut)
        { _profiler.Cancel(); return; }
        if (_reconnecting && what == NotificationWMGoBackRequest) { ReturnToLauncher(); return; }
        if (what == NotificationWMGoBackRequest)
        {
            if (ControlsOpen) _controlsSettings!.HandleBack();
            else if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.HandleBack();
            else if (_defensePanel?.ShopOpen == true) _defensePanel.HandleBack();
            else if (InspectingEquipment) ClosePlayerEquipment();
            else if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.HandleBack();
            else if (GodotObject.IsInstanceValid(_accountProfilePanel)) CloseAccountProfile();
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
