using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    public override void _PhysicsProcess(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        if (Loading || _reconnecting || _applicationSuspended) return;
        if (_verification?.FreezeSimulation == true) return;
        if (_profiler?.Running == true) return;
        if (!_playing)
        {
            _backdrop.Step();
            // Menú: mantener la simulación suave, pero evitar generar 60 snapshots/segundo
            // de un mundo que solo sirve como fondo decorativo.
            if (++_backdropSnapshotFrames >= (Ui.IsMobile ? 3 : 2) || _backdropSnapshot == null)
            {
                _backdropSnapshotFrames = 0;
                _backdropSnapshot = _backdrop.Snapshot();
            }
            return;
        }
        SynchronizeDefenseInterface();
        SynchronizeElementSelectionStatistics();
        _inputTime += delta;
        if (_practice != null)
        {
            if (!TutorialHoldsCombat && (_practice.IsDefense || !_paused && !_help.Visible))
            {
                Submit(InputMessage());
                if (_practiceSession != null) _practiceSession.Step(); else _practice.Step();
                _snapshot = _practice.Snapshot();
            }
        }
        else if (_inputTime >= 1.0 / 30) { Submit(InputMessage()); _inputTime = 0; }
        _verification?.AfterPhysics();
    }

    private void UpdateMouseMovement(bool leftPressed, bool shiftPressed)
    {
        bool alive = SynchronizeLocalLife();
        if (!leftPressed) _ignoreLeftUntilRelease = false;
        if (alive && !TutorialHoldsCombat && !_paused && !_help.Visible && !_ignoreLeftUntilRelease && !shiftPressed && leftPressed)
        { _target = _aim; _moveTo = true; }
    }

    private bool SynchronizeLocalLife()
    {
        var local = (_connection?.Latest ?? _snapshot)?.Players.FirstOrDefault(p => p.Id == _localId);
        if (local == null) return false;
        // Deaths permite detectar otra vida aunque se haya perdido el snapshot de la muerte.
        if (local.Alive != _localWasAlive || local.Deaths != _localDeaths)
        {
            _moveTo = false; _ignoreLeftUntilRelease = true; _target = local.Position;
            _touch?.Reset();
        }
        _localWasAlive = local.Alive; _localDeaths = local.Deaths;
        return local.Alive;
    }

    private void UseMagick(int slot)
    {
        if (!CheckMagickAvailable(slot, checkTarget: true)) return;
        if (slot == 1)
        {
            // El siguiente paquete no debe restaurar el destino anterior al salto.
            _moveTo = false; _ignoreLeftUntilRelease = true;
        }
        Submit(InputMessage()); Submit(new() { Type = "magick", Slot = slot });
    }

    private ClientMessage InputMessage()
    {
        bool alive = SynchronizeLocalLife() && !TutorialHoldsCombat;
        float x = 0, y = 0;
        if (alive && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && !ViewingDamageSummary && !DefenseInterfaceOpen)
        {
            if (_touch != null) { x = _touch.Movement.X; y = _touch.Movement.Y; }
            else
            {
                x = (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);
                y = (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
            }
        }
        if (x != 0 || y != 0) _moveTo = false;
        return new() { Type = "input", MoveX = x, MoveY = y, MoveTo = alive && _moveTo && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen && !ViewingDamageSummary && !DefenseInterfaceOpen,
            TargetX = _target.X, TargetY = _target.Y, AimX = _aim.X, AimY = _aim.Y };
    }

    private void Submit(ClientMessage message)
    {
        if (Loading || TutorialActive && message.Type == "defense_restart" ||
            TutorialHoldsCombat && !message.Type.StartsWith("defense_", StringComparison.Ordinal)) return;
        if (_practice != null)
        {
            message.Sequence = ++_localSequence;
            if (message.Type == "defense_restart")
            {
                CaptureStatistics(MatchRecordStatus.Left); CaptureLocalBestiary();
                if (_practice.RestartDefense()) { BeginLocalBestiary(); AutosaveSurvival("restart"); }
            }
            else if (_practice.ApplyMessage(_localId, message) && TutorialHoldsCombat)
                _snapshot = _practice.Snapshot();
        }
        else _connection?.Send(message);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        { NavigateBack(); GetViewport().SetInputAsHandled(); return; }
        if (Loading)
        {
            GetViewport().SetInputAsHandled(); return;
        }
        if (_profiler?.Running == true)
        {
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if ((ControlsOpen || ViewingDamageSummary) && key.PhysicalKeycode != Key.Escape) { GetViewport().SetInputAsHandled(); return; }
            if (key.PhysicalKeycode == Key.F1) { if (!InspectingEquipment) ToggleHelp(); GetViewport().SetInputAsHandled(); return; }
        }
        if (!_playing || TutorialHoldsCombat || _paused || _help.Visible || InspectingEquipment || ControlsOpen || ViewingDamageSummary || DefenseInterfaceOpen ||
            _waitingPanel?.Visible == true) return;
        if (input is InputEventKey { Pressed: true, Echo: false } press)
        {
            Element? element = press.PhysicalKeycode switch
            {
                Key.Q => Element.Water, Key.W => Element.Life, Key.E => Element.Shield, Key.R => Element.Cold,
                Key.A => Element.Lightning, Key.S => Element.Arcane, Key.D => Element.Earth, Key.F => Element.Fire, _ => null
            };
            if (element != null) Submit(new() { Type = "element", Element = element.Value });
            if (press.PhysicalKeycode == Key.Backspace) Submit(new() { Type = "clear" });
            int slot = press.PhysicalKeycode switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, _ => -1 };
            if (slot >= 0) UseMagick(slot);
        }
        if (input is InputEventMouseButton mouse)
        {
            if (Ui.IsMobile) return;
            _aim = _renderer.Cursor(mouse.Position); Submit(InputMessage());
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            {
                if (Input.IsPhysicalKeyPressed(Key.Shift)) Submit(new() { Type = "melee" });
                else if (SynchronizeLocalLife())
                { _ignoreLeftUntilRelease = false; _target = _aim; _moveTo = true; Submit(InputMessage()); }
            }
            if (mouse.ButtonIndex == MouseButton.Right)
                Submit(new() { Type = mouse.Pressed ? Input.IsPhysicalKeyPressed(Key.Shift) ? "nova" : "cast" : "release" });
            if (mouse.ButtonIndex == MouseButton.Middle && mouse.Pressed) Submit(new() { Type = "self" });
            if (mouse.ButtonIndex == MouseButton.WheelUp) _renderer.Zoom(-1.5f);
            if (mouse.ButtonIndex == MouseButton.WheelDown) _renderer.Zoom(1.5f);
        }
    }

    private void StopInput() { _moveTo = false; _aimMarker.Hide(); _touch?.Reset(); Submit(new() { Type = "release" }); Submit(InputMessage()); }
}
