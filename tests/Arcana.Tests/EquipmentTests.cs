using Arcana.Core;
using System.Reflection;

internal static class EquipmentTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(float a, float b) => Check(Math.Abs(a - b) < 0.02f, $"{a} != {b}");
    private static AccountProfile Profile(params EquipmentItem[] items) => new("test-user", "Mago", 0, 0, 0,
        new(items.FirstOrDefault(i => i.Slot == EquipmentSlot.Boots)?.Id ?? "", items.FirstOrDefault(i => i.Slot == EquipmentSlot.Hat)?.Id ?? "",
            items.FirstOrDefault(i => i.Slot == EquipmentSlot.Chest)?.Id ?? "", items.FirstOrDefault(i => i.Slot == EquipmentSlot.Weapon)?.Id ?? ""), items);
    private static EquipmentItem Item(EquipmentSlot slot, EquipmentStat stat, float value, EquipmentFamily family = EquipmentFamily.Mixed, ItemTier tier = ItemTier.Silver) =>
        new(Guid.NewGuid().ToString("N"), slot, tier, family, [new(stat, Math.Min(-0.04f, value), Math.Max(0.14f, value), value)], 10);
    private static object? Call(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static (GameWorld World, Wizard A, Wizard B) Arena()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 7 });
        var a = world.AddPlayer("A", 0)!; var b = world.AddPlayer("B", 1)!;
        a.Position = new(-3, 0); b.Position = new(3, 0); a.Invulnerable = b.Invulnerable = 0;
        return (world, a, b);
    }
    public static void Run(Action<string, Action> test)
    {
        EquipmentElementTests.Run(test);
        test("Equipo: 20.000 tiradas válidas con cinco calidades, rangos y posición principal", () =>
        {
            var random = new Random(441); bool penalty = false;
            foreach (var tier in Enum.GetValues<ItemTier>()) foreach (var slot in Enum.GetValues<EquipmentSlot>())
            for (int i = 0; i < 1000; i++)
            {
                var item = EquipmentCatalogue.Generate(random, tier: tier, slot: slot);
                Check(EquipmentCatalogue.Valid(item), "Tirada fuera de rango.");
                Check(item.Bonuses[0].Stat == EquipmentCatalogue.MainStat(slot, item.Family), "Se perdió la especialidad de la posición.");
                Check(item.Bonuses[0].Current > 0, "El atributo principal debe ofrecer una ventaja.");
                if (tier >= ItemTier.Diamond)
                    Check(item.Family != EquipmentFamily.Mixed && item.Bonuses.All(b => b.Minimum > 0 && b.Current > 0), "Un nivel alto tiene penalizaciones o no está especializado.");
                else { Check(item.Family == EquipmentFamily.Mixed, "Una mezcla baja quedó atada a una familia."); penalty |= item.Bonuses.Any(b => b.Current < 0); }
            }
            Check(penalty, "Los tres niveles bajos no generan compensaciones.");
        });
        test("Equipo: serialización conserva mínimo, máximo, valor y versión de generación", () =>
        {
            var item = EquipmentCatalogue.Generate(new(4), tier: ItemTier.Gold);
            var saved = Protocol.Deserialize<EquipmentItem>(Protocol.Serialize(item))!;
            Check(saved.Id == item.Id && saved.GenerationVersion == item.GenerationVersion && saved.Bonuses.SequenceEqual(item.Bonuses), "Se modificó una tirada al recuperar el objeto.");
        });
        test("Equipo: bonus Mítico de dos y cuatro piezas; conjuntos mixtos y reemplazo", () =>
        {
            var slots = Enum.GetValues<EquipmentSlot>();
            EquipmentItem Piece(int n, EquipmentFamily family) => Item(slots[n], EquipmentStat.EnergyRegeneration, 0.01f, family, ItemTier.Mythic);
            var two = EquipmentModifiers.Calculate(Profile(Piece(0, EquipmentFamily.Life), Piece(1, EquipmentFamily.Life)));
            Near(two.Healing, 1.05f);
            var four = EquipmentModifiers.Calculate(Profile(slots.Select((s, n) => Piece(n, EquipmentFamily.Life)).ToArray()));
            Near(four.Healing, 1.15f); Near(four.LifeCost, 0.9f);
            var mixed = EquipmentModifiers.Calculate(Profile(Piece(0, EquipmentFamily.Life), Piece(1, EquipmentFamily.Life), Piece(2, EquipmentFamily.Fire), Piece(3, EquipmentFamily.Fire)));
            Near(mixed.Healing, 1.05f); Near(mixed.Fire, 1.05f);
            var shield = EquipmentModifiers.Calculate(Profile(slots.Select((s, n) => Piece(n, EquipmentFamily.Shield)).ToArray()));
            Near(shield.ShieldCapacity, 1.20f); Near(shield.ShieldDuration, 1.20f);
            var diamond = Item(slots[0], EquipmentStat.EnergyRegeneration, 0.01f, EquipmentFamily.Life, ItemTier.Diamond);
            Check(EquipmentModifiers.Calculate(Profile(diamond, Piece(1, EquipmentFamily.Life))).LifePieces == 1, "Diamante contó como Mítico.");
        });
        test("Equipo: desglose suma piezas y conjunto y distingue los límites reales del combate", () =>
        {
            var items = Enum.GetValues<EquipmentSlot>().Select(s => new EquipmentItem(Guid.NewGuid().ToString("N"), s,
                ItemTier.Mythic, EquipmentFamily.Life, [new(EquipmentStat.HealingPower, 0, 0.5f, 0.2f),
                    new(EquipmentStat.MovementSpeed, -0.1f, 0.1f, s == EquipmentSlot.Boots ? -0.04f : 0.01f)], 400)).ToArray();
            var summary = EquipmentModifiers.Summarize(Profile(items));
            var heal = summary.Stats.Single(s => s.Stat == EquipmentStat.HealingPower);
            Near(heal.Items, 0.8f); Near(heal.Set, 0.15f); Near(heal.Sum, 0.95f); Near(heal.Effective, 0.6f);
            Check(heal.Limited, "No se indicó el límite de curación."); Near(summary.Modifiers.Healing, 1.6f);
            var speed = summary.Stats.Single(s => s.Stat == EquipmentStat.MovementSpeed);
            Near(speed.Items, -0.01f); Check(!speed.Limited && speed.Set == 0, "Se perdió una penalización o se añadió un conjunto ajeno.");
            var saved = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(Profile(items)))!;
            using var account = new AccountClient(); account.RestoreOfflineProfile(saved);
            Check(account.Profile!.Loadout == saved.Loadout && account.Profile.Items.SelectMany(i => i.Bonuses).SequenceEqual(items.SelectMany(i => i.Bonuses)),
                "Restaurar el inventario perdió la selección o volvió a sortear los valores.");
            account.EquipOffline(EquipmentSlot.Boots, "");
            var three = EquipmentModifiers.Summarize(account.Profile!);
            Near(three.Stats.Single(s => s.Stat == EquipmentStat.HealingPower).Set, 0.05f);
            Check(three.Modifiers.LifePieces == 3 && saved.Loadout.Boots == items[0].Id, "Quitar una pieza alteró la selección guardada original.");
        });
        test("Equipo: vender desequipa, paga oro y rechaza ventas repetidas o ajenas", () =>
        {
            var item = EquipmentCatalogue.Generate(new(5)); var profile = Profile(item) with { Gold = 9007199254740993L };
            var sold = AccountInventory.Sell(profile, item.Id);
            Check(sold.Items.Length == 0 && sold.Loadout.Get(item.Slot) == "" && sold.Gold == profile.Gold + item.SaleGold, "La venta perdió precisión o dejó equipo colgante.");
            try { AccountInventory.Sell(sold, item.Id); throw new Exception("Se pagó dos veces."); } catch (ArgumentException) { }
            try { AccountInventory.Equip(sold, new(item.Id, item.Slot)); throw new Exception("Se equipó un objeto vendido."); } catch (ArgumentException) { }
        });
        test("Equipo: velocidad y alcance modifican la simulación, no solo las cifras", () =>
        {
            float Walk(bool equipped)
            {
                var (world, a, _) = Arena(); a.Position = new(-7, 0);
                if (equipped) world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Boots, EquipmentStat.MovementSpeed, 0.2f))));
                for (int n = 0; n < 30; n++) { world.ApplyMessage(a.Id, new() { Type = "input", Sequence = n + 1, MoveX = 1, AimX = 20 }); world.Step(); }
                return a.Position.X + 7;
            }
            Near(Walk(true) / Walk(false), 1.2f);
            var (ranged, caster, target) = Arena(); caster.Position = new(-10, 0); target.Position = new(9, 0);
            ranged.EquipPlayer(caster.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Hat, EquipmentStat.SpellRange, 0.25f))));
            caster.Channel = new([Element.Arcane]); caster.Facing = new(1, 0);
            ranged.Step(); Check(target.Health < 1000, "El gorro no amplió el rayo."); SnapshotValidation.Check(ranged.Snapshot());
        });
        test("Equipo: defensa y arma se aplican al daño real; curación y escudos conservan reglas", () =>
        {
            var (world, a, b) = Arena();
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Weapon, EquipmentStat.Power, 0.10f))));
            world.EquipPlayer(b.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Chest, EquipmentStat.Defense, 0.10f))));
            Call(world, "Hit", b, new SpellRecipe([Element.Arcane]), 1f, a.Id, V2.Zero, 1f);
            Near(1000 - b.Health, 115 * 1.1f * 0.9f);
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Weapon, EquipmentStat.HealingPower, 0.20f))));
            b.Health = 500; Call(world, "Hit", b, new SpellRecipe([Element.Life]), 1f, a.Id, V2.Zero, 1f); Near(b.Health, 668);
            world.EquipPlayer(a.Id, EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Weapon, EquipmentStat.ShieldCapacity, 0.20f), Item(EquipmentSlot.Hat, EquipmentStat.ShieldDuration, 0.25f))));
            a.Selection.Add(Element.Shield); Call(world, "SelfCast", a); Near(a.Shield, 504); Near(a.ShieldTime, 15);
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Equipo: las piezas permanecen al morir y al reiniciar la partida", () =>
        {
            var (world, a, b) = Arena(); var gear = EquipmentModifiers.Calculate(Profile(Item(EquipmentSlot.Boots, EquipmentStat.MovementSpeed, 0.12f)));
            world.EquipPlayer(a.Id, gear); Call(world, "Damage", a, 2000f, b.Id, null); Check(!a.Alive && a.Equipment == gear, "La muerte eliminó el equipo.");
            Call(world, "Respawn", a); Check(a.Alive && a.Equipment == gear, "La reaparición eliminó el equipo.");
            Call(world, "ResetMatch"); Check(a.Equipment == gear, "El reinicio eliminó el equipo.");
        });
        test("Recompensas: solo humanos rivales activos, sin práctica, duplicados ni victorias contra IA", () =>
        {
            var match = new RewardMatch(Guid.NewGuid().ToString("N"), "server", GameMode.Duel, ArenaMapId.Classic, DateTimeOffset.UtcNow,
                [new("a", 1, 0, 20, true), new("b", 2, 1, 20, false)]);
            Check(RewardRules.Eligible(match).Length == 2, "Se rechazó un PvP válido.");
            Check(RewardRules.Eligible(match with { Participants = [match.Participants[0]] }).Length == 0, "La IA concedió recompensa.");
            Check(RewardRules.Eligible(match with { Map = ArenaMapId.Practice }).Length == 0, "La práctica concedió recompensa.");
            Check(RewardRules.Eligible(match with { Participants = [match.Participants[0], match.Participants[1] with { Uid = "a" }] }).Length == 0, "Se recompensó una cuenta enfrentada a sí misma.");
            Check(RewardRules.Eligible(match with { Participants = [match.Participants[0], match.Participants[1] with { ActiveSeconds = 1 }] }).Length == 0, "Una partida sin participación obtuvo premio.");
        });
    }
}
