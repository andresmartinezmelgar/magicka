using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunProgression(Action<string, Action> test)
    {
        test("Defensa: tanques progresivos y flujo continuo sin pausas entre grupos", () =>
        {
            var w = World(DefenseFlow.Continuous); var p = w.AddPlayer("Humano")!; p.Position = new(76, 52);
            var baseline = Enemy(w); Check(baseline.Health == 340, "La primera vida no está ajustada.");
            Set(w, "_defenseDifficulty", 5);
            for (int i = 0; i < 40; i++) Enemy(w);
            var tank = State(w).Minions.First(m => m.Kind == MinionKind.Tank);
            var tankBody = ((Dictionary<int, Wizard>)Field(w, "_combatants"))[tank.Id];
            var regular = State(w).Minions.First(m => m.Kind == MinionKind.Melee && m.MaxHealth > baseline.MaxHealth);
            Check(tank.MaxHealth >= regular.MaxHealth * 3.4f && tankBody.Equipment.Speed < baseline.Equipment.Speed, "El tanque no resiste o no tiene contrapartida.");
            var rhythm = World(DefenseFlow.Continuous); rhythm.AddPlayer("Humano")!.Position = new(76, 52);
            var appearances = new List<int>(); int last = 0;
            for (int i = 0; i < 1800; i++) { rhythm.Step(); int count = State(rhythm).Minions.Length; if (count > last) appearances.Add(i); last = count; }
            var gaps = appearances.Zip(appearances.Skip(1), (a, b) => (b - a) / 60f).ToArray();
            Check(gaps.Length >= 18 && gaps.All(g => g is >= 0.30f and <= 0.50f), "El flujo introduce descansos o ráfagas instantáneas.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: diez mejoras desde cada tirada inicial, también negativa o ya al máximo", () =>
        {
            var w = World(); var p = w.AddPlayer("Mejoras")!;
            var original = new EquipmentItem(Guid.NewGuid().ToString("N"), EquipmentSlot.Weapon, ItemTier.Platinum, EquipmentFamily.Mixed,
                [new(EquipmentStat.Power, -0.10f, 0.10f, -0.06f), new(EquipmentStat.SpellRange, 0, 0.10f, 0.10f)], 60);
            Invoke(w, "GrantDefenseItem", p, original); Gold(w, p, 100000);
            var initial = Player(w, p).Items.Single().Item;
            Check(initial.Bonuses[0].Maximum == 0.04f && initial.Bonuses[1].Maximum == 0.20f &&
                original.Bonuses[0].Maximum == 0.10f && original.Bonuses[1].Current == 0.10f,
                "Los límites no son propios de la pieza temporal o se alteró el objeto original.");
            for (int upgrade = 1; upgrade <= 10; upgrade++)
            {
                Check(Command(w, p, "defense_upgrade", itemId: original.Id), "Se bloqueó la mejora " + upgrade);
                var item = Player(w, p).Items.Single();
                Check(item.Upgrades == upgrade && item.Item.Bonuses.Zip(original.Bonuses).All(b =>
                    b.First.Current == MathF.Round(b.Second.Current + upgrade * 0.01f, 4)), "No sumó un punto a todos los atributos.");
                SnapshotValidation.Check(w.Snapshot());
            }
            var final = Player(w, p); int gold = final.Gold;
            Check(final.Items[0].Item.Bonuses[0].Current == 0.04f && final.Items[0].Item.Bonuses[1].Current == 0.20f &&
                gold == 100000 - Enumerable.Range(0, 10).Sum(n => DefenseRules.SurvivalUpgradeCost(original.Tier, n)), "Resultado o coste incorrectos.");
            Check(!Command(w, p, "defense_upgrade", itemId: original.Id) && Player(w, p).Gold == gold, "Se cobró una undécima mejora.");
        });
        test("Supervivencia: recuperar presión lejana conserva identidad, vida, estados y premios", () =>
        {
            var w = World(); var p = w.AddPlayer("Zona nueva")!; p.Position = V2.Zero;
            var enemy = Enemy(w); enemy.Position = new(76, 52);
            enemy.Health = 123; enemy.Frozen = 2; int id = enemy.Id;
            int kills = State(w).TotalKills, gold = Player(w, p).Gold;
            Invoke(w, "ReturnDistantMinion");
            Check(enemy.Id == id && enemy.Health == 123 && enemy.Frozen == 2 && State(w).Minions.Length == 1 &&
                V2.Distance(enemy.Position, p.Position) >= DefenseRules.SafeRadius && V2.Distance(enemy.Position, p.Position) < 60 &&
                w.Map.Walkable(enemy.Position) && State(w).TotalKills == kills && Player(w, p).Gold == gold,
                "El regreso alteró el combate, creó enemigos extra o apareció encima del jugador.");
            var at = enemy.Position; Invoke(w, "ReturnDistantMinion");
            Check(enemy.Position == at, "Se recolocó un enemigo próximo y visible.");
        });
    }
}
