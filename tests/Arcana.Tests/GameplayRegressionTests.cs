using Arcana.Core;
using System.Reflection;

internal static class GameplayRegressionTests
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void Send(GameWorld world, Wizard player, string type, Element element = Element.Water) =>
        world.ApplyMessage(player.Id, new ClientMessage { Type = type, Element = element, Sequence = player.LastSequence + 1 });
    private static void Input(GameWorld world, Wizard player, V2 aim) => world.ApplyMessage(player.Id,
        new ClientMessage { Type = "input", Sequence = player.LastSequence + 1, AimX = aim.X, AimY = aim.Y });
    private static void Select(GameWorld world, Wizard player, params Element[] elements)
    { foreach (var element in elements) Send(world, player, "element", element); }
    private static void Invoke(GameWorld world, string method, params object?[] arguments) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, arguments);
    private static (GameWorld World, Wizard Player, Wizard Enemy) Arena()
    {
        var world = new GameWorld(new WorldOptions { Mode = GameMode.Arena, FriendlyFire = false });
        var player = world.AddPlayer("Lanzador")!; var enemy = world.AddPlayer("Objetivo")!;
        player.Position = V2.Zero; player.Facing = new(-1, 0); enemy.Position = new(4, 0);
        player.Invulnerable = enemy.Invulnerable = 0;
        return (world, player, enemy);
    }
    private static GameWorld EntranceWall()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var world = new GameWorld(new WorldOptions { Map = ArenaMapId.Frontline, Mode = GameMode.Warfare,
                EnvironmentSeed = 42, FriendlyFire = false });
            Invoke(world, "SpawnFrontlineWall", new Random(seed));
            if (world.Snapshot().Walls.First().A.X != -180) continue;
            // Aísla el muro bajo prueba de nuevos eventos; su duración real se mantiene.
            typeof(GameWorld).GetField("_environmentClock", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 600f);
            return world;
        }
        throw new Exception("No se encontró una semilla de muro en el primer paso.");
    }

    public static void Run(Action<string, Action> test)
    {
        test("Apuntado: los fragmentos usan la entrada recibida justo antes del lanzamiento", () =>
        {
            var (world, player, _) = Arena();
            Input(world, player, new(8, 0)); Select(world, player, Element.Water, Element.Cold, Element.Cold);
            Send(world, player, "cast");
            Check(world.Snapshot().Projectiles.All(p => p.Direction.X > 0.95f) && world.Snapshot().Projectiles.Length > 0,
                "El lanzamiento instantáneo usó la orientación del tick anterior.");
        });
        test("Apuntado: soltar una roca usa el nuevo objetivo sin esperar otro tick", () =>
        {
            var (world, player, _) = Arena();
            Input(world, player, new(8, 0)); Select(world, player, Element.Earth, Element.Earth, Element.Earth);
            Send(world, player, "cast");
            Input(world, player, new(0, -8)); Send(world, player, "release");
            var rock = world.Snapshot().Projectiles.Single();
            Check(rock.Direction.Y < -0.999f && Math.Abs(rock.Direction.X) < 0.001f, "La roca conservó el apuntado anterior.");
        });
        test("Apuntado: una barrera inmediata aparece hacia el último objetivo", () =>
        {
            var (world, player, _) = Arena();
            Input(world, player, new(8, 0)); Select(world, player, Element.Shield, Element.Earth); Send(world, player, "cast");
            Check(world.Snapshot().Walls.Single().A.X > 3, "La barrera se creó a espaldas del objetivo.");
        });
        test("Apuntado: cuerpo a cuerpo aplica la entrada del mismo lote", () =>
        {
            var (world, player, enemy) = Arena(); enemy.Position = new(1, 0);
            Input(world, player, enemy.Position); Send(world, player, "melee");
            Check(enemy.Health == 910, "El golpe ignoró el giro recibido antes de ejecutarlo.");
        });
        test("Combate: el origen de rayos y chorros no atraviesa una pared cercana", () =>
        {
            foreach (var element in new[] { Element.Arcane, Element.Fire })
            {
                var (world, player, enemy) = Arena(); player.Position = new(-2, 0); enemy.Position = new(3, 0);
                Invoke(world, "AddWall", enemy, new SpellRecipe([Element.Shield, Element.Earth]), new V2(0, -2.2f), new V2(0, 2.2f));
                Check(world.Snapshot().Walls.Length == 1, "No se creó el muro del escenario.");
                player.Position = new(-0.68f, 0);
                Check(world.Walkable(player.Position), "El lanzador no está fuera del muro.");
                Input(world, player, enemy.Position); world.Step(); Select(world, player, element, element, element); Send(world, player, "cast");
                for (int tick = 0; tick < 12; tick++) { Input(world, player, enemy.Position); world.Step(); }
                Check(enemy.Health == 1000, $"El bastón emitió {element} detrás del muro: vida {enemy.Health}, paredes {world.Snapshot().Walls.Length}.");
                Check(world.Snapshot().Walls.Single().Health < 1300, "La pared no recibió el impacto bloqueado.");
                SnapshotValidation.Check(world.Snapshot());
            }
            var (edgeWorld, edgeCaster, _) = Arena();
            edgeCaster.Position = new(edgeWorld.Map.HalfWidth - 0.7f, 0);
            var origin = (V2)typeof(GameWorld).GetMethod("SpellOrigin", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(edgeWorld, [edgeCaster, new V2(1, 0), 0.7f, 0f])!;
            Check(edgeWorld.Map.Inside(origin), "El bastón terminó exactamente sobre la frontera.");
        });
        test("Navegación: la ruta parcial se acerca al muro sin atravesarlo", () =>
        {
            var world = EntranceWall(); var walls = world.Snapshot().Walls;
            var from = world.Map.Spawn(0, 0); var goal = world.Map.ControlPoints[0];
            bool Clear(V2 a, V2 b) => walls.All(w =>
                Geometry.SegmentCapsule(a, b, w.A, w.B, ArenaMap.WizardRadius + 0.18f) == null);
            Check(world.Map.FindPath(from, goal, Clear).Length == 0, "Una ruta completa atravesó el muro.");
            var route = world.Map.FindPath(from, goal, Clear, allowPartial: true);
            Check(route.Length > 0 && route[^1].X < -180 && V2.Distance(route[^1], goal) < V2.Distance(from, goal),
                "La ruta parcial no aproxima al lado accesible del muro.");
            foreach (var at in route)
            {
                Check(world.Map.ClearPath(from, at) && Clear(from, at), "La ruta parcial cruza suelo o paredes bloqueados.");
                from = at;
            }
        });
        test("Navegación: usa otro nodo cercano si un muro tapa el más próximo", () =>
        {
            var map = ArenaMap.Get(ArenaMapId.Citadel);
            var from = new V2(-44, -4); var goal = new V2(44, 26);
            var a = new V2(-44.75f, -6); var b = new V2(-44.75f, 0);
            bool Clear(V2 x, V2 y) => Geometry.SegmentCapsule(x, y, a, b, ArenaMap.WizardRadius + 0.18f) == null;
            Check(map.Walkable(from) && map.Walkable(goal) && !map.ClearPath(from, goal), "La prueba no requiere una ruta.");
            Check(!Clear(from, new(-45, -5)) && Clear(from, new(-43, -5)), "El muro no tapa únicamente el ancla más próxima.");
            var route = map.FindPath(from, goal, Clear);
            Check(route.Length > 0 && route[^1] == goal, "No se encontró el ancla alternativa.");
            foreach (var next in route)
            { Check(map.ClearPath(from, next) && Clear(from, next), "La ruta atravesó el muro o salió del suelo."); from = next; }
        });
        test("Bots: alcanzan y destruyen un muro que corta el recorrido antes de que expire", () =>
        {
            var world = EntranceWall(); int initialWalls = world.Snapshot().Walls.Length;
            var bot = world.AddPlayer("Demoledor", 0, bot: true)!;
            for (int tick = 0; tick < 1320; tick++)
            {
                bot.Invulnerable = 1.2f; world.Step();
                if (tick % 60 == 0) SnapshotValidation.Check(world.Snapshot());
            }
            Check(bot.Position.X > -180, "El bot esperó inmóvil al otro lado del mapa hasta que caducara el muro.");
            Check(world.Time < 30 && world.Snapshot().Walls.Length < initialWalls, "El escenario no distingue destrucción de caducidad.");
        });
        test("Bots: mantienen el objetivo del muro durante la carga de una roca", () =>
        {
            var world = EntranceWall(); var wall = world.Snapshot().Walls[0]; var center = (wall.A + wall.B) / 2;
            var bot = world.AddPlayer("Demoledor", 0, bot: true)!; bot.Position = center + new V2(-7, 0);
            Check(world.Walkable(bot.Position), "El bot no tiene una posición transitable.");
            world.Step();
            Check(world.Snapshot().Players.Single().Casting && world.Snapshot().Players.Single().CastingElements!.All(e => e == Element.Earth),
                "El bot no comenzó a atacar el muro.");
            var aim = new V2(bot.Input.AimX, bot.Input.AimY);
            for (int tick = 0; tick < 12; tick++) world.Step();
            Check(new V2(bot.Input.AimX, bot.Input.AimY) == aim && V2.Dot(bot.Facing, (aim - bot.Position).Normalized) > 0.999f,
                "La carga abandonó el muro para apuntar al punto de captura.");
        });
        test("Bots: no lanzan ataques contra enemigos ocultos por obstáculos", () =>
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                var world = new GameWorld(new WorldOptions { Mode = GameMode.Arena, Seed = seed });
                var bot = world.AddPlayer("Bot", bot: true)!; var enemy = world.AddPlayer("Oculto")!;
                bot.Position = new(-11, -5); enemy.Position = new(-1, -5); bot.Invulnerable = enemy.Invulnerable = 0;
                world.Step();
                var state = world.Snapshot();
                Check(!state.Players.Single(p => p.Id == bot.Id).Casting && state.Projectiles.Length == 0 && state.Fields.Length == 0,
                    "El bot inició una ofensiva sin línea de visión.");
            }
        });
        test("Frente: cada grupo de paredes respeta también el límite ambiental", () =>
        {
            var world = new GameWorld(new WorldOptions { Map = ArenaMapId.Frontline, Mode = GameMode.Warfare });
            for (int seed = 0; seed < 80; seed++)
            {
                Invoke(world, "SpawnFrontlineWall", new Random(seed));
                Check(world.Snapshot().Walls.Count(w => w.Owner == 0) <= 12, "Un grupo de segmentos desbordó el límite de doce muros ambientales.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
    }
}
