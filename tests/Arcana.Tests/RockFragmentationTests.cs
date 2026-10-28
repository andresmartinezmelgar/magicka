using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class RockFragmentationTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float actual, float expected, string message) =>
        Check(Math.Abs(actual - expected) < .005f, $"{message}: {actual} != {expected}");
    private static object? Call(GameWorld world, string method, params object[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static IList List(GameWorld world, string name) =>
        (IList)typeof(GameWorld).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
    private static T Get<T>(object value, string name) => (T)value.GetType().GetField(name)!.GetValue(value)!;
    private static void Set(object value, string name, object data) => value.GetType().GetField(name)!.SetValue(value, data);
    private static void Shots(GameWorld world, int ticks = 1)
    { for (int i = 0; i < ticks; i++) Call(world, "TickProjectiles", GameWorld.StepSeconds); }
    private static (GameWorld World, Wizard Caster, Wizard Enemy) Arena(int rank)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare, MaxPlayers = 4, FriendlyFire = false });
        var caster = world.AddPlayer("Lanzador", 0)!; var enemy = world.AddPlayer("Objetivo", 1)!;
        caster.Position = new(-3, 0); enemy.Position = new(-1, 0);
        caster.Invulnerable = enemy.Invulnerable = 0;
        world.EquipPlayer(caster.Id, EquipmentModifiers.None with { RockFragmentation = rank });
        return (world, caster, enemy);
    }
    private static object Launch(GameWorld world, Wizard caster, params Element[] elements)
    {
        Check((bool)Call(world, "SpawnProjectile", caster, new SpellRecipe(elements), new V2(1, 0), 2f, 20f, .37f)!,
            "No se pudo lanzar la roca de prueba.");
        var projectiles = List(world, "_projectiles");
        return projectiles[projectiles.Count - 1]!;
    }
    private static void Impact(GameWorld world, int id)
    {
        for (int tick = 0; tick < 30 && world.Snapshot().Projectiles.Any(p => p.Id == id); tick++) Shots(world);
        Check(world.Snapshot().Projectiles.All(p => p.Id != id), "La roca no alcanzó el impacto preparado.");
    }
    private static void AddWall(GameWorld world, int owner, params Element[] elements)
    {
        var type = typeof(GameWorld).Assembly.GetType("Arcana.Core.Wall")!;
        var wall = Activator.CreateInstance(type, nonPublic: true)!;
        Set(wall, "Id", 9000); Set(wall, "Owner", owner); Set(wall, "A", new V2(-.3f, -3));
        Set(wall, "B", new V2(-.3f, 3)); Set(wall, "Recipe", new SpellRecipe(elements));
        Set(wall, "Health", 1300f);
        List(world, "_walls").Add(wall);
    }

    internal static void Run(Action<string, Action> test)
    {
        test("Fragmentación: cuatro o seis fragmentos, daño reducido, tirada capturada y una sola división", () =>
        {
            foreach (int rank in new[] { 0, 1, 2 })
            {
                var (world, caster, enemy) = Arena(rank);
                var rock = Launch(world, caster, Element.Earth);
                int id = Get<int>(rock, "Id");
                // Cambiar el equipo durante el vuelo no convierte una tirada baja en seis fragmentos.
                world.EquipPlayer(caster.Id, EquipmentModifiers.None with { RockFragmentation = 2 - rank });
                Impact(world, id);
                var fragments = List(world, "_projectiles").Cast<object>().ToArray();
                Check(fragments.Length == (rank == 0 ? 0 : rank == 1 ? 4 : 6), "Cantidad incorrecta de fragmentos.");
                if (rank == 0) continue;
                Check(fragments.All(p => Get<bool>(p, "IsRockFragment") && Get<int>(p, "RockFragmentation") == 0),
                    "Los fragmentos pueden volver a dividirse.");
                foreach (var fragment in fragments) Near(Get<float>(fragment, "Power"), .4f, "El impacto no conserva el 20 % de la carga original");
                Near(fragments.Sum(p => Get<V2>(p, "Velocity").X), 0, "Dispersión horizontal");
                Near(fragments.Sum(p => Get<V2>(p, "Velocity").Y), 0, "Dispersión vertical");
                var victim = world.AddPlayer("Vecino", 1)!; victim.Invulnerable = 0;
                var sideways = fragments.First(p => Get<V2>(p, "Velocity").Y > 5);
                victim.Position = Get<V2>(sideways, "Position") + Get<V2>(sideways, "Velocity").Normalized;
                enemy.Position = new(15, 12);
                Shots(world, 50);
                Near(1000 - victim.Health, 40, "Daño real del fragmento de Tierra");
                Check(world.Snapshot().Projectiles.Length == 0 && world.Snapshot().Fields.Length == 0,
                    "Los fragmentos se reproducen o la Tierra pura deja un área elemental.");
                world.MatchStatistics().Validate();
            }
            var (ice, mage, target) = Arena(2);
            var shard = Launch(ice, mage, Element.Water, Element.Cold);
            Impact(ice, Get<int>(shard, "Id"));
            Check(ice.Snapshot().Projectiles.Length == 0 && ice.Snapshot().Fields.Length == 0,
                "El afijo de roca divide también los proyectiles de hielo.");
        });
        test("Fragmentación: Fuego, Frío y Agua conservan su mezcla y dejan áreas al caer", () =>
        {
            foreach (var (element, kind) in new[] { (Element.Fire, "burning_ground"), (Element.Cold, "frost_ground"), (Element.Water, "water_pool") })
            {
                var (world, caster, enemy) = Arena(1);
                var rock = Launch(world, caster, Element.Earth, element);
                Impact(world, Get<int>(rock, "Id"));
                Check(world.Snapshot().Projectiles.All(p => p.Elements.SequenceEqual(new[] { Element.Earth, element })),
                    "Los fragmentos pierden el elemento secundario.");
                enemy.Position = new(15, 12); Shots(world, 50);
                var areas = List(world, "_fields").Cast<object>().Where(f => Get<bool>(f, "IsRockFragmentArea")).ToArray();
                Check(areas.Length == 4 && areas.All(f => Get<string>(f, "Kind") == kind), "No cae un área por fragmento.");
                foreach (var area in areas)
                {
                    Near(Get<float>(area, "Radius"), 1, "Radio del área del fragmento");
                    Near(Get<float>(area, "Lifetime"), 2, "Duración del área del fragmento");
                    Check(Get<SpellRecipe>(area, "Recipe").Elements.SequenceEqual(new[] { element }), "El suelo vuelve a aplicar Tierra.");
                }
                var field = areas[0]; var fields = List(world, "_fields"); fields.Clear(); fields.Add(field);
                enemy.Position = Get<V2>(field, "Position"); enemy.Health = 1000;
                enemy.Wet = enemy.Burning = enemy.Frozen = enemy.Chill = 0;
                Call(world, "TickWallsAndFields", .1f);
                Near(1000 - enemy.Health, SpellBalance.Damage(new([element])) * SpellBalance.GroundPower * .1f * .2f,
                    "El área del fragmento no aplica el 20 % de daño directo");
                if (element == Element.Water) Check(enemy.Wet > 0, "El charco no moja al objetivo.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        test("Fragmentación: los escudos básicos reflejan la roca y las barreras detienen los fragmentos", () =>
        {
            var (world, caster, reflector) = Arena(2); reflector.Position = new(15, 12);
            AddWall(world, reflector.Id, Element.Shield);
            var rock = Launch(world, caster, Element.Earth); int id = Get<int>(rock, "Id");
            for (int tick = 0; tick < 15 && Get<int>(rock, "Owner") == caster.Id; tick++) Shots(world);
            Check(Get<int>(rock, "Owner") == reflector.Id && world.Snapshot().Projectiles.Length == 1,
                "La roca se divide al reflejarse o pierde el propietario del reflejo.");
            Impact(world, id);
            Check(world.Snapshot().Projectiles is { Length: 6 } reflected && reflected.All(p => p.Owner == reflector.Id),
                "La roca reflejada no conserva sus fragmentos y atribución.");
            Shots(world, 90);
            Check(world.Snapshot().Projectiles.Length == 0, "Los fragmentos reflejados no terminan.");
            var (blocked, mage, enemy) = Arena(1); enemy.Position = new(15, 12);
            AddWall(blocked, enemy.Id, Element.Shield, Element.Earth);
            var stopped = Launch(blocked, mage, Element.Earth, Element.Fire);
            Impact(blocked, Get<int>(stopped, "Id")); Shots(blocked, 50);
            Check(blocked.Snapshot().Projectiles.Length == 0 && blocked.Snapshot().Fields.All(f => f.Position.X < -.3f),
                "Los fragmentos atraviesan la barrera o dejan fuego detrás de ella.");
        });
        test("Fragmentación: proyectiles y áreas respetan los límites de la simulación", () =>
        {
            var (world, caster, enemy) = Arena(2);
            var rock = Launch(world, caster, Element.Earth, Element.Fire);
            var projectiles = List(world, "_projectiles"); var type = rock.GetType();
            while (projectiles.Count < 160)
            {
                var p = Activator.CreateInstance(type, nonPublic: true)!;
                Set(p, "Id", 10000 + projectiles.Count); Set(p, "Owner", caster.Id);
                Set(p, "Position", new V2(15, 12)); Set(p, "Recipe", new SpellRecipe([Element.Earth]));
                Set(p, "Radius", .16f); Set(p, "Power", 1f); projectiles.Add(p);
            }
            Impact(world, Get<int>(rock, "Id"));
            Check(world.Snapshot().Projectiles.Length <= 160, "La fragmentación supera los 160 proyectiles.");
            projectiles.Clear();
            var fieldType = typeof(GameWorld).Assembly.GetType("Arcana.Core.Field")!;
            var fields = List(world, "_fields"); fields.Clear();
            for (int i = 0; i < 40; i++)
            {
                var f = Activator.CreateInstance(fieldType, nonPublic: true)!;
                Set(f, "Id", 20000 + i); Set(f, "Position", new V2(15, 12)); Set(f, "Radius", 1f);
                Set(f, "Lifetime", 4f); Set(f, "Recipe", new SpellRecipe([Element.Fire])); Set(f, "Kind", "burning_ground");
                fields.Add(f);
            }
            rock = Launch(world, caster, Element.Earth, Element.Fire); Impact(world, Get<int>(rock, "Id")); Shots(world, 50);
            Check(world.Snapshot().Fields.Length == 40 && world.Snapshot().Projectiles.Length <= 160,
                "Los fragmentos superan el límite de áreas o generan más proyectiles.");
        });
        test("Fragmentación: guardar durante el vuelo o con áreas conserva el efecto y lee los guardados anteriores", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 1, Seed = 37 });
            var caster = world.AddPlayer("Defensor")!; caster.Position = new(-3, 0); caster.Invulnerable = 0;
            Check((bool)Call(world, "TrySpawnMinion", MinionKind.Melee)!, "No se pudo preparar el enemigo de supervivencia.");
            int enemyId = world.Snapshot().Defense!.Minions.Single().Id;
            var combatants = (IDictionary)typeof(GameWorld).GetField("_combatants", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var enemy = (Wizard)combatants[enemyId]!; enemy.Position = new(-1, 0); enemy.Health = enemy.MaxHealth = 10000;
            typeof(Wizard).GetProperty(nameof(Wizard.Equipment))!.SetValue(caster,
                EquipmentModifiers.None with { RockFragmentation = 2 });
            var rock = Launch(world, caster, Element.Earth, Element.Fire); int id = Get<int>(rock, "Id");
            string run = Guid.NewGuid().ToString("N");
            var save = world.SaveSurvival(caster.Id, run);
            var restored = GameWorld.RestoreSurvival(Protocol.Deserialize<SurvivalSave>(Protocol.Serialize(save))!);
            Impact(world, id); Impact(restored, id);
            Check(Protocol.Serialize(world.Snapshot().Projectiles) == Protocol.Serialize(restored.Snapshot().Projectiles) &&
                world.Snapshot().Projectiles.Length == 6, "Continuar pierde el afijo capturado al lanzar.");
            var fragments = world.SaveSurvival(caster.Id, run);
            var continued = GameWorld.RestoreSurvival(fragments);
            Shots(world, 50); Shots(continued, 50);
            Check(Protocol.Serialize(world.Snapshot().Fields) == Protocol.Serialize(continued.Snapshot().Fields),
                "Continuar cambia dónde caen los fragmentos.");
            var ground = GameWorld.RestoreSurvival(world.SaveSurvival(caster.Id, run));
            Check(List(ground, "_fields").Cast<object>().Any(f => Get<bool>(f, "IsRockFragmentArea")),
                "Guardar el área pierde su escala de daño.");
            var old = JsonNode.Parse(save.State.GetRawText())!;
            foreach (var p in old["Projectiles"]!.AsArray()) { p!.AsObject().Remove("RockFragmentation"); p.AsObject().Remove("IsRockFragment"); }
            var legacy = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(old) });
            Impact(legacy, id);
            Check(legacy.Snapshot().Projectiles.Length == 0, "Un guardado anterior obtiene fragmentación retroactiva.");
            var invalid = JsonNode.Parse(save.State.GetRawText())!; invalid["Projectiles"]![0]!["RockFragmentation"] = 3;
            bool rejected = false;
            try { GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(invalid) }); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "El guardado admite un rango de fragmentación imposible.");
        });
    }
}
