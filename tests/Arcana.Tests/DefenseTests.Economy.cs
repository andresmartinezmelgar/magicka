using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunEconomy(Action<string, Action> test)
    {
        test("Supervivencia: la reparación retirada no consume oro", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; Gold(w, p, 1000);
            Check(!Command(w, p, "defense_repair") && Player(w, p).Gold == 1000, "Se aceptó una reparación retirada.");
        });
        test("Defensa: compras autoritativas, mejoras limitadas y botín personal", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            Gold(w, a, 1000); Check(Command(w, a, "defense_supply"), "No entregó suministros.");
            var item = Player(w, a).Items.Single();
            Check(!Command(w, b, "defense_equip", itemId: item.Item.Id), "Equipó una pieza ajena.");
            Check(Command(w, a, "defense_upgrade", itemId: item.Item.Id), "No mejoró la pieza.");
            var improved = Player(w, a).Items.Single();
            Check(improved.Upgrades == 1 && improved.Item.Bonuses.Zip(item.Item.Bonuses).All(v => v.First.Current >= v.Second.Current && v.First.Current <= v.First.Maximum), "Mejora fuera del rango.");
            int gold = Player(w, a).Gold;
            Check(!Command(w, a, "defense_sell", itemId: item.Item.Id) && Player(w, a).Gold == gold, "Se vendió una pieza equipada.");
            var empty = new EquipmentLoadout();
            Check(w.ApplyMessage(a.Id, new() { Type = "defense_loadout", Sequence = a.LastSequence + 1, Loadout = empty }), "No se confirmó la retirada del equipo.");
            Check(Command(w, a, "defense_sell", itemId: item.Item.Id) && Player(w, a).Gold == gold + item.Item.SaleGold, "No vendió la pieza temporal.");
            Check(!Command(w, a, "defense_sell", itemId: item.Item.Id), "Pagó dos veces por la misma pieza.");
            Invoke(w, "AddDefenseDrop", a.Position, b.Id, DefenseDropKind.Equipment); b.Position = new(30, 20);
            Invoke(w, "TickDefenseDrops", 0.01f);
            Check(State(w).Drops.Length == 1 && Player(w, a).Items.Length == 0, "Robó una recogida asignada.");
            b.Position = a.Position; Invoke(w, "TickDefenseDrops", 0.01f);
            Check(Player(w, b).Items.Length == 1, "El propietario no recogió su pieza.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Defensa: auras dañan minions y curan aliados", () =>
        {
            var w = World(); var p = w.AddPlayer("A")!; var ally = w.AddPlayer("B")!;
            p.Position = new(-28.8f, 0); ally.Position = p.Position + new V2(0, 1); ally.Health = 500;
            Gold(w, p, 1000); Command(w, p, "defense_aura", 0); Command(w, p, "defense_aura", 2);
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(1, 0);
            float health = enemy.Health;
            Invoke(w, "TickDefenseAuras", 1f);
            Check(enemy.Health < health && ally.Health > 500, "Aura dañó o curó un objetivo incorrecto.");
        });
    }
}
