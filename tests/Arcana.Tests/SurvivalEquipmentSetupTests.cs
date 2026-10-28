using Arcana.Core;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SurvivalEquipmentSetupTests
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static EquipmentItem Piece(EquipmentSlot slot, EquipmentStat stat, float value, EquipmentFamily family = EquipmentFamily.Mixed,
        ItemTier tier = ItemTier.Mythic, ItemAffix? affix = null) => EquipmentCatalogue.Generate(new(932), slot: slot, family: family, tier: tier)
        with { Id = Guid.NewGuid().ToString("N"), Bonuses = [new(stat, -.25f, .5f, value)], Affix = affix };
    private static GameWorld World() => new(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
        SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 2 });
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => typeof(GameWorld)
        .GetMethod("GrantDefenseItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, [player, item]);
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) => world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id);
    private static bool Send(GameWorld world, Wizard player, ClientMessage message)
    { message.Sequence = player.LastSequence + 1; return world.ApplyMessage(player.Id, message); }
    private static bool Setup(GameWorld world, Wizard player, SurvivalEquipmentSetup setup) => Send(world, player,
        new() { Type = "defense_equipment_setup", SurvivalEquipmentSetup = setup });
    private static SurvivalEquipmentSetup Enabled(Element element, params ItemAffixKind[] priorities) => new()
        { Enabled = true, Element = element, Priorities = priorities };

    internal static void Run(Action<string, Action> test)
    {
        test("Equipo predeterminado: maximiza los ocho elementos por sus cifras y no solo por calidad o familia", () =>
        {
            EquipmentStat[] stats = [EquipmentStat.WaterPower, EquipmentStat.HealingPower, EquipmentStat.ShieldCapacity, EquipmentStat.ColdPower,
                EquipmentStat.LightningPower, EquipmentStat.ArcanePower, EquipmentStat.EarthPower, EquipmentStat.FirePower];
            foreach (var element in Enum.GetValues<Element>())
            {
                var weak = Piece(EquipmentSlot.Hat, stats[(int)element], .01f);
                var strong = Piece(EquipmentSlot.Hat, stats[(int)element], .3f, tier: ItemTier.Silver);
                var selected = SurvivalEquipmentSelection.Select([weak, strong], Enabled(element), new(Hat: weak.Id));
                Check(selected.Hat == strong.Id, "No potencia " + element);
                Check(selected == SurvivalEquipmentSelection.Select([strong, weak], Enabled(element), new()), "Depende del orden del inventario.");
            }
            var elemental = Piece(EquipmentSlot.Weapon, EquipmentStat.FirePower, .2f);
            var general = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .3f);
            Check(SurvivalEquipmentSelection.Select([elemental, general], Enabled(Element.Fire), new()).Weapon == general.Id,
                "Ignora la potencia general que aumenta más el daño elemental.");
        });
        test("Equipo predeterminado: compara el conjunto completo y el anillo del elemento sin sumar atributos inactivos", () =>
        {
            var items = Enum.GetValues<EquipmentSlot>().Where(s => !RingEffects.IsRing(s)).SelectMany(slot => new[] {
                Piece(slot, EquipmentStat.HealingPower, .01f, EquipmentFamily.Life),
                Piece(slot, EquipmentStat.HealingPower, .04f, EquipmentFamily.Fire) }).ToArray();
            var ring = Piece(EquipmentSlot.MagicRing, EquipmentStat.ShieldCapacity, .01f, EquipmentFamily.Life);
            var other = Piece(EquipmentSlot.MagicRing, EquipmentStat.ShieldCapacity, .4f, EquipmentFamily.Fire);
            var choice = SurvivalEquipmentSelection.Select([..items, ring, other], Enabled(Element.Life), new());
            Check(items.Where(i => choice.Get(i.Slot) == i.Id).All(i => i.Family == EquipmentFamily.Life), "Pierde el bono de cuatro piezas.");
            Check(choice.MagicRing == ring.Id, "Elige un anillo de otro elemento por su absorción inactiva.");
            var modifiers = EquipmentModifiers.Calculate(new("setup", "Mago", 0, 0, 0, choice, [..items, ring, other]));
            Check(Math.Abs(modifiers.Healing - 1.19f) < .001f, "La comparación no coincide con las reglas reales del conjunto.");
        });
        test("Equipo predeterminado: Marca y Eco coexisten, duplicados no puntúan dos veces y se respeta el orden", () =>
        {
            var markBoots = Piece(EquipmentSlot.Boots, EquipmentStat.ArcanePower, .3f, affix: new(ItemAffixKind.ArcaneMark, 2));
            var echoBoots = Piece(EquipmentSlot.Boots, EquipmentStat.ArcanePower, .01f, affix: new(ItemAffixKind.ArcaneEcho, 2));
            var markHat = Piece(EquipmentSlot.Hat, EquipmentStat.ArcanePower, .01f, affix: new(ItemAffixKind.ArcaneMark, 2));
            var chosen = SurvivalEquipmentSelection.Select([markBoots, echoBoots, markHat],
                Enabled(Element.Arcane, ItemAffixKind.ArcaneMark, ItemAffixKind.ArcaneEcho), new());
            var modifiers = EquipmentModifiers.Calculate(new("setup", "Mago", 0, 0, 0, chosen, [markBoots, echoBoots, markHat]));
            Check(chosen.Boots == echoBoots.Id && modifiers.ArcaneMark == 2 && modifiers.ArcaneEcho == 2, "Repite Marca en lugar de reunir ambos afijos.");
            Check(SurvivalEquipmentSelection.Select([markBoots, echoBoots], Enabled(Element.Arcane, ItemAffixKind.ArcaneEcho, ItemAffixKind.ArcaneMark), new())
                .Boots == echoBoots.Id, "La potencia sustituye la primera prioridad.");
            var diamond = echoBoots with { Id = Guid.NewGuid().ToString("N"), Tier = ItemTier.Diamond,
                Affix = new(ItemAffixKind.ArcaneEcho, 1), Bonuses = [new(EquipmentStat.ArcanePower, 0, .5f, .4f)] };
            Check(SurvivalEquipmentSelection.Select([markBoots, diamond], Enabled(Element.Arcane, ItemAffixKind.ArcaneEcho), new()).Boots == diamond.Id,
                "Prioriza afijos que no son Míticos.");
            foreach (var kind in Enum.GetValues<ItemAffixKind>()) Check(Enabled(Element.Life, kind).Valid, "Falta una prioridad Mítica: " + kind);
            var gold20 = Piece(EquipmentSlot.GoldRing, EquipmentStat.ArcanePower, .01f, affix: new(ItemAffixKind.GoldBonus, 20));
            var gold10 = Piece(EquipmentSlot.GoldRing, EquipmentStat.ArcanePower, .3f, affix: new(ItemAffixKind.GoldBonus, 10));
            var dividend = Piece(EquipmentSlot.GoldRing, EquipmentStat.ArcanePower, .1f, affix: new(ItemAffixKind.CoinDividend));
            Check(SurvivalEquipmentSelection.Select([gold20, gold10, dividend], Enabled(Element.Arcane, ItemAffixKind.GoldBonus), new()).GoldRing == gold20.Id &&
                SurvivalEquipmentSelection.Select([gold20, gold10, dividend], Enabled(Element.Arcane, ItemAffixKind.CoinDividend), new()).GoldRing == dividend.Id,
                "Ignora los afijos económicos o confunde el 10 % y el 20 % de oro.");
        });
        test("Equipo predeterminado: activar vende descartes, protege favoritos y mejora el equipo incluso bajo un filtro de calidad", () =>
        {
            var world = World(); var player = world.AddPlayer("Mago")!;
            var weak = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .01f, tier: ItemTier.Gold);
            var strong = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .2f);
            var favorite = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .02f, tier: ItemTier.Gold);
            var offElement = Piece(EquipmentSlot.Hat, EquipmentStat.FirePower, .1f, EquipmentFamily.Fire);
            foreach (var item in new[] { weak, strong, favorite, offElement }) Give(world, player, item);
            Check(Send(world, player, new() { Type = "defense_favorite", ItemId = favorite.Id, Slot = 1 }), "No protege el favorito.");
            Check(Send(world, player, new() { Type = "defense_auto_sell", ItemId = strong.Id, AutoSellStyle = EquipmentStyle.Of(strong), Slot = 1 }), "No activa filtro anterior.");
            int gold = State(world, player).Gold;
            Check(Setup(world, player, Enabled(Element.Life)), "No aplica configuración.");
            var state = State(world, player);
            Check(state.Items.Length == 3 && state.Items.Any(i => i.Item.Id == offElement.Id) &&
                state.Items.Single(i => i.Equipped).Item.Id == strong.Id && state.Favorites.Contains(favorite.Id) &&
                state.Gold == gold + weak.SaleGold && state.AutoSold == 1, "No vende exactamente el descarte inicial.");
            var next = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .3f, tier: ItemTier.Silver);
            Give(world, player, next); state = State(world, player);
            Check(state.Items.Single(i => i.Equipped).Item.Id == next.Id && state.Items.Length == 4 && state.AutoSold == 1 &&
                state.Gold == gold + weak.SaleGold && state.Items.Any(i => i.Item.Id == strong.Id) && state.Items.Any(i => i.Item.Id == offElement.Id),
                "El filtro vende la mejora, un Mítico sustituido o un Mítico de otro elemento.");
            Check(!Send(world, player, new() { Type = "defense_equip", ItemId = favorite.Id }) &&
                !Send(world, player, new() { Type = "defense_loadout", Loadout = new(Hat: favorite.Id) }), "Permite reemplazar manualmente la selección activa.");
            Check(Setup(world, player, Enabled(Element.Life) with { Enabled = false }), "No desactiva.");
            Check(Send(world, player, new() { Type = "defense_equip", ItemId = favorite.Id }), "Desactivar no restaura el control manual.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Equipo predeterminado: 24 favoritos no desbordan el inventario y cada venta se contabiliza una vez", () =>
        {
            var world = World(); var player = world.AddPlayer("Mago")!;
            for (int i = 0; i < DefenseRules.InventoryLimit; i++)
            {
                var item = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .01f);
                Give(world, player, item); Check(Send(world, player, new() { Type = "defense_favorite", ItemId = item.Id, Slot = 1 }), "No marca favorito.");
            }
            Check(Setup(world, player, Enabled(Element.Life)), "No activa con inventario lleno.");
            var previous = State(world, player); var next = Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .3f, tier: ItemTier.Silver);
            Give(world, player, next); var state = State(world, player);
            Check(state.Items.Length == 24 && state.Items.All(i => i.Item.Id != next.Id) && state.Favorites.Length == 24 &&
                state.Gold == previous.Gold + next.SaleGold && state.AutoSold == previous.AutoSold + 1,
                "Pierde favoritos, desborda o duplica el oro.");
            SnapshotValidation.Check(world.Snapshot()); world.MatchStatistics().Validate();
        });
        test("Supervivencia: un Mítico sin espacio queda en el suelo y se recoge una sola vez al liberar una ranura", () =>
        {
            foreach (bool automatic in new[] { false, true })
            {
                var world = World(); var player = world.AddPlayer("Mago")!;
                for (int i = 0; i < DefenseRules.InventoryLimit; i++)
                    Give(world, player, Piece(EquipmentSlot.Hat, EquipmentStat.HealingPower, .01f));
                if (automatic) Check(Setup(world, player, Enabled(Element.Life)), "No activa el filtro.");
                var item = Piece(EquipmentSlot.Hat, EquipmentStat.FirePower, .3f, EquipmentFamily.Fire);
                var call = typeof(GameWorld).GetMethod("AddDefenseDrop", BindingFlags.Instance | BindingFlags.NonPublic)!;
                call.Invoke(world, [player.Position, player.Id, DefenseDropKind.Equipment]);
                var drops = (IList)typeof(GameWorld).GetField("_defenseDrops", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
                var drop = drops[0]!; drop.GetType().GetField("Item")!.SetValue(drop, item);
                var tick = typeof(GameWorld).GetMethod("TickDefenseDrops", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var before = State(world, player);
                for (int i = 0; i < 3; i++) tick.Invoke(world, [.1f]);
                var full = State(world, player);
                Check(drops.Count == 1 && full.Items.Length == 24 && full.Gold == before.Gold && full.AutoSold == before.AutoSold,
                    "Convierte un Mítico en oro o lo retira del suelo con inventario lleno.");
                Check(world.MatchStatistics().Sum(StatisticGroup.Equipment, StatisticMeasure.Collected, (int)ItemTier.Mythic) == 24,
                    "Los intentos de recogida repetidos inflan las estadísticas.");
                var manual = full.Items.First(i => !i.Equipped).Item;
                Check(Send(world, player, new() { Type = "defense_sell", ItemId = manual.Id }), "No permite la venta manual de un Mítico.");
                tick.Invoke(world, [.1f]); tick.Invoke(world, [.1f]);
                var after = State(world, player);
                Check(drops.Count == 0 && after.Items.Length == 24 && after.Items.Count(i => i.Item.Id == item.Id) == 1 &&
                    after.Gold == before.Gold + manual.SaleGold && after.AutoSold == before.AutoSold,
                    "Pierde el botín al liberar espacio o contabiliza una venta automática.");
                Check(world.MatchStatistics().Sum(StatisticGroup.Equipment, StatisticMeasure.Collected, (int)ItemTier.Mythic) == 25,
                    "La recogida posterior no cuenta exactamente un objeto.");
                SnapshotValidation.Check(world.Snapshot());
            }
        });
        test("Equipo predeterminado: red y guardado conservan prioridades; reiniciar mantiene la preferencia, no el equipo", () =>
        {
            var world = World(); var player = world.AddPlayer("Mago")!; var ally = world.AddPlayer("Aliado")!;
            var setup = Enabled(Element.Arcane, ItemAffixKind.ArcaneMark, ItemAffixKind.ArcaneEcho);
            var message = Protocol.Deserialize<ClientMessage>(Protocol.SerializeAction(new() { Type = "defense_equipment_setup", SurvivalEquipmentSetup = setup }))!;
            Check(message.SurvivalEquipmentSetup!.SameAs(setup) && Send(world, player, message), "Pierde la configuración en transporte.");
            setup.Priorities[0] = ItemAffixKind.DoubleBurn;
            Check(State(world, player).EquipmentSetup.Priorities[0] == ItemAffixKind.ArcaneMark, "El mensaje conserva una referencia mutable.");
            var privateState = DefenseVisibility.ForPlayer(world.Snapshot(), ally.Id).Defense!.Defenders.Single(p => p.PlayerId == player.Id);
            Check(!privateState.EquipmentSetup.Enabled && privateState.EquipmentSetup.Priorities.Length == 0, "Filtra las prioridades a otro jugador.");
            world.RemovePlayer(ally.Id);
            var save = world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var human = restored.Players[player.Id];
            Check(State(restored, human).EquipmentSetup.SameAs(State(world, player).EquipmentSetup) &&
                State(restored, human).EquipmentSetupInitialized, "Continuar pierde la selección automática.");
            typeof(GameWorld).GetMethod("ResetDefense", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(restored, []);
            Check(State(restored, human).EquipmentSetup.Enabled && State(restored, human).Items.Length == 0, "Reiniciar pierde la preferencia o conserva botín.");
            var node = JsonNode.Parse(save.State.GetRawText())!;
            foreach (var d in node["Defenders"]!.AsArray())
            { d!["Progress"]!.AsObject().Remove("EquipmentSetup"); d["Progress"]!.AsObject().Remove("EquipmentSetupInitialized"); }
            var legacy = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(node) });
            Check(!State(legacy, legacy.Players[player.Id]).EquipmentSetup.Enabled, "Un guardado anterior activa ventas sin permiso.");
        });
        test("Equipo predeterminado: rechaza prioridades inválidas sin vender ni alterar la configuración", () =>
        {
            var world = World(); var player = world.AddPlayer("Mago")!;
            Give(world, player, Piece(EquipmentSlot.Hat, EquipmentStat.Power, .1f));
            foreach (var setup in new[] { Enabled((Element)99), Enabled(Element.Fire, (ItemAffixKind)99),
                Enabled(Element.Fire, ItemAffixKind.DoubleBurn, ItemAffixKind.DoubleBurn), Enabled(Element.Fire) with { Priorities = null! },
                Enabled(Element.Fire, ItemAffixKind.ArcaneMark, ItemAffixKind.ArcaneEcho, ItemAffixKind.DoubleBurn,
                    ItemAffixKind.WaterShieldPool, ItemAffixKind.Icebreaker, ItemAffixKind.SteamImpact) })
                Check(!Setup(world, player, setup) && State(world, player).Items.Length == 1 && !State(world, player).EquipmentSetup.Enabled,
                    "Acepta o aplica parcialmente una configuración inválida.");
            var preferences = new InventoryPreferences { SurvivalEquipment = Enabled(Element.Earth, ItemAffixKind.RockFragmentation) };
            var reloaded = JsonSerializer.Deserialize<InventoryPreferences>(JsonSerializer.Serialize(preferences))!; reloaded.Normalize();
            Check(reloaded.SurvivalEquipment.SameAs(preferences.SurvivalEquipment), "No persiste la preferencia del dispositivo.");
        });
    }
}
