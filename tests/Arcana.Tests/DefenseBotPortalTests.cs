using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class DefenseBotPortalTests
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    private static (GameWorld World, Wizard Human, Wizard[] Bots) World(int bots, SurvivalMapSpec? map = null)
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = map ?? SurvivalMapSpec.Legacy, MaxPlayers = 4, Bots = bots, Seed = 71 });
        var human = world.AddPlayer("Explorador")!;
        Set(world, "_spawnClock", 10000f); Set(world, "_bossClock", 10000f);
        return (world, human, world.Players.Values.Where(p => p.Bot).OrderBy(p => p.Id).ToArray());
    }
    private static V2 Nearby(GameWorld world, V2 portal, float radius) => Enumerable.Range(0, 16)
        .Select(i => portal + new V2(MathF.Cos(i * MathF.Tau / 16), MathF.Sin(i * MathF.Tau / 16)) * radius)
        .First(at => world.Walkable(at) && world.Map.ClearPath(portal, at));
    private static int[] Follow(GameWorld world, Wizard[] bots, int ticks)
    {
        var crossings = new int[bots.Length];
        for (int tick = 0; tick < ticks; tick++)
        {
            var before = bots.Select(b => b.Position).ToArray();
            world.Step();
            for (int i = 0; i < bots.Length; i++)
            {
                Check(world.Walkable(bots[i].Position), "Un bot atravesó una pared o salió del suelo.");
                if (V2.Distance(before[i], bots[i].Position) > 20) crossings[i]++;
            }
        }
        return crossings;
    }
    private static IList Walls(GameWorld world) => (IList)Field(world, "_walls").GetValue(world)!;
    private static void Block(GameWorld world, V2 point)
    {
        var wall = Activator.CreateInstance(typeof(GameWorld).Assembly.GetType("Arcana.Core.Wall")!, true)!;
        Set(wall, "Id", 9000 + Walls(world).Count); Set(wall, "A", point + new V2(-2, 0));
        Set(wall, "B", point + new V2(2, 0)); Set(wall, "Health", 100000f); Set(wall, "Lifetime", 10000f);
        Set(wall, "Recipe", new SpellRecipe([Element.Shield, Element.Earth]));
        Walls(world).Add(wall);
    }

    public static void Run(Action<string, Action> test)
    {
        test("Bots: los tres aliados siguen al humano por el portal en ambos sentidos y sin rebotes", () =>
        {
            foreach (bool reverse in new[] { false, true })
            {
                var (world, human, bots) = World(3); var pair = world.Map.Teleports[0];
                var entry = reverse ? pair.B : pair.A; var exit = reverse ? pair.A : pair.B;
                for (int i = 0; i < bots.Length; i++) bots[i].Position = entry + new V2(5 + i * 2, 0);
                bots[^1].TeleportCooldown = 1.5f;
                human.Position = entry; world.Step();
                Check(V2.Distance(human.Position, exit) < .01f, "El humano no cruzó el portal.");
                human.Position = Nearby(world, exit, 6);
                var crossings = Follow(world, bots, 600);
                Check(crossings.All(c => c == 1), "Algún aliado no eligió el portal o regresó por él.");
                Check(bots.All(b => V2.Distance(b.Position, human.Position) < 18), "Los aliados no se reagruparon.");
            }
        });
        test("Bots: seguir por portales también funciona en el mapa procedural actual", () =>
        {
            var (world, human, bots) = World(1, new SurvivalMapSpec(71)); var pair = world.Map.Teleports[0];
            bots[0].Position = Nearby(world, pair.A, 5);
            human.Position = pair.A; world.Step();
            Check(V2.Distance(human.Position, pair.B) < .01f, "El humano no cruzó el portal procedural.");
            human.Position = Nearby(world, pair.B, 6);
            var crossings = Follow(world, bots, 480);
            Check(crossings[0] == 1 && V2.Distance(bots[0].Position, human.Position) < 18,
                "El aliado ignoró el atajo del mapa procedural.");
        });
        test("Bots: una salida bloqueada se descarta y el portal se reconsidera al quedar libre", () =>
        {
            var (world, human, bots) = World(1); var pair = world.Map.Teleports[0];
            bots[0].Position = pair.A + new V2(6, 0); human.Position = pair.B + new V2(0, -6);
            foreach (var portal in world.Map.Teleports) Block(world, portal.B);
            Check(Follow(world, bots, 60)[0] == 0, "Un bot atravesó una salida bloqueada.");
            Walls(world).Clear();
            Check(Follow(world, bots, 600)[0] == 1 && V2.Distance(bots[0].Position, human.Position) < 18,
                "El bot conservó la ruta a pie después de liberarse el portal.");
        });
        test("Bots: mantienen el recorrido a pie cuando es más corto que usar portales", () =>
        {
            var (world, human, bots) = World(1); var entry = world.Map.Teleports[0].A;
            bots[0].Position = entry + new V2(6, 0); human.Position = entry + new V2(0, 6);
            Check(Follow(world, bots, 180)[0] == 0 && V2.Distance(bots[0].Position, human.Position) < 10,
                "El bot tomó un portal innecesario o dejó de acompañar al humano cercano.");
        });
    }
}
