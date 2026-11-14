using System.Collections;
using System.Reflection;
using Arcana.Core;

internal static class DefenseBotEquipmentTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static EquipmentItem Piece(EquipmentSlot slot, EquipmentStat stat, float value,
        ItemTier tier = ItemTier.Mythic, EquipmentFamily family = EquipmentFamily.Mixed, ItemAffix? affix = null) =>
        new(Guid.NewGuid().ToString("N"), slot, tier, family, [new(stat, -.25f, .5f, value)], 10, EquipmentCatalogue.GenerationVersion)
        { Affix = affix };
    private static EquipmentLoadout Select(EquipmentItem[] items, EquipmentLoadout? current = null, WizardType type = WizardType.Balanced,
        IReadOnlyDictionary<string, int>? upgrades = null) => DefenseBotEquipment.Select(items, type, current ?? new(), upgrades);
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => typeof(GameWorld)
        .GetMethod("GrantDefenseItem", Private)!.Invoke(world, [player, item]);
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) =>
        world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id);
    private static bool Send(GameWorld world, Wizard player, ClientMessage message)
    { message.Sequence = player.LastSequence + 1; return world.ApplyMessage(player.Id, message); }
    private static (GameWorld World, Wizard Human, Wizard Bot) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 2, Seed = 37 });
        return (world, world.AddPlayer("Humano")!, world.AddPlayer("Bot", 0, bot: true)!);
    }

    public static void Run(Action<string, Action> test)
    {
        test("Bots: sustituyen la primera pieza por atributos mejores y conservan el equipo en empates", BetterStats);
        test("Bots: el tipo de mago orienta la elección elemental", Affinity);
        test("Bots: respetan límites de atributos e ignoran atributos antiguos inactivos", EffectiveStats);
        test("Bots: valoran el conjunto completo aunque sus piezas aisladas tengan peores tiradas", CompleteSet);
        test("Bots: combinan Marca y Eco en lugar de duplicar un afijo", Affixes);
        test("Bots: comparan el nivel real de los dos anillos y la mejora del nivel seis", Rings);
        test("Bots: recibir equipo mejora el conjunto sin cambiar inventarios humanos ni curar", Pickup);
        test("Bots: respetan las ranuras manuales y vacías, también después de continuar", ManualSlots);
        test("Bots: mejorar una pieza desequipada vuelve a comparar el inventario", Upgrade);
    }

    private static void BetterStats()
    {
        foreach (var stat in new[] { EquipmentStat.Power, EquipmentStat.Defense, EquipmentStat.HealingPower,
            EquipmentStat.MovementSpeed, EquipmentStat.SpellRange, EquipmentStat.ShieldCapacity })
        {
            var first = Piece(EquipmentSlot.Chest, stat, .01f);
            var better = Piece(EquipmentSlot.Chest, stat, .2f, ItemTier.Silver);
            Check(Select([first, better], new(Chest: first.Id)).Chest == better.Id, "La calidad prevalece sobre la mejora útil: " + stat);
            var same = better with { Id = Guid.NewGuid().ToString("N") };
            Check(Select([same, better], new(Chest: better.Id)).Chest == better.Id &&
                Select([better, same], new(Chest: better.Id)).Chest == better.Id, "Un empate cambia el equipo: " + stat);
        }
    }

    private static void Affinity()
    {
        var fire = Piece(EquipmentSlot.Weapon, EquipmentStat.FirePower, .2f);
        var cold = Piece(EquipmentSlot.Weapon, EquipmentStat.ColdPower, .2f);
        Check(Select([cold, fire], type: WizardType.Pyromancer).Weapon == fire.Id &&
            Select([fire, cold], type: WizardType.Cryomancer).Weapon == cold.Id, "La elección ignora la afinidad del mago.");
    }

    private static void EffectiveStats()
    {
        var weapon = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .4f);
        var redundant = Piece(EquipmentSlot.Boots, EquipmentStat.Power, .3f);
        var protection = Piece(EquipmentSlot.Boots, EquipmentStat.Defense, .2f, ItemTier.Silver);
        Check(Select([weapon, redundant, protection], new(Boots: redundant.Id, Weapon: weapon.Id)).Boots == protection.Id,
            "El bot valora potencia que ya supera el límite.");
        var legacy = Piece(EquipmentSlot.MagicRing, EquipmentStat.Power, .5f, family: EquipmentFamily.Life) with { GenerationVersion = 6 };
        var active = Piece(EquipmentSlot.MagicRing, EquipmentStat.ShieldCapacity, .1f, ItemTier.Silver, EquipmentFamily.Life);
        Check(Select([legacy, active], new(MagicRing: legacy.Id)).MagicRing == active.Id, "El bot suma un atributo inactivo de un anillo antiguo.");
    }

    private static void CompleteSet()
    {
        var items = Enum.GetValues<EquipmentSlot>().Where(s => !RingEffects.IsRing(s)).SelectMany(slot => new[]
        {
            Piece(slot, EquipmentStat.HealingPower, .01f, family: EquipmentFamily.Life),
            Piece(slot, EquipmentStat.HealingPower, .04f)
        }).ToArray();
        var current = new EquipmentLoadout();
        foreach (var item in items.Where(i => i.Family == EquipmentFamily.Mixed)) current = current.With(item.Slot, item.Id);
        var selected = Select(items, current);
        var equipped = items.Where(i => selected.Get(i.Slot) == i.Id).ToArray();
        Check(equipped.Length == 4 && equipped.All(i => i.Family == EquipmentFamily.Life),
            "El bot pierde el conjunto de cuatro piezas por comparar ranuras aisladas.");
    }

    private static void Affixes()
    {
        var mark = Piece(EquipmentSlot.Boots, EquipmentStat.ArcanePower, .1f, affix: new(ItemAffixKind.ArcaneMark, 2));
        var echo = Piece(EquipmentSlot.Boots, EquipmentStat.ArcanePower, .1f, affix: new(ItemAffixKind.ArcaneEcho, 2));
        var hat = Piece(EquipmentSlot.Hat, EquipmentStat.ArcanePower, .1f, affix: new(ItemAffixKind.ArcaneMark, 2));
        Check(Select([mark, echo, hat], new(Boots: mark.Id, Hat: hat.Id)).Boots == echo.Id,
            "El bot puntúa dos veces Marca en lugar de reunir Marca y Eco.");
    }

    private static void Rings()
    {
        var gold = Piece(EquipmentSlot.GoldRing, EquipmentStat.Power, .01f);
        var upgradedGold = gold with { Id = Guid.NewGuid().ToString("N") };
        Check(Select([gold, upgradedGold], new(GoldRing: gold.Id), upgrades: new Dictionary<string, int> { [upgradedGold.Id] = 10 })
            .GoldRing == upgradedGold.Id, "El bot ignora el intervalo de monedas del anillo mejorado.");
        var five = Piece(EquipmentSlot.MagicRing, EquipmentStat.ShieldCapacity, .1f, family: EquipmentFamily.Fire);
        var six = Piece(EquipmentSlot.MagicRing, EquipmentStat.ShieldCapacity, .01f, family: EquipmentFamily.Fire);
        Check(Select([five, six], new(MagicRing: five.Id), upgrades: new Dictionary<string, int> { [five.Id] = 5, [six.Id] = 6 })
            .MagicRing == six.Id, "El bot ignora el salto de habilidad del nivel seis.");
    }

    private static void Pickup()
    {
        var (world, human, bot) = World();
        var first = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .01f, ItemTier.Silver);
        var better = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .2f, ItemTier.Silver);
        var humanFirst = first with { Id = Guid.NewGuid().ToString("N") };
        bot.Health = 600;
        Give(world, bot, first); Give(world, bot, better);
        Give(world, human, humanFirst); Give(world, human, better with { Id = Guid.NewGuid().ToString("N") });
        Check(State(world, bot).Items.Single(i => i.Equipped).Item.Id == better.Id &&
            State(world, human).Items.Single(i => i.Equipped).Item.Id == humanFirst.Id &&
            State(world, bot).Items.Length == 2 && State(world, bot).Gold == 0 && bot.Health == 600,
            "La mejora no se equipa o altera el inventario humano, vende piezas o cura al bot.");
        SnapshotValidation.Check(world.Snapshot());
    }

    private static void ManualSlots()
    {
        var (world, human, bot) = World();
        var first = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .01f, ItemTier.Silver);
        var better = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .2f, ItemTier.Silver);
        Give(world, bot, first);
        Check(Send(world, human, new() { Type = "defense_bot_equip", DefenseBotId = bot.Id,
            Slot = (int)EquipmentSlot.Weapon, ItemId = first.Id }), "No acepta el equipo manual del bot.");
        Give(world, bot, better);
        Check(State(world, bot).Items.Single(i => i.Equipped).Item.Id == first.Id, "El bot sustituye una elección manual.");
        Check(Send(world, human, new() { Type = "defense_bot_equip", DefenseBotId = bot.Id,
            Slot = (int)EquipmentSlot.Weapon, ItemId = "" }), "No permite vaciar una ranura.");
        var restored = GameWorld.RestoreSurvival(world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N")));
        var savedBot = restored.Players[bot.Id];
        Give(restored, savedBot, better with { Id = Guid.NewGuid().ToString("N") });
        Check(State(restored, savedBot).Items.All(i => !i.Equipped) && State(restored, savedBot).Items.Length == 3,
            "Al continuar, el bot vuelve a llenar una ranura vaciada manualmente.");
        SnapshotValidation.Check(restored.Snapshot());
    }

    private static void Upgrade()
    {
        var (world, human, bot) = World();
        var first = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .1f, ItemTier.Silver);
        var second = Piece(EquipmentSlot.Weapon, EquipmentStat.Power, .105f, ItemTier.Silver);
        Give(world, bot, first); Give(world, bot, second);
        Check(State(world, bot).Items.Single(i => i.Equipped).Item.Id == second.Id, "No prepara el arma más fuerte.");
        var defenders = (IDictionary)typeof(GameWorld).GetField("_defenders", Private)!.GetValue(world)!;
        var progress = defenders[bot.Id]!;
        progress.GetType().GetField("Gold")!.SetValue(progress, 1000);
        Check(Send(world, human, new() { Type = "defense_bot_upgrade", DefenseBotId = bot.Id,
            ItemId = first.Id, DefenseUpgradeFromLevel = 0 }), "No mejora la pieza desequipada.");
        Check(State(world, bot).Items.Single(i => i.Equipped).Item.Id == first.Id && bot.Equipment.Power > 1.109f,
            "Mejorar una pieza no vuelve a comparar el inventario.");
        var restored = GameWorld.RestoreSurvival(world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N")));
        Check(State(restored, restored.Players[bot.Id]).Items.Single(i => i.Equipped).Item.Id == first.Id,
            "Continuar pierde el equipo elegido tras la mejora.");
    }
}
