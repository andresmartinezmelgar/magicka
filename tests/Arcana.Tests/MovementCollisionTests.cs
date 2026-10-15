using Arcana.Core;
using System.Reflection;

internal static class MovementCollisionTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static string At(V2 point) => $"({point.X:0.000}, {point.Y:0.000})";

    private static (GameWorld World, Wizard Player) Create(ArenaMapId map = ArenaMapId.Classic)
    {
        var world = new GameWorld(new WorldOptions { Map = map,
            Mode = map == ArenaMapId.Bastion ? GameMode.Defense : map == ArenaMapId.Frontline ? GameMode.Warfare : GameMode.Arena, MaxPlayers = 4,
            Seed = 42, PickupSeed = 42, EnvironmentSeed = 42 });
        var player = world.AddPlayer("Colisiones")!;
        return (world, player);
    }

    private static void Step(GameWorld world, Wizard player, V2 direction, bool checkSegment = true)
    {
        var from = player.Position;
        float maximum = 6.2f * RelicBuffs.Speed(player) / 60;
        world.ApplyMessage(player.Id, new ClientMessage { Type = "input", Sequence = player.LastSequence + 1,
            MoveX = direction.X, MoveY = direction.Y, AimX = from.X - 10, AimY = from.Y });
        world.Step();
        Check(world.Walkable(player.Position), $"El movimiento entró en un obstáculo: {At(player.Position)}.");
        Check(V2.Distance(from, player.Position) <= maximum + 0.0001f,
            $"La asistencia aumentó la velocidad en {world.Map.Id}: {At(from)} → {At(player.Position)}.");
        if (checkSegment)
        {
            Check(world.Map.ClearPath(from, player.Position), "El movimiento cortó un pilar o una esquina.");
            Check(world.Snapshot().Walls.All(w => Geometry.SegmentCapsule(from, player.Position,
                w.A, w.B, ArenaMap.WizardRadius + 0.18f) == null), "El movimiento atravesó un muro.");
        }
    }

    private static void Wall(GameWorld world, Wizard player, V2 a, V2 b)
    {
        int count = world.Snapshot().Walls.Length;
        typeof(GameWorld).GetMethod("AddWall", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(world, [player, new SpellRecipe([Element.Shield, Element.Earth]), a, b]);
        Check(world.Snapshot().Walls.Length == count + 1, "No se creó el muro del escenario.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Colisiones: mantener una dirección rodea un pilar incluso de frente", () =>
        {
            foreach (var direction in new V2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
                new V2(1, 1).Normalized, new V2(-1, 1).Normalized })
            {
                var (world, player) = Create(); var center = new V2(-6, -5);
                player.Position = center - direction * 4;
                for (int tick = 0; tick < 155; tick++) Step(world, player, direction);
                Check(V2.Dot(player.Position - center, direction) > 3,
                    $"El mago sigue bloqueado delante del pilar con dirección {At(direction)}: {At(player.Position)}.");
            }
        });
        test("Colisiones: barrido y rodeo de todos los pilares de los cinco mapas", () =>
        {
            foreach (var map in Enum.GetValues<ArenaMapId>())
            foreach (var obstacle in ArenaMap.Get(map).Obstacles)
            foreach (var direction in new V2[] { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) })
            {
                var (world, player) = Create(map);
                player.TeleportCooldown = 600;
                player.Position = obstacle.Position - direction * 4;
                Check(world.Walkable(player.Position), "El escenario del pilar no comienza en suelo transitable.");
                for (int tick = 0; tick < 160; tick++) Step(world, player, direction);
                Check(V2.Dot(player.Position - obstacle.Position, direction) > 2,
                    $"El rodeo quedó bloqueado en {map}, pilar {At(obstacle.Position)}.");
            }
        });
        test("Colisiones: el deslizamiento respeta el lado de llegada al pilar", () =>
        {
            foreach (float offset in new[] { -0.3f, 0.3f })
            {
                var (world, player) = Create(); player.Position = new(-10, -5 + offset);
                for (int tick = 0; tick < 75; tick++) Step(world, player, new(1, 0));
                Check((player.Position.Y + 5) * offset > 0.2f, "El rodeo cambió de lado durante el contacto.");
                Check(player.Position.X > -6, "El mago no salió de la mitad delantera del pilar.");
            }
        });
        test("Colisiones: una pared plana se rodea por un extremo sin soltar la dirección", () =>
        {
            var (world, player) = Create(); player.Position = new(-4, 0);
            Wall(world, player, new(0, -2.2f), new(0, 2.2f));
            for (int tick = 0; tick < 180; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > 4 && world.Snapshot().Walls.Length == 1,
                $"El mago no rodeó el muro mientras seguía activo: {At(player.Position)}.");
        });
        test("Colisiones: el contacto frontal con pared elige el extremo cercano", () =>
        {
            var (world, player) = Create(); player.Position = new(-4, 1.4f);
            Wall(world, player, new(0, -2.2f), new(0, 2.2f));
            for (int tick = 0; tick < 100; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > 1 && player.Position.Y > 2.2f,
                "El mago tomó el extremo lejano de una pared plana.");
        });
        test("Colisiones: el rodeo continúa por la unión de varios segmentos de muro", () =>
        {
            var (world, player) = Create(); player.Position = new(-4, -0.2f);
            Wall(world, player, new(0, -4.4f), V2.Zero);
            Wall(world, player, V2.Zero, new(0, 4.4f));
            for (int tick = 0; tick < 240; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > 2 && world.Snapshot().Walls.Length == 2,
                $"El rodeo se invirtió al cambiar de segmento: {At(player.Position)}.");
        });
        test("Colisiones: deslizamiento sobre una pared oblicua", () =>
        {
            var (world, player) = Create(); player.Position = new(-5, 0);
            Wall(world, player, new(-2, -2), new(2, 2));
            for (int tick = 0; tick < 180; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > 4 && world.Snapshot().Walls.Length == 1,
                "El mago quedó atascado en una pared oblicua.");
        });
        test("Colisiones: el rodeo descarta un extremo cerrado por el borde del mapa", () =>
        {
            var (world, player) = Create(); player.Position = new(0, 14.8f);
            Wall(world, player, new(4, 11.4f), new(4, 15.8f));
            for (int tick = 0; tick < 210; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > 5 && player.Position.Y < 11.4f,
                $"El mago escogió el extremo cerrado en vez del accesible: {At(player.Position)}.");
        });
        test("Colisiones: una pared del pasillo se sigue hasta la abertura", () =>
        {
            var (world, player) = Create(ArenaMapId.Citadel); player.Position = new(-36, -22);
            for (int tick = 0; tick < 240; tick++) Step(world, player, new(1, 0));
            Check(player.Position.X > -30 && player.Position.Y > -16,
                $"El mago no encontró el extremo accesible de la pared: {At(player.Position)}.");
        });
        test("Colisiones: diagonales, esquinas y bordes conservan suelo y velocidad", () =>
        {
            var (world, player) = Create(); player.Position = new(20, 1);
            for (int tick = 0; tick < 60; tick++) Step(world, player, new(1, 1));
            Check(player.Position.X < 22 - ArenaMap.WizardRadius && player.Position.Y > 4,
                "La diagonal se bloqueó en vez de deslizarse por la pared.");
            player.Position = new(20, 14);
            for (int tick = 0; tick < 180; tick++) Step(world, player, new(1, 1));
        });
        test("Colisiones: soltar o invertir el mando cancela el rodeo", () =>
        {
            var (world, player) = Create(); player.Position = new(-8, -5);
            for (int tick = 0; tick < 12; tick++) Step(world, player, new(1, 0));
            var stopped = player.Position;
            for (int tick = 0; tick < 20; tick++) Step(world, player, V2.Zero);
            Check(player.Position == stopped, "La asistencia continuó tras soltar el mando.");
            Step(world, player, new(-1, 0));
            Check(player.Position.X < stopped.X && Math.Abs(player.Position.Y - stopped.Y) < 0.0001f,
                "El lado de rodeo anterior ignoró el cambio de dirección.");
        });
        test("Colisiones: el joystick conserva su intensidad durante el rodeo", () =>
        {
            var (world, player) = Create(); player.Position = new(-8, -5);
            for (int tick = 0; tick < 100; tick++)
            {
                var from = player.Position; Step(world, player, new(0.25f, 0));
                Check(V2.Distance(from, player.Position) <= 6.2f * 0.25f / 60 + 0.0001f,
                    "El roce convirtió el joystick parcial en velocidad máxima.");
            }
            Check(player.Position.Y < -5.5f || player.Position.Y > -4.5f,
                "El joystick parcial quedó detenido de frente al pilar.");
        });
        test("Colisiones: congelación y empuje frontal no activan un rodeo automático", () =>
        {
            var (world, player) = Create(); player.Position = new(-8, -5); player.Frozen = 2;
            var from = player.Position;
            for (int tick = 0; tick < 30; tick++) Step(world, player, new(1, 0));
            Check(player.Position == from, "La asistencia movió a un mago congelado.");
            player.Frozen = 0; player.Position = new(-10, -5); player.Knockback = new(360, 0);
            world.ApplyMessage(player.Id, new ClientMessage { Type = "input", Sequence = player.LastSequence + 1 });
            world.Step();
            Check(world.Walkable(player.Position) && player.Position.X < -7.58f && player.Position.Y == -5,
                "El empuje atravesó o rodeó artificialmente el pilar.");
        });
        test("Colisiones: caminar al cursor también supera el pilar en la arena clásica", () =>
        {
            var (world, player) = Create(); player.Position = new(-10, -5); var goal = new V2(-2, -5);
            for (int tick = 0; tick < 240 && V2.Distance(player.Position, goal) > 0.18f; tick++)
            {
                var from = player.Position;
                world.ApplyMessage(player.Id, new ClientMessage { Type = "input", Sequence = player.LastSequence + 1,
                    MoveTo = true, TargetX = goal.X, TargetY = goal.Y });
                world.Step();
                Check(world.Walkable(player.Position) && world.Map.ClearPath(from, player.Position),
                    "El recorrido al cursor atravesó el pilar.");
                Check(V2.Distance(from, player.Position) <= 6.2f / 60 + 0.0001f, "La ruta aceleró al mago.");
            }
            Check(V2.Distance(player.Position, goal) <= 0.18f, "El mago no llegó al destino detrás del pilar.");
        });
        test("Colisiones: un muro que sella todo el pasillo sigue bloqueando el avance", () =>
        {
            GameWorld? blocked = null;
            for (int seed = 0; seed < 40; seed++)
            {
                var world = new GameWorld(new WorldOptions { Map = ArenaMapId.Frontline,
                    Mode = GameMode.Warfare, EnvironmentSeed = 42 });
                typeof(GameWorld).GetMethod("SpawnFrontlineWall", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(world, [new Random(seed)]);
                if (world.Snapshot().Walls.First().A.X != -180) continue;
                typeof(GameWorld).GetField("_environmentClock", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(world, 600f);
                blocked = world; break;
            }
            Check(blocked != null, "No se creó el cierre del pasillo.");
            var player = blocked!.AddPlayer("Muro cerrado", 0)!; player.Position = new(-184, -7);
            for (int tick = 0; tick < 300; tick++) Step(blocked, player, new(1, 0));
            Check(player.Position.X < -180 && blocked.Snapshot().Walls.Length > 0,
                "El deslizamiento permitió saltar un muro que cierra el pasillo.");
        });
    }
}
