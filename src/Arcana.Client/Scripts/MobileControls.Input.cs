using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (EditorMode)
        {
            bool edited = input switch
            {
                InputEventScreenTouch touch => EditControl(touch.Index, touch.Position, touch.Pressed && !touch.Canceled),
                InputEventScreenDrag drag => DragControl(drag.Index, drag.Position),
                InputEventMouseButton { ButtonIndex: MouseButton.Left, Device: not -1 } mouse => EditControl(-2, mouse.Position, mouse.Pressed),
                InputEventMouseMotion { Device: not -1 } mouse => DragControl(-2, mouse.Position),
                _ => false
            };
            if (edited) GetViewport().SetInputAsHandled();
            return;
        }
        bool used = input switch
        {
            InputEventScreenTouch touch => Touch(touch.Index, touch.Position, touch.Pressed, touch.Canceled),
            InputEventScreenDrag drag => Drag(drag.Index, drag.Position),
            _ => false
        };
        if (used) GetViewport().SetInputAsHandled();
    }

    internal bool Touch(int finger, Vector2 point, bool down, bool canceled = false)
    {
        if (!IsVisibleInTree()) return false;
        point -= GlobalPosition;
        if (!down || canceled)
        {
            if (finger == _moveFinger) { _moveFinger = -1; _move = Vector2.Zero; _moveCenter = _moveHome; QueueRedraw(); return true; }
            if (finger == _aimFinger)
            {
                _aimFinger = -1; _aimKnob = Vector2.Zero; _aimCenter = _aimHome;
                _aimedProjectile = null;
                bool castOnRelease = _directionalAimPending && !canceled;
                _directionalAimPending = false; QueueRedraw();
                if (_pendingMagick >= 0)
                {
                    if (!canceled)
                    {
                        AimChanged?.Invoke();
                        ActionRequested?.Invoke(new() { Type = "magick", Slot = _pendingMagick });
                    }
                    CancelMagickAim();
                    Request(new() { Type = "release" }); return true;
                }
                // Escudos y fragmentos de hielo son instantáneos: el lanzamiento
                // espera la dirección FINAL del gesto, nunca el primer contacto.
                // Un toque cancelado no debe gastar los elementos.
                if (castOnRelease)
                {
                    AimChanged?.Invoke();
                    Request(new() { Type = "cast" });
                }
                Request(new() { Type = "release" }); return true;
            }
            if (_pressed.Remove(finger, out var previous)) { previous.Panel.Modulate = Colors.White; return true; }
            return false;
        }
        if (finger == _moveFinger || finger == _aimFinger || _pressed.ContainsKey(finger)) return true;
        if (!new Rect2(Vector2.Zero, Size).HasPoint(point)) return false;
        if (PointerBlocked?.Invoke(point + GlobalPosition) == true) return false;
        var action = _actions.FirstOrDefault(a => a.Panel.Visible && ButtonRect(a).HasPoint(point));
        if (action != null)
        {
            _pressed[finger] = action; action.Panel.Modulate = new Color(1.2f, 1.2f, 1.2f);
            if (action.Message != null)
            {
                // El primer elemento manual vuelve a empezar aunque el acceso ocupe las tres cargas.
                if (action.Message.Type == "element" && _preparedShortcut != null) Request(new() { Type = "clear" });
                Request(new() { Type = action.Message.Type, Element = action.Message.Element, Slot = action.Message.Slot });
            }
            action.Run?.Invoke(); return true;
        }
        if (_hudAreas.Any(area => !area.Floating && !Settings.Hidden(area.Name) && area.Bounds.HasPoint(point + GlobalPosition)) ||
            !Settings.Hidden(MapPosition) && MapRectangle?.HasPoint(point + GlobalPosition) == true) return false;
        // Las posiciones editadas tienen prioridad sobre la zona flotante de movimiento.
        // Un toque adicional en un joystick ocupado nunca se reasigna al otro control.
        float aimDistance = point.DistanceTo(_aimFinger >= 0 ? _aimCenter : _aimHome);
        float moveDistance = point.DistanceTo(_moveFinger >= 0 ? _moveCenter : _moveHome);
        if (_aimFinger >= 0) aimDistance = Math.Min(aimDistance, point.DistanceTo(_aimHome));
        if (_moveFinger >= 0) moveDistance = Math.Min(moveDistance, point.DistanceTo(_moveHome));
        bool nearAim = aimDistance <= AimRadius + 20, nearMove = moveDistance <= MoveRadius + 20;
        bool movementZone = new Rect2(new(Settings.LeftHanded ? Size.X / 2 : 0, 0), new(Size.X / 2, Size.Y)).HasPoint(point);
        if (nearAim && (!nearMove || aimDistance <= moveDistance) || !nearMove && !movementZone)
        {
            if (Settings.Hidden("Aim")) return false;
            if (_aimFinger >= 0) return true;
            _aimCenter = FreeStickCenter(point, "Aim");
            _aimFinger = finger;
            var spell = new SpellRecipe(_selected.Elements);
            _aimedSpellShape = spell.Shape;
            _aimedProjectile = spell.Shape is SpellShape.Rock or SpellShape.Shards ? spell.Elements : null;
            _rockChargeObserved = false;
            _directionalAimPending = _pendingMagick < 0 && (_aimedSpellShape is SpellShape.Barrier or SpellShape.Shards);
            Aim(point); AimChanged?.Invoke();
            if (!_directionalAimPending && _pendingMagick < 0) Request(new() { Type = "cast" });
            QueueRedraw();
            return true;
        }
        if (nearMove || movementZone)
        {
            if (Settings.Hidden("Move")) return false;
            if (_moveFinger >= 0) return true;
            _moveCenter = FreeStickCenter(point, "Move"); _moveFinger = finger;
            Move(point); return true;
        }
        return false;
    }

    internal bool Drag(int finger, Vector2 point)
    {
        if (!IsVisibleInTree()) return false;
        point -= GlobalPosition;
        if (finger == _moveFinger) { Move(point); return true; }
        if (finger == _aimFinger) { Aim(point); AimChanged?.Invoke(); return true; }
        return _pressed.ContainsKey(finger);
    }

    private void Request(ClientMessage message)
    {
        if (message.Type == "magick")
        {
            if (_pendingMagick == message.Slot)
            {
                bool automaticMeteor = message.Slot == 2 && _meteorTapAt is { } previous && Time.GetTicksMsec() - previous <= 350;
                CancelMagickAim();
                if (automaticMeteor) AutoMeteorRequested?.Invoke();
                return;
            }
            if (MagickAvailable?.Invoke(message.Slot) == false) return;
            if (message.Slot is 1 or 2)
            {
                ActionRequested?.Invoke(new() { Type = "release" });
                _directionalAimPending = false; _pendingMagick = message.Slot; _magickAimAmount = 1;
                _meteorTapAt = message.Slot == 2 ? Time.GetTicksMsec() : null;
                _aimedProjectile = null;
                if (_aimFinger >= 0) _magickAimAmount = Math.Min(1, _aimKnob.Length() * Settings.Sensitivity);
                AimChanged?.Invoke(); QueueRedraw(); return;
            }
            CancelMagickAim();
        }
        bool selectionAction = message.Type is "element" or "clear" or "cast" or "self" or "nova";
        if (message.Type is "element" or "clear") _preparedShortcut = null;
        if (selectionAction) _selected.Predict(message);
        ActionRequested?.Invoke(message);
        // Submit asigna la secuencia de la conexión o de la partida local.
        if (selectionAction) _selected.Track(message);
    }

    private Vector2 Stick(Vector2 offset, float radius)
    {
        if (offset.Length() < Settings.DeadZone * radius) return Vector2.Zero;
        return offset.LimitLength(radius) / radius;
    }

    private void Move(Vector2 point) { _move = Stick(point - _moveCenter, MoveRadius); QueueRedraw(); }

    private void Aim(Vector2 point)
    {
        _aimKnob = Stick(point - _aimCenter, AimRadius);
        if (_pendingMagick >= 0) _magickAimAmount = Math.Min(1, _aimKnob.Length() * Settings.Sensitivity);
        if (!_aimKnob.IsZeroApprox()) _aim = _aimKnob.Normalized();
        QueueRedraw();
    }

    private void CancelMagickAim()
    { _pendingMagick = -1; _meteorTapAt = null; _magickAimAmount = 1; QueueRedraw(); }
}
