using System.Collections;
using System.Globalization;
using System.Reflection;
using Arcana.Core;

internal static class BestiaryStatsTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Field(GameWorld w, string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
    private static void Set(GameWorld w, string name, object value) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(w, value);
    private static object? Call(GameWorld w, string method, params object?[] args) => typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Row(MinionKind kind, string name, int stage = 1, int party = 1) => BestiaryCatalogue.Stats(kind, stage, party).Single(s => s.Name == name).Value;
    private static (GameWorld World, Wizard Target) Setup(int stage = 1, int party = 1)
    {
        var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 });
        var target = w.AddPlayer("Objetivo")!; target.Invulnerable = 0;
        for (int i = 1; i < party; i++) w.AddPlayer("Aliado " + i, bot: true);
        Set(w, "_defenseDifficulty", stage); Set(w, "_defensePartySize", party);
        return (w, target);
    }
    private static Wizard Spawn(GameWorld w, MinionKind kind)
    {
        Check((bool)Call(w, "TrySpawnMinion", kind)!, "No se pudo crear el enemigo.");
        int id = w.Snapshot().Defense!.Minions.Last().Id;
        return ((Dictionary<int, Wizard>)Field(w, "_combatants"))[id];
    }
    public static void Run(Action<string, Action> test)
    {
        test("Bestiario: vida y velocidad coinciden con los trece enemigos al escalar dificultad y grupo", () =>
        {
            foreach (int stage in new[] { 1, 5, 61 })
            foreach (int party in new[] { 1, 4 })
            {
                var (w, _) = Setup(stage, party);
                foreach (var kind in Enum.GetValues<MinionKind>())
                {
                    var enemy = Spawn(w, kind); var rows = BestiaryCatalogue.Stats(kind, stage, party);
                    Check(Row(kind, "Vida máxima", stage, party) == N(enemy.MaxHealth), "Vida incorrecta: " + kind);
                    Check(Row(kind, "Velocidad", stage, party) == N(enemy.Equipment.Speed * Wizard.BaseMovementSpeed) + " m/s", "Velocidad incorrecta: " + kind);
                    Check(rows.Length >= 5 && rows.All(s => s.Name.Length > 0 && s.Value.Length > 0), "Ficha incompleta: " + kind);
                }
            }
        });
        test("Bestiario: daño por hechizo coincide con impactos reales, afinidades y carga del haz", () =>
        {
            foreach (int stage in new[] { 1, 5 })
            foreach (var kind in new[] { MinionKind.Mage, MinionKind.Warlock, MinionKind.Cryomancer })
            {
                var (w, target) = Setup(stage); var enemy = Spawn(w, kind);
                foreach (var spell in DefenseRules.EnemySpells(kind))
                {
                    var shown = BestiaryCatalogue.SpellDamage(kind, spell, stage);
                    float scale = spell.Shape == SpellShape.Rock ? DefenseRules.EnemyRockPower : spell.Shape == SpellShape.Shards ? SpellBalance.ShardPower : 1;
                    foreach (bool maximum in new[] { false, true })
                    {
                        target.Health = 100000; target.MaxHealth = 100000; target.Wet = target.Burning = target.Chill = target.Frozen = 0;
                        float beam = spell.Shape != SpellShape.Beam ? 1 : maximum ? SpellBalance.BeamDamageMultiplier(DefenseRules.EnemyChannelSeconds(spell)) : SpellBalance.BeamMinimumDamage;
                        Call(w, "Hit", target, spell, scale, enemy.Id, V2.Zero, beam);
                        float expected = maximum ? shown.Maximum : shown.Minimum;
                        Check(Math.Abs((100000 - target.Health) - expected) < 0.01f, "Daño distinto del combate: " + kind + "/" + spell.Shape);
                    }
                    Check(shown.PerSecond == (spell.Shape is SpellShape.Beam or SpellShape.Spray or SpellShape.Lightning), "Unidad incorrecta del daño.");
                    if (spell.Shape == SpellShape.Shards) Check(shown.Projectiles == 5, "Se confundió el daño por esquirla con el daño total.");
                }
            }
            Check(DefenseRules.EnemySpells(MinionKind.Mage).Length == 4 && DefenseRules.EnemySpells(MinionKind.Warlock).Length == 2 &&
                DefenseRules.EnemySpells(MinionKind.Cryomancer).Length == 3, "Faltan ataques en el catálogo.");
        });
        test("Bestiario: curación, escudo, áreas y daños físicos coinciden con el combate", () =>
        {
            var (w, target) = Setup(); var shaman = Spawn(w, MinionKind.Shaman); var ally = Spawn(w, MinionKind.Melee);
            shaman.Position = ally.Position + new V2(1, 0); ally.Health = ally.MaxHealth - 180;
            var shamanState = ((IDictionary)Field(w, "_minions"))[shaman.Id]!;
            float previous = ally.Health; Call(w, "TickDefenseShaman", shamanState, DefenseRules.ShamanHealInterval + 0.1f);
            Check(Row(MinionKind.Shaman, "Curación a un aliado").StartsWith(N(ally.Health - previous) + " "), "Curación incorrecta.");
            var sentinel = Spawn(w, MinionKind.Sentinel); var sentinelState = ((IDictionary)Field(w, "_minions"))[sentinel.Id]!;
            Call(w, "UpdateDefenseEnemyDefense", sentinelState, 0f);
            Check(Row(MinionKind.Sentinel, "Escudo de tierra") == N(sentinel.Shield), "Escudo incorrecto.");
            foreach (var kind in Enum.GetValues<MinionKind>().Where(k => !DefenseRules.SpellcastingEnemy(k)))
            {
                var enemy = Spawn(w, kind); target.Health = 1000;
                float amount = kind == MinionKind.Archer ? SpellBalance.Earth * DefenseRules.ArrowPower : DefenseRules.MinionMeleeAttack(kind);
                Call(w, "Damage", target, amount, enemy.Id, null);
                Check(Row(kind, kind == MinionKind.Archer ? "Daño por flecha" : "Daño por golpe") == N(1000 - target.Health), "Daño físico incorrecto.");
                if (DefenseRules.AreaAttackRadius(kind) > 0)
                    Check(Row(kind, "Radio del ataque de área") == N(DefenseRules.AreaAttackRadius(kind)) + " m", "Área incorrecta.");
            }
        });
    }
}
