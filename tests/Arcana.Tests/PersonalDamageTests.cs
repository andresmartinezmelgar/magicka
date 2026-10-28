using System.Collections;
using System.Reflection;
using System.Text.Json;
using Arcana.Core;

internal static class PersonalDamageTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object? Call(GameWorld world, string name, params object?[] args) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static MatchDamageSnapshot? DamageOf(GameWorld world, Wizard player) => world.Snapshot().Players.Single(p => p.Id == player.Id).Damage;
    private static void Hit(GameWorld world, Wizard target, Wizard owner, float amount, params Element[] elements) => Call(world, "Damage", target, amount, owner.Id, elements);
    private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < .001, message);
    private static GameWorld World(GameMode mode) => new(new() { Mode = mode, MaxPlayers = 4,
        Map = mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic,
        SurvivalMap = mode == GameMode.Defense ? SurvivalMapSpec.Legacy : null });
    private static Wizard Enemy(GameWorld world)
    {
        Call(world, "TrySpawnMinion", MinionKind.Melee);
        var states = (IDictionary)typeof(GameWorld).GetField("_minions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
        var state = states.Values.Cast<object>().Last();
        return (Wizard)state.GetType().GetField("Body")!.GetValue(state)!;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Absorción personal: todos los modos cuentan el daño real detenido, conservan al reaparecer y reinician por partida", () =>
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                var world = World(mode); var player = world.AddPlayer("Defensor", 0)!;
                var enemy = mode == GameMode.Defense ? Enemy(world) : world.AddPlayer("Rival", 1)!;
                player.Invulnerable = enemy.Invulnerable = 0;
                player.Shield = player.ShieldMax = 30; player.ShieldTime = 5;
                Hit(world, player, enemy, 13, Element.Fire);
                var protectedOnly = DamageOf(world, player)!;
                Near(protectedOnly.Total, 0, "La absorción se sumó al daño infligido.");
                Near(protectedOnly.PersonalShieldAbsorbed, 13, "No cuenta si el jugador todavía no ha atacado.");
                Hit(world, player, enemy, 10000, Element.Fire); Call(world, "Respawn", player);
                Near(DamageOf(world, player)!.PersonalShieldAbsorbed, 30, "Pierde la absorción al morir o suma exceso.");
                var transported = JsonSerializer.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()), Protocol.Json)!;
                SnapshotValidation.Check(transported);
                Near(transported.Players.Single(p => p.Id == player.Id).Damage!.PersonalShieldAbsorbed, 30, "La red pierde la absorción.");
                world.ResetForWaitingRoom();
                Check(DamageOf(world, player) == null, "La absorción pasa a otra partida.");
            }
        });
        test("Absorción personal: dos humanos de la misma clase conservan cifras independientes al guardar y continuar", () =>
        {
            var world = World(GameMode.Defense); var a = world.AddPlayer("A")!; var b = world.AddPlayer("B")!;
            var enemy = Enemy(world); a.Invulnerable = b.Invulnerable = enemy.Invulnerable = 0;
            foreach (var p in new[] { a, b }) { p.Shield = p.ShieldMax = 100; p.ShieldTime = 5; }
            Hit(world, a, enemy, 31, Element.Fire); Hit(world, b, enemy, 7, Element.Fire);
            Near(DamageOf(world, a)!.PersonalShieldAbsorbed, 31, "Mezcla jugadores de la misma clase.");
            Near(DamageOf(world, b)!.PersonalShieldAbsorbed, 7, "Comparte el contador personal.");
            world.RemovePlayer(b.Id);
            var save = world.SaveSurvival(a.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var human = restored.Players[a.Id];
            Near(DamageOf(restored, human)!.PersonalShieldAbsorbed, 31, "Continuar pierde la absorción acumulada.");
            var legacy = JsonSerializer.Deserialize<MatchDamageSnapshot>("{\"Health\":[0,0,0,0,0,0,0,0,0],\"Shield\":[0,0,0,0,0,0,0,0,0]}")!;
            Check(legacy.Valid() && legacy.PersonalShieldAbsorbed == 0 && legacy.BarrierAbsorbed == 0, "Rompe los resúmenes anteriores.");
        });
        test("Daño personal: todos los modos separan vida y escudos, omiten exceso y conservan el total al reaparecer", () =>
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                var world = World(mode); var player = world.AddPlayer("Autor", 0)!;
                var enemy = mode == GameMode.Defense ? Enemy(world) : world.AddPlayer("Rival", 1)!;
                player.Invulnerable = enemy.Invulnerable = 0;
                enemy.Health = 50; enemy.Shield = enemy.ShieldMax = 30; enemy.ShieldTime = 6;
                Hit(world, enemy, player, 100, Element.Fire);
                var damage = DamageOf(world, player)!;
                Near(damage.Health[(int)Element.Fire], 50, "Incluye exceso de daño en " + mode);
                Near(damage.Shield[(int)Element.Fire], 30, "Perdió el escudo en " + mode);
                Near(damage.Total, 80, "Duplica el daño en " + mode);
                Hit(world, player, enemy, 10000, Element.Arcane); Call(world, "Respawn", player);
                Near(DamageOf(world, player)!.Total, 80, "La muerte o reaparición borró la partida en " + mode);
                world.ResetForWaitingRoom();
                Check(DamageOf(world, player) == null, "Una partida nueva arrastra el daño en " + mode);
            }
        });
        test("Daño personal: dos humanos de la misma clase conservan cifras propias y las mezclas usan su daño real", () =>
        {
            var world = World(GameMode.Defense); var a = world.AddPlayer("A")!; var b = world.AddPlayer("B")!;
            var enemy = Enemy(world); enemy.Health = enemy.MaxHealth = 10000; enemy.Invulnerable = 0;
            Call(world, "HitAs", StatisticAttack.Beam, enemy, new SpellRecipe([Element.Arcane, Element.Fire]), .1f, a.Id, V2.Zero);
            Hit(world, enemy, b, 13, Element.Lightning);
            var first = DamageOf(world, a)!; var second = DamageOf(world, b)!;
            Near(first.Health[(int)Element.Arcane], SpellBalance.Arcane * .1, "La mezcla no ponderó Arcano.");
            Near(first.Health[(int)Element.Fire], SpellBalance.Fire * .1, "La mezcla no ponderó Fuego.");
            Near(first.Health[(int)Element.Lightning], 0, "Sumó el daño del otro humano.");
            Near(second.Total, 13, "La clase compartida mezcló a los jugadores.");
            first.Health[0] = 999;
            Near(DamageOf(world, a)!.Health[0], 0, "La captura comparte matrices con la simulación.");
            var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()), Protocol.Json)!;
            SnapshotValidation.Check(snapshot);
            Near(snapshot.Players.Single(p => p.Id == a.Id).Damage!.Total, SpellBalance.Arcane * .1 + SpellBalance.Fire * .1, "La red perdió el desglose.");
        });
        test("Daño personal: el fuego amigo y el daño propio no inflan el total; las barreras enemigas sí cuentan", () =>
        {
            var world = World(GameMode.Warfare); var a = world.AddPlayer("Autor", 0)!;
            var ally = world.AddPlayer("Aliado", 0)!; var enemy = world.AddPlayer("Rival", 1)!;
            a.Invulnerable = ally.Invulnerable = enemy.Invulnerable = 0;
            Hit(world, a, a, 7, Element.Fire); Hit(world, ally, a, 11, Element.Fire);
            Check(DamageOf(world, a) == null, "Se contabilizó daño propio o amigo.");
            Call(world, "AddWall", enemy, new SpellRecipe([Element.Shield, Element.Earth]), new V2(0, 2), new V2(0, 4));
            var walls = (IList)typeof(GameWorld).GetField("_walls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
            var wall = walls[0]!; wall.GetType().GetField("Health")!.SetValue(wall, 100f);
            Call(world, "DamageWall", wall, new SpellRecipe([Element.Earth]), a.Id, 1000f);
            var damage = DamageOf(world, a)!;
            Near(damage.Shield[(int)Element.Earth], 100, "La barrera perdió o duplicó su daño efectivo.");
            Near(damage.HealthTotal, 0, "La barrera se anotó como vida.");
            wall.GetType().GetField("Health")!.SetValue(wall, 158f);
            Call(world, "DamageWall", wall, new SpellRecipe([Element.Earth, Element.Fire]), a.Id, 1000f);
            damage = DamageOf(world, a)!;
            Near(damage.Shield[(int)Element.Earth], 100 + 158 * 18d / 76, "La barrera mixta no ponderó la resistencia a Tierra.");
            Near(damage.Shield[(int)Element.Fire], 158 * 58d / 76, "La barrera mixta no ponderó el daño de Fuego.");
            var defended = DamageOf(world, enemy)!;
            Near(defended.BarrierAbsorbed, 258, "La barrera no cuenta para su creador.");
            Near(defended.PersonalShieldAbsorbed, 0, "La barrera se sumó al escudo personal.");
            Near(defended.Total, 0, "La absorción de la barrera infla el daño infligido.");
        });
        test("Daño personal: auras, quemaduras y daño sin elemento tienen atribución personal y de origen", () =>
        {
            var world = World(GameMode.Arena); var a = world.AddPlayer("Autor", 0)!; var enemy = world.AddPlayer("Rival", 1)!;
            enemy.Invulnerable = 0;
            foreach (var (attack, element, amount) in new[] { (StatisticAttack.ArcaneAura, Element.Arcane, 24f),
                (StatisticAttack.WaterAura, Element.Water, 12f), (StatisticAttack.Burn, Element.Fire, 8f) })
                Call(world, "DamageAs", attack, enemy, amount, a.Id, new[] { element });
            Call(world, "Damage", enemy, 9f, a.Id, null);
            var damage = DamageOf(world, a)!; var report = world.MatchStatistics();
            Near(damage.Total, 53, "Se omitió una fuente de daño."); Near(damage.Health[8], 9, "El daño sin elemento se perdió.");
            foreach (var element in new[] { Element.Arcane, Element.Water })
                Near(report.Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, (int)StatisticDamageSource.Aura * 9 + (int)element),
                    damage.Health[(int)element], "El aura se atribuye a un ataque directo.");
            MatchStatisticsReport.Parse(report.ToJson());
            var snapshot = world.Snapshot();
            foreach (var invalid in new[] { new MatchDamageSnapshot([1], new double[9]), new MatchDamageSnapshot(new double[9], [double.NaN]),
                new MatchDamageSnapshot(Enumerable.Repeat(-1d, 9).ToArray(), new double[9]),
                new MatchDamageSnapshot(new double[9], new double[9]) { PersonalShieldAbsorbed = -1 },
                new MatchDamageSnapshot(new double[9], new double[9]) { BarrierAbsorbed = double.PositiveInfinity } })
            {
                bool rejected = false;
                try { SnapshotValidation.Check(snapshot with { Players = snapshot.Players.Select(p => p.Id == a.Id ? p with { Damage = invalid } : p).ToArray() }); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "La red aceptó un desglose de daño inválido.");
            }
        });
    }
}
