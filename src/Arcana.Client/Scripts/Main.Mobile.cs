using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private MobileControls? _touch;
    private bool _applicationSuspended, _applicationPaused, _applicationFocused = true;
    private long _applicationSuspendedAt;

    private void SetApplicationSuspended(bool suspended)
    {
        if (_applicationSuspended == suspended) return;
        _applicationSuspended = suspended;
        if (suspended)
        {
            _applicationSuspendedAt = System.Environment.TickCount64;
            if (_playing)
            {
                StopInput();
                if (!_paused && !_help.Visible) TogglePause();
                SynchronizeDefenseInterface();
                AutosaveSurvival("background"); CaptureLocalBestiary();
            }
            if (_profiler?.Running == true) _profiler.Cancel();
        }
        else
        {
            // El tiempo en otra app no cuenta como un fallo al preparar recursos.
            if (Loading) _loadingStartedAt += Math.Max(0, System.Environment.TickCount64 - _applicationSuspendedAt);
            _snapshotAge = 0; _lastHealthSnapshot = null; _inputTime = 0;
            _ignoreLeftUntilRelease = true; _moveTo = false; _touch?.Reset();
        }
    }

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
                or "--regression-smoke" or "--navigation-smoke" or "--shop-smoke" or "--sale-smoke" or "--usability-smoke"
                or "--equipment-smoke" or "--preview-equipment-picker" or "--preview-equipment-totals"
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
            if (!_playing || _paused || _help.Visible || InspectingEquipment || ControlsOpen || ViewingDamageSummary || DefenseInterfaceOpen || _waitingPanel?.Visible == true) return;
            UpdateTouchAim();
            if (message.Type == "magick") UseMagick(message.Slot);
            else { Submit(InputMessage()); Submit(message); }
        };
        _touch.AimChanged += () => { if (_playing && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && !ViewingDamageSummary && !DefenseInterfaceOpen && _waitingPanel?.Visible != true) { UpdateTouchAim(); Submit(InputMessage()); } };
        _touch.HelpRequested += ToggleHelp;
        _touch.MenuRequested += TogglePause;
        _touch.ScoreRequested += () => { if (_hud != null) { StopInput(); _hud.ScoreOpen = !_hud.ScoreOpen; SynchronizeDefenseInterface(); } };
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
        bool visible = _playing && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && !ViewingDamageSummary && _waitingPanel?.Visible != true;
        if (_touch.Visible && !visible) _touch.Reset();
        _touch.Visible = visible;
    }
    public override void _Notification(int what)
    {
        if (!Ui.IsMobile || _launcher == null) return;
        if (what == NotificationWMGoBackRequest)
        {
            if (!NavigateBack()) GetTree().Quit();
            return;
        }
        HandleApplicationLifecycle(what);
    }

    private void HandleApplicationLifecycle(long what)
    {
        switch (what)
        {
            case NotificationApplicationPaused: _applicationPaused = true; break;
            case NotificationApplicationResumed: _applicationPaused = false; break;
            case NotificationApplicationFocusOut: _applicationFocused = false; break;
            case NotificationApplicationFocusIn: _applicationFocused = true; break;
            default: return;
        }
        SetApplicationSuspended(_applicationPaused || !_applicationFocused);
    }
}
