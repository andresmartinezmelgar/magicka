using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ClientVerification
{
    private bool CheckRespawnInput()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Reaparición: " + detail); }
        var saved = (_runtime.Practice, _runtime.Snapshot, _runtime.LocalId, _runtime.LocalSequence, _runtime.MoveTo, _runtime.IgnoreLeftUntilRelease,
            _runtime.LocalWasAlive, _runtime.LocalDeaths, _runtime.Aim, _runtime.Target);
        try
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            foreach (bool missedDeath in new[] { false, true })
            {
                var world = new GameWorld(new()
                {
                    Mode = mode, SpawnSeed = 19,
                    Map = mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic,
                    MaxPlayers = mode == GameMode.Defense ? DefenseRules.MaxDefenders : 16
                });
                var player = world.AddPlayer("Reaparición", 0)!;
                player.Position = new(-2, 0);
                _runtime.Practice = world; _runtime.LocalId = player.Id; _runtime.LocalSequence = 0; _runtime.Snapshot = world.Snapshot();
                _runtime.LocalWasAlive = true; _runtime.LocalDeaths = 0;
                _runtime.Aim = _runtime.Target = new(12, 0); _runtime.MoveTo = true; _runtime.IgnoreLeftUntilRelease = false;
                if (Ui.IsMobile)
                {
                    var start = _runtime.Touch!.GlobalPosition + new Vector2(142, _runtime.Touch.Size.Y - 125);
                    _runtime.Touch.Touch(0, start, true); _runtime.Touch.Drag(0, start + new Vector2(0, 80));
                }
                _runtime.Submit(_runtime.InputMessage()); world.Step();
                // Estado autoritativo de muerte, seguido de la reaparición real de GameWorld.
                player.Health = 0; player.Deaths++; player.Respawn = 0.1f;
                if (!missedDeath)
                {
                    _runtime.Snapshot = world.Snapshot();
                    _runtime.UpdateMouseMovement(true, false);
                    _runtime.UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                        Position = _runtime.Renderer.Camera.UnprojectPosition(ArenaRenderer.World(_runtime.Target)) });
                    var deadInput = _runtime.InputMessage();
                    Require(!deadInput.MoveTo && deadInput.MoveX == 0 && deadInput.MoveY == 0,
                        "una orden durante la muerte queda preparada para la siguiente vida.");
                }
                for (int i = 0; i < 7; i++) world.Step();
                Require(player.Alive, "el servidor no ha reaparecido al mago.");
                _runtime.Snapshot = world.Snapshot();
                var spawn = player.Position;
                for (int i = 0; i < 120; i++)
                {
                    _runtime.Aim = new(12, 0);
                    if (!Ui.IsMobile) _runtime.UpdateMouseMovement(true, false);
                    _runtime.Submit(_runtime.InputMessage()); world.Step(); _runtime.Snapshot = world.Snapshot();
                    Require(player.Position == spawn, $"{mode}: se restaura el destino o el joystick anterior.");
                }
                if (Ui.IsMobile)
                {
                    Require(_runtime.Touch!.Movement.IsZeroApprox(), "el joystick conserva el dedo de la vida anterior.");
                    var start = _runtime.Touch.GlobalPosition + new Vector2(142, _runtime.Touch.Size.Y - 125);
                    Require(_runtime.Touch.Touch(0, start, true),
                        "el joystick no admite un gesto nuevo.");
                    _runtime.Touch.Drag(0, start + new Vector2(0, 80));
                    Require(_runtime.InputMessage().MoveY > 0.9f, "el gesto nuevo no mueve desde la reaparición.");
                }
                else
                {
                    var goal = spawn + new V2(0, 3);
                    _runtime.UpdateMouseMovement(false, false);
                    _runtime.UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                        Position = _runtime.Renderer.Camera.UnprojectPosition(ArenaRenderer.World(goal)) });
                    Require(_runtime.InputMessage().MoveTo && V2.Distance(_runtime.Target, goal) < 0.01f,
                        "el clic nuevo no usa la posición de reaparición.");
                }
                _runtime.Submit(_runtime.InputMessage()); world.Step();
                Require(V2.Distance(player.Position, spawn) <= 6.2f * GameWorld.StepSeconds + 0.0001f,
                    "la orden nueva recupera la posición de la vida anterior.");
                _runtime.Touch?.Reset();
            }
            GD.Print("RESPAWN_INPUT_OK: todos los modos, clic o joystick anterior, órdenes durante la muerte, snapshot de muerte perdido y nueva orden desde la reaparición");
            return true;
        }
        finally
        {
            _runtime.Touch?.Reset();
            (_runtime.Practice, _runtime.Snapshot, _runtime.LocalId, _runtime.LocalSequence, _runtime.MoveTo, _runtime.IgnoreLeftUntilRelease,
                _runtime.LocalWasAlive, _runtime.LocalDeaths, _runtime.Aim, _runtime.Target) = saved;
        }
    }

    private bool CheckTranslationInput()
    {
        void Require(bool condition, string detail)
        { if (!condition) throw new InvalidDataException("Traslación: " + detail); }
        var saved = (_runtime.Practice, _runtime.Snapshot, _runtime.LocalId, _runtime.LocalSequence, _runtime.MoveTo, _runtime.IgnoreLeftUntilRelease,
            _runtime.LocalWasAlive, _runtime.LocalDeaths, _runtime.Aim, _runtime.Target);
        try
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena });
            var player = world.AddPlayer("Traslación")!;
            player.Position = new(-2, 0); player.Facing = new(1, 0);
            _runtime.Practice = world; _runtime.LocalId = player.Id; _runtime.LocalSequence = 0; _runtime.Snapshot = world.Snapshot();
            _runtime.LocalWasAlive = true; _runtime.LocalDeaths = 0;
            _runtime.Aim = new(16, 0);
            if (Ui.IsMobile)
            {
                _runtime.UpdateTouchAim(); player.Facing = (_runtime.Aim - player.Position).Normalized;
            }
            _runtime.Target = _runtime.Aim; _runtime.MoveTo = true; _runtime.IgnoreLeftUntilRelease = false;
            _runtime.Submit(_runtime.InputMessage()); world.Step(); _runtime.Snapshot = world.Snapshot();
            var before = player.Position;
            if (Ui.IsMobile)
            {
                var touch = _runtime.Touch!;
                var button = touch.GlobalPosition + new Vector2(touch.Size.X / 2 - 60, touch.Size.Y - 54);
                Require(touch.Touch(2, button, true), "el botón 2 no recibe el toque.");
                touch.Touch(2, button, false);
            }
            else _runtime.UnhandledInput(new InputEventKey { PhysicalKeycode = Key.Key2, Pressed = true });
            var arrival = player.Position;
            Require(V2.Distance(before, arrival) > 0.4f && V2.Distance(before, arrival) <= 7,
                "el botón 2 no ejecuta un salto válido.");
            Require(!_runtime.InputMessage().MoveTo, "el cliente reenvía el clic anterior.");
            for (int frame = 0; frame < 120; frame++)
            {
                _runtime.Aim = new(-16, 3);
                if (!Ui.IsMobile) _runtime.UpdateMouseMovement(true, false);
                _runtime.Submit(_runtime.InputMessage()); world.Step();
                Require(player.Position == arrival, "el ratón arrastra al mago después del salto.");
            }
            // Una orden nueva tiene que partir de la llegada, sin recuperar la posición anterior.
            var goal = arrival + new V2(0, 3);
            if (Ui.IsMobile)
            {
                var touch = _runtime.Touch!;
                var start = touch.GlobalPosition + new Vector2(142, touch.Size.Y - 125);
                Require(touch.Touch(0, start, true),
                    "el joystick no recibe la nueva orden.");
                touch.Drag(0, start + new Vector2(0, 80));
            }
            else
            {
                _runtime.UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
                    Position = _runtime.Renderer.Camera.UnprojectPosition(ArenaRenderer.World(goal)) });
                Require(_runtime.InputMessage().MoveTo && V2.Distance(_runtime.Target, goal) < 0.01f, "el nuevo clic no fija un destino nuevo.");
            }
            for (int frame = 0; frame < 15; frame++)
            {
                var previous = player.Position;
                _runtime.Submit(_runtime.InputMessage()); world.Step();
                Require(V2.Distance(previous, player.Position) <= 6.2f * GameWorld.StepSeconds + 0.0001f,
                    "el movimiento nuevo recupera la posición previa al salto.");
            }
            Require(V2.Distance(player.Position, arrival) > 0.5f && V2.Distance(player.Position, goal) < 3,
                "la nueva orden no avanza desde la llegada.");
            _runtime.Touch?.Reset();
            _runtime.MoveTo = true; _runtime.Target = goal;
            _runtime.UseMagick(0);
            Require(_runtime.InputMessage().MoveTo && player.Haste > 0, "Celeridad cancela el destino del clic.");
            GD.Print($"TRANSLATION_OK: {(Ui.IsMobile ? "botón táctil y joystick" : "tecla 2, clic mantenido y nuevo clic")}, parada tras el salto y movimiento desde la llegada");
            return true;
        }
        finally
        {
            _runtime.Touch?.Reset();
            (_runtime.Practice, _runtime.Snapshot, _runtime.LocalId, _runtime.LocalSequence, _runtime.MoveTo, _runtime.IgnoreLeftUntilRelease,
                _runtime.LocalWasAlive, _runtime.LocalDeaths, _runtime.Aim, _runtime.Target) = saved;
        }
    }
}
