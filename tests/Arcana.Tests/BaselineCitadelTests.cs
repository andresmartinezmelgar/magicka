using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineCitadelTests
{
    public static void RunGeometry(Action<string, Action> Test)
    {
        Test("Ciudadela: suelo exactamente cuatro veces mayor, forma irregular y puntos accesibles", () =>
        {
            var classic = ArenaMap.Get(ArenaMapId.Classic); var map = ArenaMap.Get(ArenaMapId.Citadel);
            Check(classic.Area == 1408 && map.Area == classic.Area * 4 && map.HalfWidth != map.HalfHeight,
                "El segundo mapa no tiene la superficie o la forma solicitadas.");
            Check(map.Floors.Count == 5 && !map.Inside(new(-22, 24)) && map.Walkable(new(-22, 10)), "Los pasillos no delimitan el suelo.");
            foreach (var point in map.ControlPoints) Check(map.Walkable(point), "Un punto está bloqueado.");
            for (int team = 0; team < 2; team++)
            for (int i = 0; i < 8; i++) Check(map.Walkable(map.Spawn(team, i)), "Una aparición está fuera del suelo.");
            var path = map.FindPath(map.ControlPoints[0], map.ControlPoints[2]);
            Check(path.Length > 1 && path[^1] == map.ControlPoints[2], "No existe ruta entre los extremos.");
            var at = map.ControlPoints[0];
            foreach (var next in path) { Check(map.ClearPath(at, next), "La ruta cruza una pared o un pilar."); at = next; }
            Check(map.FindPath(map.ControlPoints[0], new(-22, 24)).Length == 0, "Se trazó una ruta por un patio sin suelo.");
            var world = new GameWorld(new() { Map = ArenaMapId.Citadel }); var player = world.AddPlayer("Pasillos")!;
            Check(world.ApplyMessage(player.Id, new() { Type = "input", Sequence = 1, MoveTo = true, TargetX = 50, TargetY = 28 }) &&
                player.Input.TargetX == 50 && player.Input.TargetY == 28, "Se limitaron los destinos al mapa pequeño.");
            var snapshot = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
            Check(snapshot.Map == map.Id && snapshot.Points.Select(p => p.Position).SequenceEqual(map.ControlPoints), "La red perdió el mapa o sus puntos.");
            SnapshotValidation.Check(snapshot);
            foreach (var invalid in new[] { snapshot with { Map = (ArenaMapId)99 }, snapshot with { HoldingTeam = 2 },
                snapshot with { HoldSeconds = float.NaN }, snapshot with { HoldSeconds = 16 } })
            {
                bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se aceptaron metadatos de mapa o control inválidos.");
            }
        });
    }

    public static void RunCaptureAndMovement(Action<string, Action> Test)
    {
        Test("Ciudadela: ambos equipos ganan exactamente tras 900 ticks de control, y el reinicio borra el contador", () =>
        {
            foreach (int team in new[] { 0, 1 })
            {
                var (w, _) = CaptureCitadel(team);
                Check(Math.Abs(w.HoldSeconds - GameWorld.StepSeconds) < 0.00001f, "El contador no empezó al capturar el último punto.");
                for (int i = 0; i < 898; i++) w.Step();
                Check(!w.MatchEnded && w.HoldSeconds < 15 && w.Scores.All(s => s == 5), "Se ganó antes de 15 segundos o se consumieron tickets.");
                w.Step(); var state = w.Snapshot();
                Check(w.MatchEnded && w.Winner == team && state.HoldSeconds == 15 && state.HoldingTeam == team, "No se ganó a los 15 segundos exactos.");
                SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(state))!);
                for (int i = 0; i < 550; i++) w.Step();
                Check(!w.MatchEnded && w.HoldingTeam == -1 && w.HoldSeconds == 0 && w.Snapshot().Points.All(p => p.Owner == -1) &&
                    w.Players.Values.All(p => w.Map.Walkable(p.Position)), "El reinicio no limpió el control o reapareció en el mapa equivocado.");
            }
        });
        Test("Ciudadela: disputar y neutralizar reinicia el contador y nunca acumula intervalos separados", () =>
        {
            var (w, capturers) = CaptureCitadel(0);
            for (int i = 0; i < 898; i++) w.Step();
            var enemy = w.Players.Values.First(p => p.Team == 1); enemy.Position = capturers[0].Position + new V2(1, 0); w.Step();
            Check(!w.MatchEnded && w.HoldingTeam == -1 && w.HoldSeconds == 0 && w.Snapshot().Points[0].Contested,
                "La disputa no detuvo la victoria en el último tick.");
            enemy.Position = w.Map.Spawn(1, 0); w.Step();
            for (int i = 0; i < 448; i++) w.Step();
            Check(!w.MatchEnded && w.HoldSeconds < 8, "Se conservaron segundos anteriores a la disputa.");
            capturers[0].Position = w.Map.Spawn(0, 0); enemy.Position = w.Map.ControlPoints[0]; w.Step();
            Check(w.HoldSeconds == 0, "Un ataque sin defensor no interrumpió el control.");
            for (int i = 0; i < 110; i++) w.Step();
            Check(w.Snapshot().Points[0].Owner == -1 && w.HoldingTeam == -1, "Neutralizar el punto mantuvo el contador.");
            enemy.Position = w.Map.Spawn(1, 0); capturers[0].Position = w.Map.ControlPoints[0];
            for (int i = 0; i < 300 && w.HoldingTeam < 0; i++) w.Step();
            Check(!w.MatchEnded && w.HoldSeconds == GameWorld.StepSeconds, "Recapturar no empezó un intervalo nuevo.");
        });
        Test("Ciudadela: control parcial y bajas no sustituyen los 15 segundos de control total", () =>
        {
            var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Tickets = 1 }); var a = w.AddPlayer("Azul", 0)!;
            var b = w.AddPlayer("Rojo", 1)!; a.Position = w.Map.ControlPoints[1];
            for (int i = 0; i < 1500; i++) w.Step();
            Check(!w.MatchEnded && w.HoldSeconds == 0 && w.Scores.All(s => s == 1), "Un solo punto ganó por tickets.");
            b.Position = a.Position + new V2(1, 0); b.Health = 1; b.Invulnerable = 0;
            Select(w, a, Element.Arcane); Send(w, a, "nova"); w.Step();
            Check(!b.Alive && !w.MatchEnded && w.Scores.All(s => s == 1), "Una baja decidió la victoria por tickets.");
            Check(MatchRules.Objective(GameMode.Warfare, 15, 75, ArenaMapId.Citadel).Contains("15 segundos"), "La ayuda muestra otra condición de victoria.");
        });
        Test("Ciudadela: paredes bloquean caminar, rayos, proyectiles y traslación", () =>
        {
            var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
            var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; a.Position = new(-35, -22); b.Position = new(-1, -22);
            a.Invulnerable = b.Invulnerable = 0;
            Check(!w.Walkable(new(-32, -22)) && !w.Walkable(new(-33.2f, -22)), "Se permitió entrar en el patio o atravesar la pared.");
            Send(w, a, "input", aim: b.Position); w.Step(); Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast");
            for (int i = 0; i < 30; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Check(b.Health == 1000 && w.Snapshot().Beams.All(beam => beam.To.X <= -33), "El rayo atravesó un límite del mapa.");
            Send(w, a, "release"); Select(w, a, Element.Earth); Send(w, a, "cast");
            for (int i = 0; i < 10; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Send(w, a, "release");
            for (int i = 0; i < 25; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
            Check(w.Snapshot().Projectiles.Length == 0 && b.Health == 1000, "La roca cruzó el patio.");
            Send(w, a, "magick", slot: 1);
            Check(a.Position.X < -33.48f && w.Map.Walkable(a.Position), "La traslación cruzó una pared del pasillo.");
            a.Position = new(-33.5f, -22); a.ActionCooldown = 0;
            Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast"); w.Step();
            Check(w.Snapshot().Beams.All(beam => beam.From.X < -33 && beam.To.X <= -33), "El origen del hechizo saltó la pared al estar pegado a ella.");
        });
        Test("Ciudadela: el desplazamiento al cursor sigue los pasillos y los bots no salen del suelo", () =>
        {
            var w = new GameWorld(new() { Map = ArenaMapId.Citadel }); var player = w.AddPlayer("Ruta")!;
            player.Position = w.Map.ControlPoints[0]; var goal = w.Map.ControlPoints[2];
            for (int i = 0; i < 1700 && V2.Distance(player.Position, goal) > 0.2f; i++)
            {
                w.ApplyMessage(player.Id, new() { Type = "input", Sequence = player.LastSequence + 1, MoveTo = true,
                    TargetX = goal.X, TargetY = goal.Y, AimX = goal.X, AimY = goal.Y }); w.Step();
                Check(w.Map.Walkable(player.Position), "El jugador cortó por una zona sin suelo.");
            }
            Check(V2.Distance(player.Position, goal) < 0.2f, "El jugador no llegó al punto al otro lado de los pasillos.");
            var bots = new GameWorld(new() { Map = ArenaMapId.Citadel, Bots = 8 });
            for (int i = 0; i < 3600; i++) bots.Step();
            SnapshotValidation.Check(bots.Snapshot());
            Check(bots.Players.Values.All(p => bots.Map.Walkable(p.Position)) && bots.Players.Values.Any(p => Math.Abs(p.Position.X) < 30),
                "Los bots quedaron atascados o atravesaron los bordes.");
        });
        Test("Traslación: la siguiente ruta se calcula desde la llegada", () =>
        {
            var world = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
            var player = world.AddPlayer("Ruta previa")!;
            player.Position = world.Map.ControlPoints[0];
            var goal = world.Map.ControlPoints[2];
            void Click(GameWorld w, Wizard p) => w.ApplyMessage(p.Id, new() { Type = "input", Sequence = p.LastSequence + 1,
                MoveTo = true, TargetX = goal.X, TargetY = goal.Y, AimX = goal.X, AimY = goal.Y });
            Check(!world.Map.ClearPath(player.Position, goal), "La prueba no necesita una ruta por los pasillos.");
            Click(world, player); world.Step();
            player.Facing = new(0, 1);
            Send(world, player, "input", aim: player.Position + player.Facing * 14);
            var before = player.Position;
            Send(world, player, "magick", slot: 1);
            var arrival = player.Position;
            Check(V2.Distance(before, arrival) > 6 && !player.Input.MoveTo, "Traslación no detuvo el recorrido anterior.");
            world.Step(); Check(player.Position == arrival, "Se conservó el destino anterior al salto.");
            var fresh = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
            var reference = fresh.AddPlayer("Ruta desde la llegada")!; reference.Position = arrival;
            Click(world, player); Click(fresh, reference);
            for (int i = 0; i < 12; i++)
            {
                world.Step(); fresh.Step();
                Check(V2.Distance(player.Position, reference.Position) < 0.0001f,
                    "El siguiente clic reutilizó una ruta calculada antes del salto.");
                Check(world.Map.Walkable(player.Position), "La nueva ruta dejó el suelo.");
            }
            Check(V2.Distance(player.Position, arrival) > 0.5f, "La nueva ruta no avanzó desde la llegada.");
        });
        Test("Traslación: usa el último destino aunque no haya pasado un tick y respeta los pilares", () =>
        {
            foreach (var map in Enum.GetValues<ArenaMapId>())
            {
                var world = new GameWorld(new() { Map = map, MaxPlayers = 4, Mode = map == ArenaMapId.Bastion ? GameMode.Defense : ArenaMap.Get(map).SequentialCapture ? GameMode.Warfare : GameMode.Arena });
                var player = world.AddPlayer("Giro inmediato")!;
                var origin = map == ArenaMapId.Frontline ? new V2(-150, -12) : V2.Zero;
                player.Position = origin; player.Facing = new(1, 0);
                Send(world, player, "input", aim: origin + new V2(0, 7));
                Send(world, player, "magick", slot: 1);
                Check(Math.Abs(player.Position.X - origin.X) < 0.0001f && player.Position.Y > origin.Y + 6.5f,
                    "Traslación reutilizó la orientación del tick anterior en " + map);
                Check(player.Facing == new V2(0, 1) && !player.Input.MoveTo && world.Map.Walkable(player.Position),
                    "El salto no dejó orientación y posición coherentes.");
                var arrival = player.Position;
                world.Step(); Check(player.Position == arrival, "El mago continuó hacia el destino tras llegar.");
            }
            var (blocked, wizard, _) = Setup();
            wizard.Position = new(-10, -5); wizard.Facing = new(0, 1);
            Send(blocked, wizard, "input", aim: new(0, -5));
            Send(blocked, wizard, "magick", slot: 1);
            Check(wizard.Position.X is > -10 and < -7.5f && Math.Abs(wizard.Position.Y + 5) < 0.0001f && blocked.Walkable(wizard.Position),
                "Actualizar la dirección permitió saltar al otro lado de un pilar.");
        });
    }
}
