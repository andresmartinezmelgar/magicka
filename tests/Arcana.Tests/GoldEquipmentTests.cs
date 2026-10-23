using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class GoldEquipmentTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static object? Call(GameWorld world, string method, params object[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static EquipmentItem Item(EquipmentSlot slot, ItemAffixKind kind, int value = 1) =>
        EquipmentCatalogue.Generate(new(5 + (int)slot), tier: ItemTier.Mythic, slot: slot) with { Affix = new(kind, value) };
    private static AccountProfile Profile(params EquipmentItem[] items)
    {
        var loadout = new EquipmentLoadout();
        foreach (var i in items) loadout = loadout.With(i.Slot, i.Id);
        return new("gold-test", "Mago", 0, 0, 0, loadout, items);
    }
    private static (GameWorld World, Wizard Player) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Seed = 771, MaxPlayers = 4 });
        var player = world.AddPlayer("Mago")!;
        Call(world, "GrantDefenseItem", player, Item(EquipmentSlot.Boots, ItemAffixKind.CoinDividend));
        Call(world, "GrantDefenseItem", player, Item(EquipmentSlot.Hat, ItemAffixKind.GoldBonus, 10));
        return (world, player);
    }
    private static int Gold(GameWorld world, Wizard player) => world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id).Gold;
    private static void Pickup(GameWorld world, Wizard player, int gold)
    {
        Call(world, "AddDefenseDrop", player.Position, 0, DefenseDropKind.Gold);
        var drops = (IList)typeof(GameWorld).GetField("_defenseDrops", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        var drop = drops[drops.Count - 1]!;
        drop.GetType().GetField("Gold")!.SetValue(drop, gold);
        Call(world, "TickDefenseDrops", 0.01f);
    }

    public static void Run(Action<string, Action> test)
    {
        test("Inventario: la pieza equipada precede a una mítica incluso al ordenar por atributo", () =>
        {
            var low = EquipmentCatalogue.Generate(new(5), tier: ItemTier.Silver, slot: EquipmentSlot.Weapon);
            var high = Item(EquipmentSlot.Weapon, ItemAffixKind.GoldBonus, 20);
            foreach (EquipmentStat? stat in new EquipmentStat?[] { null, EquipmentStat.Power, EquipmentStat.ColdPower })
                Check(EquipmentQuery.Sort([high, low], stat, low.Id).First().Id == low.Id, "La calidad desplazó a la equipada.");
            var profile = Profile(low) with { Items = [low, high] };
            var prefs = new InventoryPreferences();
            Check(prefs.Sellable(profile, [low.Id, high.Id]).Single().Id == high.Id, "La selección manual vendió una equipada.");
            var draft = profile with { Loadout = new() };
            Check(prefs.Sellable(draft, [low.Id, high.Id], profile.Loadout).Single().Id == high.Id,
                "La selección pendiente permite vender antes de guardar el equipo.");
            try { AccountInventory.Sell(profile, low.Id); throw new Exception("La venta autoritativa retiró una equipada."); }
            catch (ArgumentException) { }
        });
        test("Oro: se generan ambos afijos en calidades altas y se describen sus dos modos", () =>
        {
            var kinds = new HashSet<ItemAffixKind>();
            for (int seed = 0; seed < 1000; seed++)
            {
                var item = EquipmentCatalogue.Generate(new(seed), tier: seed % 2 == 0 ? ItemTier.Diamond : ItemTier.Mythic);
                Check(EquipmentCatalogue.Valid(item), "El catálogo generó un afijo inválido.");
                kinds.Add(item.Affix!.Kind);
            }
            foreach (var kind in new[] { ItemAffixKind.CoinDividend, ItemAffixKind.GoldBonus })
            {
                Check(kinds.Contains(kind), "El afijo no aparece en el botín.");
                var affix = new ItemAffix(kind, kind == ItemAffixKind.GoldBonus ? 10 : 1);
                string text = ItemAffixes.Describe(affix);
                Check(text.Contains("PvP") && text.Contains("Supervivencia"), "No se explica cuándo actúa el bono.");
                Check(!ItemAffixes.Valid(affix, ItemTier.Gold), "Un afijo apareció en una calidad baja.");
            }
            Check(!ItemAffixes.Valid(new(ItemAffixKind.GoldBonus, 100), ItemTier.Mythic), "Se aceptó un porcentaje sin límite.");
        });
        test("Oro: solo actúa el equipo puesto y los efectos iguales usan el más alto", () =>
        {
            var items = new[] { Item(EquipmentSlot.Boots, ItemAffixKind.CoinDividend), Item(EquipmentSlot.Hat, ItemAffixKind.GoldBonus, 10),
                Item(EquipmentSlot.Chest, ItemAffixKind.GoldBonus, 20), Item(EquipmentSlot.Weapon, ItemAffixKind.GoldBonus, 20) };
            var profile = Profile(items);
            var gear = EquipmentModifiers.Calculate(profile);
            Check(gear.ExtraGoldPerTwo == 1 && gear.GoldBonusPercent == 20, "Se sumaron porcentajes duplicados.");
            var empty = EquipmentModifiers.Calculate(profile with { Loadout = new() });
            Check(empty.ExtraGoldPerTwo == 0 && empty.GoldBonusPercent == 0, "Una pieza de la mochila concede oro.");
            var publicGear = PublicEquipment.Prepare(profile).View;
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, publicGear));
            Check(Protocol.Deserialize<EquipmentItem>(Protocol.Serialize(items[0]))!.Affix == items[0].Affix, "Se perdió el afijo al guardar.");
        });
        test("Oro: conserva monedas y decimales entre recogidas y suma bonos sobre la base", () =>
        {
            int coin = 0, percent = 0, total = 0;
            for (int i = 0; i < 10; i++) total += GoldBonuses.Apply(1, 1, 10, ref coin, ref percent);
            Check(total == 16 && coin == 0 && percent == 0, "Se pierden restos de recogidas pequeñas.");
            Check(GoldBonuses.Apply(100, 1, 20, ref coin, ref percent) == 170, "Los bonos se multiplicaron entre sí.");
        });
        test("Supervivencia: cada aliado aplica su propio bono al oro compartido", () =>
        {
            var (world, player) = World(); var ally = world.AddPlayer("Aliado")!; ally.Position = player.Position;
            Pickup(world, player, 5); Check(Gold(world, player) == 7 && Gold(world, ally) == 5, "Un aliado recibió un bono ajeno.");
            Pickup(world, player, 5); Check(Gold(world, player) == 16 && Gold(world, ally) == 10, "Se perdió oro o se aplicó dos veces al grupo.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Supervivencia: guardar y continuar conserva el oro y los restos de los dos bonos", () =>
        {
            var (world, player) = World(); Pickup(world, player, 5);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var continued = restored.Players[player.Id];
            Pickup(restored, continued, 5);
            Check(Gold(restored, continued) == 16 && continued.Equipment.ExtraGoldPerTwo == 1 && continued.Equipment.GoldBonusPercent == 10,
                "La continuación reinició los bonos o sus restos.");
        });
        test("PvP: la recompensa final aplica los bonos y respeta informes antiguos y Defensa", () =>
        {
            var winner = new MatchParticipant("a", 1, 0, 30, true) { ExtraGoldPerTwo = 1, GoldBonusPercent = 10 };
            var loser = new MatchParticipant("b", 2, 1, 30, false) { ExtraGoldPerTwo = 1, GoldBonusPercent = 10 };
            var match = new RewardMatch(Guid.NewGuid().ToString("N"), "server", GameMode.Duel, ArenaMapId.Classic,
                DateTimeOffset.UtcNow, [winner, loser], RulesVersion: 3);
            Check(RewardRules.Eligible(match).Length == 2 && RewardRules.Gold(match, winner) == 80 && RewardRules.Gold(match, loser) == 39,
                "El premio final no usa los bonos del equipo de partida.");
            Check(RewardRules.Gold(match with { RulesVersion = 1 }, winner, 10) == 10 &&
                RewardRules.Gold(match with { Mode = GameMode.Defense, RulesVersion = 2 }, winner, 10) == 10,
                "Se modificó un premio antiguo o se duplicó el bono fuera de Supervivencia.");
            Check(RewardRules.Eligible(match with { Participants = [winner with { GoldBonusPercent = 500 }, loser] }).Length == 0,
                "Se aceptó un bono ajeno al equipo válido.");
        });
    }
}
