using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class SurvivalAuraAndOracleTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Call(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static (GameWorld World, Wizard Player, object Progress) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Seed = 431 });
        var player = world.AddPlayer("Defensor")!; player.Position = new(-25, 0); player.Invulnerable = 0;
        Set(world, "_spawnClock", 1000f); Set(world, "_bossClock", 170f);
        return (world, player, ((IDictionary)Get(world, "_defenders"))[player.Id]!);
    }
    private static Wizard Spawn(GameWorld world, MinionKind kind, V2 position)
    {
        Check((bool)Call(world, "TrySpawnMinion", kind)!, "No se creó el enemigo de prueba.");
        var state = ((IDictionary)Get(world, "_minions")).Values.Cast<object>().Last();
        var body = (Wizard)Get(state, "Body"); body.Position = position; Set(state, "Attack", 1000f); return body;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Auras nuevas: Arcano daña escudos y Agua moja y daña solo a enemigos visibles", () =>
        {
            var (world, player, progress) = World(); var ally = world.AddPlayer("Aliado")!;
            ally.Position = player.Position; ally.Health = player.Health = 700;
            var enemy = Spawn(world, MinionKind.Melee, player.Position + new V2(2, 0));
            enemy.Health = enemy.MaxHealth = 1000; enemy.Shield = enemy.ShieldMax = 100; enemy.ShieldTime = 6;
            var levels = (int[])Get(progress, "Auras"); levels[(int)DefenseAura.Arcane] = 2; levels[(int)DefenseAura.Water] = 2;
            Call(world, "TickDefenseAuras", 1f);
            Check(enemy.Shield == 28 && enemy.Health == 1000 && enemy.Wet == DefenseRules.WaterAuraWetSeconds,
                "El aura no resolvió 48 de Arcano y 24 de Agua contra el escudo.");
            Check(player.Wet == 0 && ally.Wet == 0 && player.Health == 700 && ally.Health == 700,
                "Las áreas afectaron al lanzador o a un aliado.");
            var damage = world.Snapshot().Players.Single(p => p.Id == player.Id).Damage!;
            Check(damage.Shield[(int)Element.Arcane] == 48 && damage.Shield[(int)Element.Water] == 24,
                "Las auras no aparecen en el resumen personal.");
            enemy.Position = new(2, 0); enemy.Wet = 0; float shield = enemy.Shield;
            Call(world, "TickDefenseAuras", 1f); Check(enemy.Wet == 0 && enemy.Shield == shield, "El aura afecta fuera de alcance.");
            enemy.Position = player.Position + new V2(2, 0);
            Call(world, "AddWall", player, new SpellRecipe([Element.Shield, Element.Earth]), new V2(-24, -1), new V2(-24, 1));
            Call(world, "TickDefenseAuras", 1f); Check(enemy.Wet == 0 && enemy.Shield == shield, "El aura atraviesa un muro.");
        });
        test("Auras nuevas: compras, diez niveles, prismas y exclusión del PvP", () =>
        {
            var (world, player, progress) = World(); Set(progress, "Gold", 1000000);
            foreach (var aura in new[] { DefenseAura.Arcane, DefenseAura.Water })
            {
                for (int level = 0; level < DefenseRules.MaxAuraLevel; level++)
                {
                    int before = (int)Get(progress, "Gold");
                    Check(world.ApplyMessage(player.Id, new() { Type = "defense_aura", Sequence = player.LastSequence + 1, Slot = (int)aura }), "No se compró el aura.");
                    Check(before - (int)Get(progress, "Gold") == DefenseRules.AuraCost(level), "El precio del aura no coincide.");
                }
                Check(!world.ApplyMessage(player.Id, new() { Type = "defense_aura", Sequence = player.LastSequence + 1, Slot = (int)aura }), "El aura superó diez niveles.");
            }
            var prism = Spawn(world, MinionKind.Prism, player.Position + new V2(1, 0));
            typeof(Wizard).GetProperty("VulnerableElement", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(prism, Element.Fire);
            prism.Health = prism.MaxHealth = 1000; Call(world, "TickDefenseAuras", .25f);
            Check(prism.Health == 1000 && prism.Wet == 0, "Un aura ignoró la inmunidad del prisma.");
            typeof(Wizard).GetProperty("VulnerableElement", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(prism, Element.Water);
            Call(world, "TickDefenseAuras", .25f);
            Check(prism.Health == 970 && prism.Wet > 0, "Agua no afectó al prisma vulnerable o Arcano atravesó su inmunidad.");
            var pvp = new GameWorld(new() { Mode = GameMode.Arena }); var human = pvp.AddPlayer("PvP")!;
            Check(!pvp.ApplyMessage(human.Id, new() { Type = "defense_aura", Slot = (int)DefenseAura.Water, Sequence = 1 }), "Se habilitó el aura de supervivencia en PvP.");
            Call(pvp, "TickDefenseAuras", .25f);
        });
        test("Oráculo: límite natural, intervalo sin reemplazo inmediato y aumento de fuerza individual", () =>
        {
            var (world, player, _) = World(); Set(world, "_defenseDifficulty", 6);
            var oracle = Spawn(world, MinionKind.Oracle, new(-5, 0));
            float cooldown = (float)Get(world, "_oracleSpawnCooldown");
            Check(cooldown >= 90 && cooldown <= 150 && oracle.MaxHealth == DefenseRules.MinionHealth(MinionKind.Oracle, 6, 1) &&
                DefenseRules.MinionHealth(MinionKind.Oracle, 1, 1) == 1400 && DefenseRules.OracleStrikeDamage == 200,
                "El encuentro no tiene intervalo o no aumentó su fuerza individual.");
            void NaturalAttempts(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Call(world, "SpawnMinion");
                    foreach (var state in ((IDictionary)Get(world, "_minions")).Values.Cast<object>())
                        if ((MinionKind)Get(state, "Kind") != MinionKind.Oracle) ((Wizard)Get(state, "Body")).Health = 0;
                    Call(world, "CleanupDefense");
                }
            }
            Set(world, "_oracleSpawnCooldown", 0f); NaturalAttempts(500);
            Check(world.Snapshot().Defense!.Minions.Count(m => m.Kind == MinionKind.Oracle) == 1, "La generación natural creó varios oráculos.");
            oracle.Health = 0; Call(world, "CleanupDefense"); Set(world, "_oracleSpawnCooldown", 100f); NaturalAttempts(500);
            Check(world.Snapshot().Defense!.Minions.All(m => m.Kind != MinionKind.Oracle), "Se reemplazó al oráculo durante su intervalo.");
            Call(world, "TickDefense", .25f); Check((float)Get(world, "_oracleSpawnCooldown") == 99.75f, "El intervalo no avanza con el combate.");
        });
        test("Guardado: conserva cinco auras, daño personal e intervalo del oráculo y acepta partidas anteriores", () =>
        {
            var (world, player, progress) = World();
            var enemy = Spawn(world, MinionKind.Melee, new(-24, 0)); enemy.Health = enemy.MaxHealth = 1000;
            ((int[])Get(progress, "Auras"))[(int)DefenseAura.Arcane] = 2;
            ((int[])Get(progress, "Auras"))[(int)DefenseAura.Water] = 3;
            Set(world, "_oracleSpawnCooldown", 123.5f); Call(world, "TickDefenseAuras", .25f);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(JsonSerializer.Deserialize<SurvivalSave>(JsonSerializer.Serialize(save))!);
            var before = world.Snapshot(); var after = restored.Snapshot();
            Check(Protocol.Serialize(before.Players.Single(p => p.Id == player.Id).Damage) == Protocol.Serialize(after.Players.Single(p => p.Id == player.Id).Damage) &&
                after.Defense!.Defenders.Single(p => p.PlayerId == player.Id).Auras.SequenceEqual(new[] { 0, 0, 0, 2, 3 }) &&
                (float)Get(restored, "_oracleSpawnCooldown") == 123.5f, "El guardado reinició el daño, las auras o el intervalo.");
            var legacy = JsonNode.Parse(save.State.GetRawText())!; legacy.AsObject().Remove("OracleSpawnCooldown");
            foreach (var entry in legacy["Defenders"]!.AsArray())
            {
                entry!["Body"]!.AsObject().Remove("Damage"); entry["Progress"]!["Auras"] = JsonSerializer.SerializeToNode(new[] { 0, 0, 0 });
            }
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) }).Snapshot();
            Check(old.Players.Single(p => p.Id == player.Id).Damage == null && old.Defense!.Defenders.Single().Auras.SequenceEqual(new[] { 0, 0, 0, 0, 0 }),
                "La migración de una partida anterior falló.");
            SnapshotValidation.Check(after); SnapshotValidation.Check(old);
        });
    }
}
