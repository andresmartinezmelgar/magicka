using Arcana.Core;
using System.Reflection;

internal static class FrontlineTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static GameWorld World(int seed = 71) => new(new WorldOptions {
        Map = ArenaMapId.Frontline, Mode = GameMode.Warfare, EnvironmentSeed = seed, FriendlyFire = false, Tickets = 5, ScoreLimit = 1 });
    private static void Advance(GameWorld world, int ticks, bool protect = true)
    {
        for (int i = 0; i < ticks; i++)
        {
            if (protect) foreach (var player in world.Players.Values.Where(p => p.Alive)) player.Invulnerable = 1.2f;
            world.Step();
        }
    }
    private static void Capture(GameWorld world, Wizard player, int point)
    {
        player.Position = world.Map.ControlPoints[point];
        Advance(world, 510);
        Check(world.Snapshot().Points[point].Owner == player.Team, "No se capturó el punto del frente.");
    }
    private static void Invoke(GameWorld world, string method, params object?[] arguments) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, arguments);
    private static void Kill(GameWorld world, Wizard victim, Wizard killer)
    { victim.Health = 1; victim.Invulnerable = 0; Invoke(world, "Damage", victim, 1000f, killer.Id, null); }

    public static async Task RunNetwork(string address, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Frente en red: sala, cinco puntos, equipos, apuntado largo y modos rechazados", async () =>
        {
            foreach (var mode in new[] { GameMode.Duel, GameMode.Arena })
            {
                bool rejected = false;
                try { await RoomBrowser.CreateAsync(address, new CreateRoomRequest {
                    Name = "Frente inválido", Map = ArenaMapId.Frontline, Mode = mode, MaxPlayers = 2, ServerPassword = password }); }
                catch (InvalidOperationException e) { rejected = e.Message.Contains("Frente de conquista"); }
                Check(rejected, "La API aceptó un frente con victoria por bajas.");
            }
            var room = await RoomBrowser.CreateAsync(address, new CreateRoomRequest { Name = "Frente en red",
                Map = ArenaMapId.Frontline, Mode = GameMode.Warfare, MaxPlayers = 4, ServerPassword = password, Tickets = 5, ScoreLimit = 1 });
            var directory = await RoomBrowser.ListAsync(address);
            Check(directory.Rooms.Any(r => r.Id == room.Id && r.Map == ArenaMapId.Frontline), "El directorio perdió el mapa.");
            await using var blue = new ArenaConnection(); await using var red = new ArenaConnection();
            await blue.ConnectAsync(address, "Azul", 0, password, roomId: room.Id);
            await red.ConnectAsync(address, "Rojo", 1, password, roomId: room.Id);
            for (int i = 0; i < 150 && blue.Latest!.Players.Length < 2; i++) await Task.Delay(20);
            var state = blue.Latest!;
            Check(state.Map == ArenaMapId.Frontline && state.Points.Length == 5 && state.Scores.All(s => s == 0) && !state.MatchEnded &&
                state.Points.Select(p => p.Position).SequenceEqual(ArenaMap.Get(ArenaMapId.Frontline).ControlPoints), "El snapshot del frente es incorrecto.");
            var player = red.Latest!.Players.Single(p => p.Id == red.PlayerId);
            red.Send(new ClientMessage { Type = "input", AimX = player.Position.X, AimY = player.Position.Y + 6 });
            bool facing = false;
            for (int i = 0; i < 150; i++)
            {
                player = red.Latest!.Players.Single(p => p.Id == red.PlayerId);
                if (Math.Abs(player.Facing.X) < 0.001f && player.Facing.Y > 0.999f) { facing = true; break; }
                await Task.Delay(20);
            }
            Check(facing, "La red recortó el apuntado en el extremo del mapa.");
            SnapshotValidation.Check(blue.Latest!); SnapshotValidation.Check(red.Latest!);
        });
    }

    public static void Run(Action<string, Action> test)
    {
        test("Frente: pasillo grande, cinco puntos conectados, portales y apariciones seguras", () =>
        {
            var map = ArenaMap.Get(ArenaMapId.Frontline);
            Check(map.HalfWidth * 2 == 480 && map.Area > ArenaMap.Get(ArenaMapId.Citadel).Area * 1.8f &&
                map.Floors.Count > 1 && map.Boundary.Count > 4 && map.ControlPoints.Count == 5, "Geometría o tamaño incorrectos.");
            Check(!map.Inside(new(-150, 18)) && map.ControlPoints.All(map.Walkable), "El pasillo se convirtió en un rectángulo o bloqueó puntos.");
            for (int team = 0; team < 2; team++) for (int count = 0; count <= 5; count++) for (int slot = 0; slot < 4; slot++)
                Check(map.Walkable(map.Spawn(team, slot, count)), "Respawn adelantado sin suelo.");
            V2 from = map.Spawn(0, 0);
            foreach (var goal in map.ControlPoints.Concat([map.Spawn(1, 0)]))
            {
                var path = map.FindPath(from, goal); Check(path.Length > 0, "Un quiebro desconecta el recorrido.");
                foreach (var next in path) { Check(map.ClearPath(from, next), "La ruta cruza una esquina."); from = next; }
            }
            Check(map.Teleports.Count == 3 && map.Teleports.All(p => map.Walkable(p.A) && map.Walkable(p.B)), "Portales bloqueados.");
        });
        test("Frente: infiltrarse hasta el último punto no permite saltar capturas", () =>
        {
            var world = World(); var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
            blue.Position = world.Map.ControlPoints[4]; red.Position = world.Map.ControlPoints[0];
            Advance(world, 300);
            Check(world.Snapshot().Points.All(p => p.Owner == -1 && p.Progress == 0), "Una infiltración capturó un punto posterior.");
            blue.Position = world.Map.ControlPoints[0]; red.Position = world.Map.ControlPoints[4];
            Advance(world, 260);
            Check(world.Snapshot().Points[0].Owner == 0 && world.Snapshot().Points[4].Owner == 1 &&
                world.Snapshot().Points.Skip(1).Take(3).All(p => p.Owner == -1), "No se habilitaron solo los primeros puntos libres.");
            blue.Position = world.Map.ControlPoints[3]; red.Position = world.Map.ControlPoints[1]; Advance(world, 260);
            Check(world.Snapshot().Points[3].Progress == 0 && world.Snapshot().Points[1].Progress == 0, "Se saltó un punto libre intermedio.");
            Check(world.Scores.All(s => s == 0) && !world.MatchEnded, "El mapa usó tickets para terminar.");
        });
        test("Frente: entradas y respawn avanzan al conquistar y retroceden al perder terreno", () =>
        {
            var world = World(); var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
            Capture(world, blue, 0); Capture(world, blue, 1);
            var newcomer = world.AddPlayer("Refuerzo", 0)!;
            Check(newcomer.Position.X == world.Map.Spawn(0, 0, 2).X, "El refuerzo entró en la base inicial.");
            Kill(world, blue, red); blue.Respawn = GameWorld.StepSeconds / 2; world.Step();
            Check(blue.Alive && blue.Position.X == world.Map.Spawn(0, 0, 2).X && world.Walkable(blue.Position), "El respawn no avanzó.");
            Capture(world, red, 4); Capture(world, red, 3); Capture(world, red, 2);
            red.Position = world.Map.ControlPoints[1]; Advance(world, 110);
            Check(world.Snapshot().Points[1].Owner == -1, "No se neutralizó el primer punto enemigo.");
            Kill(world, blue, red); blue.Respawn = GameWorld.StepSeconds / 2; world.Step();
            Check(blue.Position.X == world.Map.Spawn(0, 0, 1).X, "La aparición no retrocedió al perder terreno.");
        });
        test("Frente: disputa detiene progreso y el enemigo solo puede neutralizar el punto inmediato", () =>
        {
            var world = World(); var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
            Capture(world, blue, 0); Capture(world, blue, 1); Capture(world, red, 4); Capture(world, red, 3);
            blue.Position = red.Position = world.Map.ControlPoints[2]; Advance(world, 50);
            var point = world.Snapshot().Points[2]; Check(point.Contested && point.Progress == 0, "La disputa avanzó la captura.");
            blue.Position = world.Map.Spawn(0, 0); Capture(world, red, 2);
            red.Position = world.Map.ControlPoints[0]; Advance(world, 260);
            Check(world.Snapshot().Points[0].Owner == 0 && world.Snapshot().Points[0].Progress == -1, "Se atacó un punto enemigo posterior.");
            red.Position = world.Map.ControlPoints[1]; Advance(world, 110);
            Check(world.Snapshot().Points[1].Owner == -1, "El punto enemigo inmediato no se neutraliza.");
        });
        test("Frente: victoria solo con los cinco puntos, sin bajas ni tickets, y reinicio limpio", () =>
        {
            foreach (int team in new[] { 0, 1 })
            {
                var world = World(); var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
                var winner = team == 0 ? blue : red; var loser = team == 0 ? red : blue;
                for (int i = 0; i < 7; i++) { Kill(world, loser, winner); loser.Respawn = GameWorld.StepSeconds / 2; world.Step(); }
                Check(winner.Kills == 7 && world.Scores.All(s => s == 0) && !world.MatchEnded, "Las bajas terminaron la partida.");
                for (int i = 0; i < 5; i++)
                {
                    winner.Position = world.Map.ControlPoints[team == 0 ? i : 4 - i];
                    Advance(world, 260);
                    Check(world.Snapshot().Points.Count(p => p.Owner == team) == i + 1, "Orden de captura incorrecto.");
                    Check(world.MatchEnded == (i == 4), "Victoria prematura o ausente.");
                }
                Check(world.Winner == team, "Ganador incorrecto.");
                Advance(world, 550);
                Check(!world.MatchEnded && world.Snapshot().Points.All(p => p.Owner == -1 && p.Progress == 0) &&
                    world.Scores.All(s => s == 0) && world.Snapshot().Walls.Length == 0 && world.Snapshot().Fields.Length == 0 &&
                    world.Players.Values.All(p => Math.Abs(p.Position.X) == 230), "El reinicio conserva terreno o eventos.");
            }
        });
        test("Frente: portales bidireccionales, enfriamiento y captura posterior bloqueada", () =>
        {
            var world = World(); var player = world.AddPlayer("Explorador")!; var pair = world.Map.Teleports[1];
            player.Position = pair.A; world.Step();
            Check(player.Position == pair.B && player.TeleportCooldown > 1.9f, "No se efectuó el traslado.");
            Advance(world, 180); Check(player.Position == pair.B, "El portal provocó un rebote sin salir del círculo de llegada.");
            player.Position = world.Map.ControlPoints[3]; Advance(world, 260);
            Check(world.Snapshot().Points[3].Progress == 0, "El portal permitió saltarse el frente.");
            player.Position = pair.B; world.Step(); Check(player.Position == pair.A, "No se puede volver por el portal.");
            SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!);
        });
        test("Frente: meteoros avisados dañan ambos equipos incluso sin fuego amigo", () =>
        {
            var world = World(); var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
            Invoke(world, "SpawnFrontlineMeteor", new Random(17));
            var meteor = world.Snapshot().Fields.Single();
            Check(meteor.Kind == "meteor" && meteor.Owner == 0 && !meteor.Armed && meteor.Lifetime > 1, "No hay aviso previo del meteoro.");
            blue.Position = meteor.Position + new V2(-0.1f, 0); red.Position = meteor.Position + new V2(0.1f, 0);
            blue.Invulnerable = red.Invulnerable = 0;
            Advance(world, 30, false); Check(blue.Health == 1000 && red.Health == 1000, "El meteoro impactó durante el aviso.");
            Advance(world, 60, false);
            Check(blue.Health < 1000 && red.Health < 1000 && world.Snapshot().Fields.Length == 0, "El peligro no impactó ambos equipos o no se limpió.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Frente: muros ambientales bloquean, reciben daño y respetan los límites de red", () =>
        {
            var world = World(); var player = world.AddPlayer("Demoledor")!;
            Invoke(world, "SpawnFrontlineWall", new Random(9));
            var wall = world.Snapshot().Walls.First(); var center = (wall.A + wall.B) / 2;
            Check(wall.Owner == 0 && wall.Lifetime == 30 && !world.Walkable(center), "El muro no bloquea el paso.");
            SnapshotValidation.Check(world.Snapshot());
            player.Position = center + new V2(-2, 0); player.Facing = new(1, 0);
            long sequence = 0;
            void Send(string type, Element element = Element.Water) => world.ApplyMessage(player.Id, new ClientMessage {
                Type = type, Sequence = ++sequence, Element = element, AimX = center.X, AimY = center.Y });
            for (int attempt = 0; attempt < 8 && world.Snapshot().Walls.Any(w => w.Id == wall.Id); attempt++)
            {
                Send("input"); Send("element", Element.Life); Send("element", Element.Life); Send("element", Element.Life); Send("cast");
                for (int tick = 0; tick < 100; tick++) { Send("input"); world.Step(); }
            }
            Check(!world.Snapshot().Walls.Any(w => w.Id == wall.Id), "Los hechizos no destruyen el muro.");
            Check(world.Walkable(center), "La colisión permanece después de destruir el muro.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Frente: eventos aleatorios reproducibles, acotados y exclusivos del mapa", () =>
        {
            var a = World(121); var b = World(121); a.AddPlayer("A"); b.AddPlayer("A");
            bool meteor = false, wall = false;
            for (int i = 0; i < 7200; i++)
            {
                a.Step(); b.Step();
                var state = a.Snapshot();
                meteor |= state.Fields.Length > 0; wall |= state.Walls.Length > 0;
                Check(Protocol.Serialize(state) == Protocol.Serialize(b.Snapshot()), "La semilla no reproduce los eventos.");
                if (i % 60 == 0) SnapshotValidation.Check(state);
            }
            Check(meteor && wall, "No se producen ambos eventos durante la partida.");
            var old = new GameWorld(new WorldOptions { Map = ArenaMapId.Classic }); old.AddPlayer("A"); Advance(old, 2000);
            Check(old.Snapshot().Fields.Length == 0 && old.Snapshot().Walls.Length == 0, "Los eventos se filtraron a otro mapa.");
        });
        test("Frente: se rechazan modos, puntos y muros incompatibles sin ampliar los mapas anteriores", () =>
        {
            foreach (var mode in new[] { GameMode.Duel, GameMode.Arena })
            {
                bool rejected = false;
                try { _ = new GameWorld(new WorldOptions { Map = ArenaMapId.Frontline, Mode = mode }); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Se aceptó un modo con puntuación en el frente.");
            }
            var world = World(); world.AddPlayer("A"); var state = world.Snapshot();
            foreach (var invalid in new[] { state with { Points = state.Points.Take(3).ToArray() },
                state with { Points = state.Points.Select(p => p.Id == 4 ? p with { Id = 5 } : p).ToArray() },
                state with { Points = state.Points.Select(p => p.Id == 0 ? p with { Position = V2.Zero } : p).ToArray() },
                state with { Walls = [new WallSnapshot(1000, 1, new(-20, -10), new(-20, -6), 1300, 30, [Element.Earth])] } })
            {
                bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Se aceptó un estado inválido del frente.");
            }
            SnapshotValidation.Check(state);
        });
    }
}
