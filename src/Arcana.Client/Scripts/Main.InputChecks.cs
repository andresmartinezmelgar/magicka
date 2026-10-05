using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool CheckRespawnInput()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Reaparición: " + detail); }
        var saved = (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease,
            _localWasAlive, _localDeaths, _aim, _target);
        try
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            foreach (bool missedDeath in new[] { false, true })
            {
                var world = new GameWorld(new() { Mode = mode, SpawnSeed = 19 });
                var player = world.AddPlayer("Reaparición", 0)!;
                player.Position = new(-2, 0);
                _practice = world; _localId = player.Id; _localSequence = 0; _snapshot = world.Snapshot();
                _localWasAlive = true; _localDeaths = 0;
                _aim = _target = new(12, 0); _moveTo = true; _ignoreLeftUntilRelease = false;
                if (Ui.IsMobile) _touch!.Touch(0, _touch.GlobalPosition + new Vector2(142, _touch.Size.Y - 45), true);
                Submit(InputMessage()); world.Step();
                // Estado autoritativo de muerte, seguido de la reaparición real de GameWorld.
                player.Health = 0; player.Deaths++; player.Respawn = 0.1f;
                if (!missedDeath)
                {
                    _snapshot = world.Snapshot();
                    UpdateMouseMovement(true, false);
                    _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                        Position = _renderer.Camera.UnprojectPosition(ArenaRenderer.World(_target)) });
                    var deadInput = InputMessage();
                    Require(!deadInput.MoveTo && deadInput.MoveX == 0 && deadInput.MoveY == 0,
                        "una orden durante la muerte queda preparada para la siguiente vida.");
                }
                for (int i = 0; i < 7; i++) world.Step();
                Require(player.Alive, "el servidor no ha reaparecido al mago.");
                _snapshot = world.Snapshot();
                var spawn = player.Position;
                for (int i = 0; i < 120; i++)
                {
                    _aim = new(12, 0);
                    if (!Ui.IsMobile) UpdateMouseMovement(true, false);
                    Submit(InputMessage()); world.Step(); _snapshot = world.Snapshot();
                    Require(player.Position == spawn, $"{mode}: se restaura el destino o el joystick anterior.");
                }
                if (Ui.IsMobile)
                {
                    Require(_touch!.Movement.IsZeroApprox(), "el joystick conserva el dedo de la vida anterior.");
                    Require(_touch.Touch(0, _touch.GlobalPosition + new Vector2(142, _touch.Size.Y - 45), true),
                        "el joystick no admite un gesto nuevo.");
                    Require(InputMessage().MoveX > 0.9f, "el gesto nuevo no mueve desde la reaparición.");
                }
                else
                {
                    var goal = spawn + new V2(0, 3);
                    UpdateMouseMovement(false, false);
                    _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                        Position = _renderer.Camera.UnprojectPosition(ArenaRenderer.World(goal)) });
                    Require(InputMessage().MoveTo && V2.Distance(_target, goal) < 0.01f,
                        "el clic nuevo no usa la posición de reaparición.");
                }
                Submit(InputMessage()); world.Step();
                Require(V2.Distance(player.Position, spawn) <= 6.2f * GameWorld.StepSeconds + 0.0001f,
                    "la orden nueva recupera la posición de la vida anterior.");
                _touch?.Reset();
            }
            GD.Print("RESPAWN_INPUT_OK: tres modos, clic o joystick anterior, órdenes durante la muerte, snapshot de muerte perdido y nueva orden desde la reaparición");
            return true;
        }
        finally
        {
            _touch?.Reset();
            (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease,
                _localWasAlive, _localDeaths, _aim, _target) = saved;
        }
    }

    private bool CheckTranslationInput()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Traslación: " + detail); }
        var saved = (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease,
            _localWasAlive, _localDeaths, _aim, _target);
        try
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Traslación")!;
            player.Position = new(-2, 0); player.Facing = new(1, 0);
            _practice = world; _localId = player.Id; _localSequence = 0; _snapshot = world.Snapshot();
            _localWasAlive = true; _localDeaths = 0;
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
            (_practice, _snapshot, _localId, _localSequence, _moveTo, _ignoreLeftUntilRelease,
                _localWasAlive, _localDeaths, _aim, _target) = saved;
        }
    }
}
