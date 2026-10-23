using Arcana.Core;
using System.Text.Json;

internal static class UsabilityTests
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    public static void Run(Action<string, Action> test)
    {
        SaleRecoveryTests.Run(test);
        EquipmentItem Item(EquipmentSlot slot, EquipmentFamily family, float value, ItemTier tier = ItemTier.Mythic) =>
            new(Guid.NewGuid().ToString("N"), slot, tier, family, [new(EquipmentStat.FirePower, -.5f, .5f, value), new(EquipmentStat.Defense, 0, .5f, .01f)], 100);
        test("Comparación: al cambiar una oferta se pierden conjuntos y se aplican límites reales", () =>
        {
            var items = Enum.GetValues<EquipmentSlot>().Where(s => !RingEffects.IsRing(s)).Select(s => Item(s, EquipmentFamily.Fire, .3f)).ToArray();
            var p = new AccountProfile("a", "A", 0, 0, 0, new(items[0].Id, items[1].Id, items[2].Id, items[3].Id), items);
            var offer = Item(EquipmentSlot.Weapon, EquipmentFamily.Cold, -.1f, ItemTier.Silver);
            var preview = EquipmentQuery.Preview(p, offer);
            Check(preview.Before.Modifiers.Pieces(EquipmentFamily.Fire) == 4 && preview.After.Modifiers.Pieces(EquipmentFamily.Fire) == 3,
                "La oferta no contempla la pérdida del conjunto de cuatro.");
            Check(preview.After.Stats.Single(s => s.Stat == EquipmentStat.FirePower).Limited && preview.Describe().Contains("límite aplicado") &&
                preview.Describe().Contains("4/4 → 3/4") && p.Items.Length == 4 && p.Loadout.Weapon == items[3].Id,
                "Se pierde el límite, el conjunto o se modifica el perfil original.");
            var full = p with { Items = Enumerable.Range(0, 496).Select(_ => Item(EquipmentSlot.Weapon, EquipmentFamily.Mixed, 0)).Concat(items).ToArray() };
            Check(EquipmentQuery.Preview(full, offer).After.Modifiers == preview.After.Modifiers, "Un inventario lleno no permite comparar ofertas.");
        });
        test("Inventario: ordenar por atributo incluye penalizaciones y coloca las piezas sin atributo al final", () =>
        {
            var a = Item(EquipmentSlot.Weapon, EquipmentFamily.Fire, -.06f);
            var b = Item(EquipmentSlot.Weapon, EquipmentFamily.Fire, .04f, ItemTier.Silver);
            var c = a with { Id = Guid.NewGuid().ToString("N"), Bonuses = [new(EquipmentStat.Defense, 0, .2f, .1f)] };
            Check(EquipmentQuery.Sort([a, c, b], EquipmentStat.FirePower).Select(i => i.Id).SequenceEqual(new[] { b.Id, a.Id, c.Id }), "El orden depende de la calidad o mezcla ausencias y penalizaciones.");
        });
        test("Favoritos y equipos: se persisten, excluyen ventas duplicadas y validan todas las posiciones", () =>
        {
            var a = Item(EquipmentSlot.Boots, EquipmentFamily.Fire, .1f); var b = Item(EquipmentSlot.Hat, EquipmentFamily.Fire, .1f);
            var p = new AccountProfile("a", "A", 0, 0, 0, new(a.Id, b.Id), [a, b]);
            var prefs = new InventoryPreferences(); prefs.Favorites.Add(a.Id); prefs.Save("Fuego", p.Loadout, p);
            prefs = JsonSerializer.Deserialize<InventoryPreferences>(JsonSerializer.Serialize(prefs))!;
            Check(prefs.Protected(a.Id) && prefs.Sellable(p, [a.Id, b.Id, b.Id, "ajeno"]).Length == 0, "Se vende una pieza equipada.");
            var unequipped = p with { Loadout = p.Loadout.With(EquipmentSlot.Hat, "") };
            Check(prefs.Sellable(unequipped, [a.Id, b.Id, b.Id, "ajeno"]).Single().Id == b.Id, "Se vende un favorito, una pieza ajena o dos veces la misma.");
            Check(AccountInventory.ApplyLoadout(p, prefs.Loadouts.Single().Loadout).Loadout == p.Loadout, "No recupera las cuatro posiciones.");
            prefs.Save("fuego", new(a.Id), p); Check(prefs.Loadouts.Count == 1, "El mismo nombre duplica equipos.");
            Check(InventoryPreferences.Missing(new("Viejo", p.Loadout), p with { Items = [a] }) == 1, "No detecta piezas vendidas.");
            try { prefs.Save("Inválido", new(b.Id), p); throw new Exception("Acepta una pieza en otra posición."); } catch (ArgumentException) { }
        });
        test("Controles: los ajustes corruptos se limitan y las posiciones se copian antes de editar", () =>
        {
            var settings = new TouchSettings { Size = float.NaN, Spacing = 99, DeadZone = -1, Sensitivity = float.PositiveInfinity,
                Positions = new() { ["TouchSelf"] = new(-.1f, 2), ["TouchNova"] = new(float.NaN, 0) } }.Normalize();
            Check(settings.Size == 1 && settings.Spacing == 1.25f && settings.DeadZone == .05f && settings.Sensitivity == 1 && settings.Positions.Count == 1 &&
                settings.Positions["TouchSelf"] == new TouchPosition(0, 1), "Un ajuste dañado genera tamaños o coordenadas inválidas.");
            var copy = settings.Normalize(); copy.Positions["TouchSelf"] = new(.5f, .5f);
            Check(settings.Positions["TouchSelf"] == new TouchPosition(0, 1), "Cancelar la edición altera las posiciones anteriores.");
        });
    }
}
