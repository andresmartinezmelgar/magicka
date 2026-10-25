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
    private static EquipmentItem Legacy(EquipmentSlot slot, ItemAffixKind kind, int value = 1) =>
        Item(slot, kind, value) with { GenerationVersion = 5 };
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
        Call(world, "GrantDefenseItem", player, Item(EquipmentSlot.GoldRing, ItemAffixKind.GoldBonus, 10));
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
        test("Oro: el anillo progresa de una moneda extra cada once a una por moneda", () =>
        {
            var item = Item(EquipmentSlot.GoldRing, ItemAffixKind.CoinDividend);
            var profile = Profile(item);
            int[] expected = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1];
            for (int level = 0; level <= 10; level++)
            {
                var levels = new Dictionary<string, int> { [item.Id] = level };
                var gear = EquipmentModifiers.Calculate(profile, levels);
                Check(gear.ExtraGoldEvery == expected[level], "El bono usa el nivel del mago o una progresión incorrecta.");
                int coin = 0, percent = 0;
                Check(GoldBonuses.Apply(expected[level], gear.ExtraGoldEvery, 0, ref coin, ref percent) == expected[level] + 1 && coin == 0,
                    "No se entrega exactamente un oro extra al completar el intervalo.");
                var prepared = PublicEquipment.Prepare(profile, levels);
                var copy = Protocol.Deserialize<EquipmentInspection>(Protocol.Serialize(new EquipmentInspection(1, "Mago", WizardType.Balanced, false, prepared.View)))!;
                PublicEquipment.Check(copy);
                Check(copy.Equipment.Items.Single().UpgradeLevel == level && ItemAffixes.Summary(gear).Contains($"nivel {level}/10"),
                    "La ficha o la descripción no muestran el nivel real de la pieza.");
            }
            var second = Legacy(EquipmentSlot.Weapon, ItemAffixKind.CoinDividend);
            Check(EquipmentModifiers.Calculate(Profile(item, second), new Dictionary<string, int> { [item.Id] = 5, [second.Id] = 10 }).ExtraGoldEvery == 6,
                "Un afijo antiguo de arma altera el intervalo del anillo.");
        });
        test("Oro: mejorar o quitar una pieza conserva fracciones sin pagar bonos retroactivos", () =>
        {
            int coin = 0, percent = 0;
            Check(GoldBonuses.Apply(10, 11, 0, ref coin, ref percent) == 10, "El nivel cero paga antes de once monedas.");
            int fraction = coin;
            Check(GoldBonuses.Apply(1, 1, 0, ref coin, ref percent) == 2 && coin == fraction,
                "El nivel diez premia retroactivamente las monedas anteriores o pierde su fracción.");
            Check(GoldBonuses.Apply(1, 0, 0, ref coin, ref percent) == 1 && coin == fraction, "Una pieza quitada sigue concediendo oro.");
            Check(GoldBonuses.Apply(1, 11, 0, ref coin, ref percent) == 2 && coin == 0, "Se perdió el progreso al cambiar de pieza.");
        });
        test("Supervivencia: el anillo llega a nivel diez aunque los atributos estén al máximo", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Seed = 77, MaxPlayers = 4 });
            var player = world.AddPlayer("Mago")!;
            var item = Item(EquipmentSlot.GoldRing, ItemAffixKind.CoinDividend);
            item = item with { Bonuses = item.Bonuses.Select(b => b with { Current = b.Maximum }).ToArray() };
            Call(world, "GrantDefenseItem", player, item);
            var progress = ((IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!)[player.Id]!;
            progress.GetType().GetField("Gold")!.SetValue(progress, 1000000);
            Check(world.ApplyMessage(player.Id, new() { Type = "defense_shop", Slot = 1, Sequence = player.LastSequence + 1 }), "No abre la tienda.");
            for (int level = 1; level <= 10; level++)
            {
                Check(world.ApplyMessage(player.Id, new() { Type = "defense_upgrade", ItemId = item.Id, Sequence = player.LastSequence + 1 }),
                    "Los atributos máximos bloquean la mejora del afijo.");
                var entry = world.Snapshot().Defense!.Defenders.Single().Items.Single();
                Check(entry.Upgrades == level && player.Equipment.ExtraGoldEvery == 11 - level &&
                    player.PublicEquipment!.Items.Single().UpgradeLevel == level, "La mejora no se refleja en combate y en la ficha.");
            }
            long before = Gold(world, player); Pickup(world, player, 1);
            Check(Gold(world, player) == before + 2, "El nivel diez no entrega un extra por moneda.");
            Check(!world.ApplyMessage(player.Id, new() { Type = "defense_upgrade", ItemId = item.Id, Sequence = player.LastSequence + 1 }), "Acepta nivel once.");
            var saved = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(saved); var continued = restored.Players[player.Id];
            before = Gold(restored, continued); Pickup(restored, continued, 1);
            Check(Gold(restored, continued) == before + 2 && continued.Equipment.ExtraGoldEvery == 1, "Continuar pierde el nivel del afijo.");
            SnapshotValidation.Check(restored.Snapshot());
        });
        test("Supervivencia: los restos del afijo anterior siguen siendo válidos al continuar", () =>
        {
            var (world, player) = World();
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(save.State.GetRawText())!;
            var progress = node["Defenders"]![0]!["Progress"]!.AsObject();
            progress.Remove("GoldCoinRemainderScale");
            progress["Gold"] = 7; progress["GoldCoinRemainder"] = 1; progress["GoldPercentRemainder"] = 50;
            var legacy = save with { State = System.Text.Json.JsonSerializer.SerializeToElement(node) };
            var restored = GameWorld.RestoreSurvival(legacy); var continued = restored.Players[player.Id];
            Pickup(restored, continued, 6);
            Check(Gold(restored, continued) == 15 && continued.Equipment.ExtraGoldEvery == 11, "La migración pierde o multiplica el progreso anterior.");
            var next = GameWorld.RestoreSurvival(restored.SaveSurvival(continued.Id, Guid.NewGuid().ToString("N")));
            Check(Gold(next, next.Players[continued.Id]) == 15, "Volver a guardar altera el oro ya confirmado.");
        });
        test("PvP: usa el intervalo nuevo en el premio final y valida sus límites", () =>
        {
            var winner = new MatchParticipant("a", 1, 0, 30, true) { ExtraGoldEvery = 11, GoldBonusPercent = 10 };
            var loser = new MatchParticipant("b", 2, 1, 30, false) { ExtraGoldEvery = 11, GoldBonusPercent = 10 };
            var match = new RewardMatch(Guid.NewGuid().ToString("N"), "server", GameMode.Duel, ArenaMapId.Classic,
                DateTimeOffset.UtcNow, [winner, loser], RulesVersion: 4);
            Check(RewardRules.Eligible(match).Length == 2 && RewardRules.Gold(match, winner) == 59 && RewardRules.Gold(match, loser) == 29,
                "El premio final usa el bono antiguo.");
            Check(RewardRules.Gold(match, winner with { ExtraGoldEvery = 1 }) == 105 && RewardRules.Gold(match, winner, 100) == 178,
                "No respeta el intervalo o la conversión del premio.");
            Check(RewardRules.Eligible(match with { Participants = [winner with { ExtraGoldEvery = 12 }, loser] }).Length == 0, "Acepta un intervalo inválido.");
        });
        test("Inventario: la pieza equipada precede a una mítica incluso al ordenar por atributo", () =>
        {
            var low = EquipmentCatalogue.Generate(new(5), tier: ItemTier.Silver, slot: EquipmentSlot.Weapon);
            var high = Item(EquipmentSlot.Weapon, ItemAffixKind.BeamPierce, 3);
            foreach (EquipmentStat? stat in new EquipmentStat?[] { null, EquipmentStat.Power, EquipmentStat.ColdPower })
                Check(EquipmentQuery.Sort([high, low], stat, low.Id).First().Id == low.Id, "La calidad desplazó a la equipada.");
            var profile = Profile(low) with { Items = [low, high] };
            var prefs = new InventoryPreferences();
            Check(prefs.Sellable(profile, [low.Id, high.Id]).Single().Id == high.Id, "La selección manual vendió una equipada.");
            var draft = profile with { Loadout = new() };
            Check(prefs.Sellable(draft, [low.Id, high.Id]).Length == 2,
                "Una pieza que ya no está seleccionada sigue bloqueada por el equipo guardado.");
            try { AccountInventory.Sell(profile, low.Id); throw new Exception("La venta autoritativa retiró una equipada."); }
            catch (ArgumentException) { }
        });
        test("Oro: los dos afijos solo se generan en el anillo de oro y las calidades previstas", () =>
        {
            var kinds = new HashSet<ItemAffixKind>();
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            foreach (var tier in Enum.GetValues<ItemTier>())
            for (int seed = 0; seed < 160; seed++)
            {
                var item = EquipmentCatalogue.Generate(new(seed), tier: tier, slot: slot);
                Check(EquipmentCatalogue.Valid(item), "El catálogo generó un afijo inválido.");
                if (item.Affix == null) continue;
                kinds.Add(item.Affix.Kind);
                if (item.Affix.Kind is ItemAffixKind.CoinDividend or ItemAffixKind.GoldBonus)
                    Check(slot == EquipmentSlot.GoldRing && tier >= ItemTier.Diamond,
                        "Una pieza distinta del anillo de oro recibió un bono de monedas.");
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
        test("Oro: solo actúa el anillo equipado y las piezas antiguas no aumentan sus bonos", () =>
        {
            var items = new[] { Legacy(EquipmentSlot.Boots, ItemAffixKind.CoinDividend), Legacy(EquipmentSlot.Hat, ItemAffixKind.GoldBonus, 20),
                Legacy(EquipmentSlot.Chest, ItemAffixKind.GoldBonus, 20), Legacy(EquipmentSlot.Weapon, ItemAffixKind.GoldBonus, 20),
                Legacy(EquipmentSlot.MagicRing, ItemAffixKind.GoldBonus, 20), Item(EquipmentSlot.GoldRing, ItemAffixKind.GoldBonus, 10) };
            var profile = Profile(items);
            var gear = EquipmentModifiers.Calculate(profile);
            Check(gear.ExtraGoldEvery == 11 && gear.GoldBonusPercent == 10 && gear.MagicRingFamily != null,
                "Una pieza antigua aportó oro o se desactivó el efecto propio del anillo mágico.");
            var withoutRing = EquipmentModifiers.Calculate(profile with { Loadout = profile.Loadout.With(EquipmentSlot.GoldRing, "") });
            Check(withoutRing.ExtraGoldEvery == 0 && withoutRing.GoldBonusPercent == 0, "Quitar el anillo dejó oro activo en otras piezas.");
            var empty = EquipmentModifiers.Calculate(profile with { Loadout = new() });
            Check(empty.ExtraGoldEvery == 0 && empty.GoldBonusPercent == 0, "Una pieza de la mochila concede oro.");
            var publicGear = PublicEquipment.Prepare(profile).View;
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, publicGear));
            Check(publicGear.Items.Where(i => i.Slot != EquipmentSlot.GoldRing).All(i => i.Affix == null),
                "La ficha pública presenta afijos de oro inactivos como efectos equipados.");
            Check(Protocol.Deserialize<EquipmentItem>(Protocol.Serialize(items[0]))!.Affix == items[0].Affix, "Se perdió el afijo al guardar.");
        });
        test("Oro: las piezas antiguas conservan identidad, atributos y nivel, pero no compran mejoras de oro", () =>
        {
            foreach (var slot in Enum.GetValues<EquipmentSlot>().Where(s => s != EquipmentSlot.GoldRing))
            foreach (var kind in new[] { ItemAffixKind.CoinDividend, ItemAffixKind.GoldBonus })
            {
                var item = Legacy(slot, kind, kind == ItemAffixKind.GoldBonus ? 20 : 1) with { UpgradeLevel = 5 };
                item = item with { Bonuses = item.Bonuses.Select(b => b with { Current = b.Maximum }).ToArray() };
                Check(EquipmentCatalogue.Valid(item) && !EquipmentCatalogue.Valid(item with { GenerationVersion = 6 }),
                    "Se rechazó una pieza antigua o se admitió una tirada nueva fuera del anillo.");
                var profile = Profile(item);
                var saved = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(profile))!;
                var copy = saved.Items.Single();
                Check(copy.Id == item.Id && copy.Affix == item.Affix && copy.UpgradeLevel == 5 && copy.Bonuses.SequenceEqual(item.Bonuses),
                    "La corrección volvió a sortear o borró la pieza antigua.");
                var equipped = AccountInventory.ApplyLoadout(saved, saved.Loadout);
                var gear = EquipmentModifiers.Calculate(equipped);
                Check(gear.ExtraGoldEvery == 0 && gear.GoldBonusPercent == 0 &&
                    ItemAffixes.Describe(copy.Affix, copy.UpgradeLevel, copy.Slot).Contains("inactivo"),
                    "Una pieza antigua sigue aplicando o anunciando el oro como activo.");
                Check(ItemAffixes.CanUpgrade(copy, 5) == RingEffects.IsRing(slot),
                    "Un afijo de oro antiguo permite cobrar mejoras sin estadísticas que mejorar.");
                Check(AccountInventory.Sell(saved with { Loadout = new() }, copy.Id).Items.Length == 0,
                    "La pieza antigua no se puede vender al desequiparla.");
            }
        });
        test("Supervivencia: el oro de piezas antiguas queda inactivo también al guardar y continuar", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Seed = 771, MaxPlayers = 4 });
            var player = world.AddPlayer("Mago")!;
            foreach (var slot in Enum.GetValues<EquipmentSlot>().Where(s => s != EquipmentSlot.GoldRing))
                Call(world, "GrantDefenseItem", player, Legacy(slot, slot == EquipmentSlot.Boots ? ItemAffixKind.CoinDividend : ItemAffixKind.GoldBonus,
                    slot == EquipmentSlot.Boots ? 1 : 20));
            int before = Gold(world, player); Pickup(world, player, 11);
            Check(Gold(world, player) == before + 11 && player.Equipment.ExtraGoldEvery == 0 && player.Equipment.GoldBonusPercent == 0,
                "Las recogidas de Supervivencia aplican el oro antiguo sin anillo.");
            var saved = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(saved); var continued = restored.Players[player.Id];
            before = Gold(restored, continued); Pickup(restored, continued, 11);
            Check(Gold(restored, continued) == before + 11 && continued.Equipment.ExtraGoldEvery == 0 && continued.Equipment.GoldBonusPercent == 0 &&
                restored.Snapshot().Defense!.Defenders.Single().Items.All(i => i.Item.GenerationVersion == 5 && i.Item.Affix != null),
                "Continuar reactivó el oro antiguo o perdió las piezas guardadas.");
            SnapshotValidation.Check(restored.Snapshot());
        });
        test("Oro: conserva monedas y decimales entre recogidas y suma bonos sobre la base", () =>
        {
            int coin = 0, percent = 0, total = 0;
            for (int i = 0; i < 11; i++) total += GoldBonuses.Apply(1, 11, 10, ref coin, ref percent);
            Check(total == 13 && coin == 0 && percent == 10, "Se pierden restos de recogidas pequeñas.");
            coin = percent = 0;
            Check(GoldBonuses.Apply(100, 11, 20, ref coin, ref percent) == 129, "Los bonos se multiplicaron entre sí.");
        });
        test("Supervivencia: cada aliado aplica su propio bono al oro compartido", () =>
        {
            var (world, player) = World(); var ally = world.AddPlayer("Aliado")!; ally.Position = player.Position;
            Pickup(world, player, 5); Check(Gold(world, player) == 5 && Gold(world, ally) == 5, "Un aliado recibió un bono ajeno.");
            Pickup(world, player, 5); Check(Gold(world, player) == 11 && Gold(world, ally) == 10, "Se perdió oro o se aplicó dos veces al grupo.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Supervivencia: guardar y continuar conserva el oro y los restos de los dos bonos", () =>
        {
            var (world, player) = World(); Pickup(world, player, 5);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var continued = restored.Players[player.Id];
            Pickup(restored, continued, 5);
            Check(Gold(restored, continued) == 11 && continued.Equipment.ExtraGoldEvery == 11 && continued.Equipment.GoldBonusPercent == 10,
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
