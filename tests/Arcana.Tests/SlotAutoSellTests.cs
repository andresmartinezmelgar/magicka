using Arcana.Core;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SlotAutoSellTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static EquipmentItem Hat(ItemTier tier, EquipmentFamily family) => EquipmentCatalogue.Generate(new(701),
        slot: EquipmentSlot.Hat, tier: tier, family: family) with { Id = Guid.NewGuid().ToString("N") };
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => typeof(GameWorld)
        .GetMethod("GrantDefenseItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, [player, item]);
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) => world.Snapshot().Defense!.Defenders.Single(d => d.PlayerId == player.Id);

    internal static void Run(Action<string, Action> test)
    {
        test("Venta por ranura: un gorro mítico eléctrico filtra todos los elementos y todas las calidades inferiores", () =>
        {
            var reference = Hat(ItemTier.Mythic, EquipmentFamily.Lightning);
            var old = Hat(ItemTier.Gold, EquipmentFamily.Fire);
            var profile = new AccountProfile("owner", "Mago", 0, 0, 0, new(Hat: reference.Id), [reference, old]);
            var rules = new InventoryPreferences(); rules.SetAutoSell(EquipmentStyle.Of(reference), true, profile);
            var fresh = Enum.GetValues<ItemTier>().SelectMany(tier => Enum.GetValues<EquipmentFamily>().Select(f => Hat(tier, f))).ToArray();
            profile = profile with { Items = [reference, old, .. fresh] };
            var sold = rules.AutomaticSales(profile);
            Check(sold.Length == (Enum.GetValues<ItemTier>().Length - 1) * Enum.GetValues<EquipmentFamily>().Length &&
                sold.All(i => i.Tier < ItemTier.Mythic && i.Id != old.Id), "El elemento impide vender un gorro inferior o se vende uno anterior.");
            Check(rules.AutoSells(new(EquipmentSlot.Hat, ItemTier.Mythic, EquipmentFamily.Fire)), "Otra familia no reconoce el mismo límite.");
            rules.SetAutoSell(new(EquipmentSlot.Hat, ItemTier.Silver, EquipmentFamily.Water), false, profile);
            Check(rules.AutomaticSales(profile).Length == 0, "Desactivar desde otro elemento no conserva los gorros.");
        });
        test("Venta por ranura: la migración conserva activación y objetos actuales de otros elementos", () =>
        {
            var reference = Hat(ItemTier.Mythic, EquipmentFamily.Lightning); var old = Hat(ItemTier.Gold, EquipmentFamily.Fire);
            var rules = new InventoryPreferences { AutoSellRuleVersion = 1, AutoSellRules = [
                new(EquipmentStyle.Of(reference), []), new(new(EquipmentSlot.Hat, ItemTier.Gold, EquipmentFamily.Fire), [])] };
            rules.Normalize(); Check(rules.AutoSellRules.Count == 2 && rules.AutoSellRuleVersion == 1, "Migra sin conocer el inventario actual.");
            var profile = new AccountProfile("owner", "Mago", 0, 0, 0, new(Hat: reference.Id), [reference, old]);
            Check(rules.AutomaticSales(profile).Length == 0 && rules.AutoSellRules.Count == 1 &&
                rules.AutoSellRuleVersion == InventoryPreferences.CurrentAutoSellRuleVersion, "La migración borra el filtro o vende piezas actuales.");
            var favorite = Hat(ItemTier.Silver, EquipmentFamily.Water); var equipped = Hat(ItemTier.Gold, EquipmentFamily.Earth);
            rules.Favorites.Add(favorite.Id); var fresh = Hat(ItemTier.Gold, EquipmentFamily.Life);
            profile = profile with { Items = [reference, old, favorite, equipped, fresh], Loadout = new(Hat: equipped.Id) };
            Check(rules.AutomaticSales(profile).Single().Id == fresh.Id, "La regla migrada ignora otro elemento o pierde las protecciones.");
            rules = JsonSerializer.Deserialize<InventoryPreferences>(JsonSerializer.Serialize(rules))!; rules.Normalize();
            Check(rules.AutomaticSales(profile).Single().Id == fresh.Id, "Recargar las preferencias pierde el límite o las exclusiones.");
        });
        test("Supervivencia: el límite mítico vende el botín de otras familias también después de continuar", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 1 });
            var human = world.AddPlayer("Mago")!; var reference = Hat(ItemTier.Mythic, EquipmentFamily.Lightning);
            Give(world, human, reference);
            Check(world.ApplyMessage(human.Id, new() { Type = "defense_auto_sell", AutoSellStyle = EquipmentStyle.Of(reference),
                ItemId = reference.Id, Slot = 1, Sequence = human.LastSequence + 1 }), "No activa el filtro del gorro.");
            foreach (var family in Enum.GetValues<EquipmentFamily>()) Give(world, human, Hat(ItemTier.Gold, family));
            Check(State(world, human).Items.Length == 1 && State(world, human).AutoSold == Enum.GetValues<EquipmentFamily>().Length,
                "Se guardan gorros inferiores de otros elementos.");
            var save = world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N"));
            var state = JsonNode.Parse(save.State.GetRawText())!; var progress = state["Defenders"]![0]!["Progress"]!;
            progress["AutoSellRuleVersion"] = 1;
            var restored = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(state) });
            var player = restored.Players[human.Id]; var before = State(restored, player); var next = Hat(ItemTier.Silver, EquipmentFamily.Fire);
            Give(restored, player, next); var after = State(restored, player);
            Check(after.Items.Length == before.Items.Length && after.AutoSold == before.AutoSold + 1 &&
                after.Gold == before.Gold + next.SaleGold, "Continuar desactiva la regla antigua o calcula mal la venta.");
            Give(restored, player, Hat(ItemTier.Mythic, EquipmentFamily.Cold));
            Check(State(restored, player).Items.Length == after.Items.Length + 1, "Se vende un gorro de la misma calidad.");
            SnapshotValidation.Check(restored.Snapshot());
        });
    }
}
