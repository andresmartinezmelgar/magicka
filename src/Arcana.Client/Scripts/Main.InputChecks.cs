using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool CheckTranslationInput()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Traslación: " + detail); }
        var saved = (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease, _aim, _target);
        try
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Traslación")!;
            player.Position = new(-2, 0); player.Facing = new(1, 0);
            _practice = world; _localId = player.Id; _localSequence = 0; _snapshot = world.Snapshot();
            _aim = new(16, 0);
            if (Ui.IsMobile)
            {
                UpdateTouchAim(); player.Facing = (_aim - player.Position).Normalized;
            }
            _target = _aim; _moveTo = true; _ignoreLeftUntilRelease = false;
            Submit(InputMessage()); world.Step(); _snapshot = world.Snapshot();
            var before = player.Position;
            if (Ui.IsMobile)
            {
                var touch = _touch!;
                var button = touch.GlobalPosition + new Vector2(touch.Size.X / 2 - 60, touch.Size.Y - 54);
                Require(touch.Touch(2, button, true), "el botón 2 no recibe el toque.");
                touch.Touch(2, button, false);
            }
            else _UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Key2, Pressed = true });
            var arrival = player.Position;
            Require(V2.Distance(before, arrival) > 0.4f && V2.Distance(before, arrival) <= 7,
                "el botón 2 no ejecuta un salto válido.");
            Require(!InputMessage().MoveTo, "el cliente reenvía el clic anterior.");
            for (int frame = 0; frame < 120; frame++)
            {
                _aim = new(-16, 3);
                if (!Ui.IsMobile) UpdateMouseMovement(true, false);
                Submit(InputMessage()); world.Step();
                Require(player.Position == arrival, "el ratón arrastra al mago después del salto.");
            }
            // Una orden nueva tiene que partir de la llegada, sin recuperar la posición anterior.
            var goal = arrival + new V2(0, 3);
            if (Ui.IsMobile)
            {
                var touch = _touch!;
                Require(touch.Touch(0, touch.GlobalPosition + new Vector2(142, touch.Size.Y - 45), true),
                    "el joystick no recibe la nueva orden.");
            }
            else
            {
                _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                    Position = _renderer.Camera.UnprojectPosition(ArenaRenderer.World(goal)) });
                Require(InputMessage().MoveTo && V2.Distance(_target, goal) < 0.01f, "el nuevo clic no fija un destino nuevo.");
            }
            for (int frame = 0; frame < 15; frame++)
            {
                var previous = player.Position;
                Submit(InputMessage()); world.Step();
                Require(V2.Distance(previous, player.Position) <= 6.2f * GameWorld.StepSeconds + 0.0001f,
                    "el movimiento nuevo recupera la posición previa al salto.");
            }
            Require(V2.Distance(player.Position, arrival) > 0.5f && V2.Distance(player.Position, goal) < 3,
                "la nueva orden no avanza desde la llegada.");
            _touch?.Reset();
            _moveTo = true; _target = goal;
            UseMagick(0);
            Require(InputMessage().MoveTo && player.Haste > 0, "Celeridad cancela el destino del clic.");
            GD.Print($"TRANSLATION_OK: {(Ui.IsMobile ? "botón táctil y joystick" : "tecla 2, clic mantenido y nuevo clic")}, parada tras el salto y movimiento desde la llegada");
            return true;
        }
        finally
        {
            _touch?.Reset();
            (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease, _aim, _target) = saved;
        }
    }
}
