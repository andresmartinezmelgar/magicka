using System.Collections;
using System.Reflection;
using Arcana.Core;

internal static class SurvivalEconomyTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static (GameWorld World, Wizard Player) World()
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 1, Seed = 37 });
        return (world, world.AddPlayer("Defensor")!);
    }
    private static object Progress(GameWorld world, Wizard player) =>
        ((IDictionary)typeof(GameWorld).GetField("_defenders", Private)!.GetValue(world)!)[player.Id]!;
    private static void Gold(GameWorld world, Wizard player, int gold)
    { var progress = Progress(world, player); progress.GetType().GetField("Gold")!.SetValue(progress, gold); }
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) =>
        world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id);
    private static bool Buy(GameWorld world, Wizard player, string type, string id = "", int slot = 0) =>
        world.ApplyMessage(player.Id, new() { Type = type, ItemId = id, Slot = slot, Sequence = player.LastSequence + 1 });
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => typeof(GameWorld)
        .GetMethod("GrantDefenseItem", Private)!.Invoke(world, [player, item]);
    private static EquipmentItem Ring(ItemTier tier, EquipmentSlot slot = EquipmentSlot.MagicRing) =>
        EquipmentCatalogue.Generate(new(73), tier: tier, slot: slot, family: EquipmentFamily.Life);

    public static void Run(Action<string, Action> test)
    {
        test("Economía de Supervivencia: los incrementos de coste crecen y no son lineales", Curves);
        test("Economía de Supervivencia: un aura completa y una habilidad completa requieren 4550 de oro", Powers);
        test("Economía de Supervivencia: diez mejoras de equipo son asequibles según su calidad", Equipment);
        test("Economía de Supervivencia: 5400 de oro permiten especializarse o repartir mejoras con prioridades distintas", BuildChoices);
        test("Economía de Supervivencia: suministro por 50, sin cobrar con oro insuficiente o inventario lleno", Supply);
        test("Economía de Supervivencia: continuar conserva oro y niveles y aplica el nuevo coste al siguiente nivel", SavedProgress);
        test("Economía de cuenta: los anillos permanentes conservan su tarifa y el cobro único", PermanentRing);
    }

    private static void Curves()
    {
        var curves = new List<int[]>
        {
            Enumerable.Range(0, DefenseRules.MaxAuraLevel).Select(DefenseRules.AuraCost).ToArray(),
            Enumerable.Range(0, DefenseRules.MaxPassiveLevel).Select(DefenseRules.PassiveCost).ToArray()
        };
        foreach (var tier in Enum.GetValues<ItemTier>())
            curves.Add(Enumerable.Range(0, DefenseRules.ItemUpgradeLimit).Select(level => DefenseRules.SurvivalUpgradeCost(tier, level)).ToArray());
        foreach (var prices in curves)
        {
            var increases = prices.Zip(prices.Skip(1)).Select(pair => pair.Second - pair.First).ToArray();
            Check(prices[0] > 0 && increases.All(increase => increase > 0) && increases[^1] > increases[0] &&
                increases.Zip(increases.Skip(1)).All(pair => pair.Second >= pair.First),
                "La curva vuelve a ser lineal, reduce precios o abarata los incrementos finales.");
        }
    }

    private static void Powers()
    {
        var (world, player) = World();
        Gold(world, player, 39);
        Check(!Buy(world, player, "defense_aura") && State(world, player).Gold == 39 && State(world, player).Auras[0] == 0,
            "Comprar sin oro modifica el aura o el saldo.");
        Gold(world, player, 40);
        Check(Buy(world, player, "defense_aura") && State(world, player).Gold == 0, "La primera aura no cuesta 40.");

        (world, player) = World(); Gold(world, player, 4549);
        for (int level = 0; level < 10; level++) Check(Buy(world, player, "defense_aura"), "El aura supera su presupuesto de diez niveles.");
        Check(State(world, player).Gold == 1249 && State(world, player).Auras[0] == 10, "El aura no suma 3300 de oro.");
        for (int level = 0; level < 4; level++) Check(Buy(world, player, "defense_passive"), "La habilidad supera su presupuesto.");
        Check(State(world, player).Gold == 499 && !Buy(world, player, "defense_passive") && State(world, player).Passives[0] == 4,
            "La última habilidad se compra sin su precio completo.");
        Gold(world, player, 500);
        Check(Buy(world, player, "defense_passive") && State(world, player).Gold == 0 && State(world, player).Passives[0] == 5,
            "La habilidad no suma 1250 de oro o pierde el nivel.");
        Gold(world, player, 1000);
        Check(!Buy(world, player, "defense_aura") && !Buy(world, player, "defense_passive") && State(world, player).Gold == 1000,
            "El descuento permite superar los niveles máximos o cobra un intento rechazado.");
    }

    private static void Equipment()
    {
        int[] budgets = [785, 1190, 1570, 2355, 3140];
        foreach (var tier in Enum.GetValues<ItemTier>())
        {
            var (world, player) = World(); var ring = Ring(tier); Give(world, player, ring);
            Gold(world, player, budgets[(int)tier]);
            for (int level = 0; level < 10; level++)
                Check(Buy(world, player, "defense_upgrade", ring.Id), "La pieza supera el presupuesto temporal: " + tier);
            var state = State(world, player);
            Check(state.Gold == 0 && state.Items.Single().Upgrades == 10, "El coste total no coincide: " + tier);
            Gold(world, player, 1000);
            Check(!Buy(world, player, "defense_upgrade", ring.Id) && State(world, player).Gold == 1000,
                "Una pieza al máximo sigue cobrando o ganando niveles: " + tier);
        }
    }

    private static void BuildChoices()
    {
        var (world, player) = World(); var ring = Ring(ItemTier.Mythic); Give(world, player, ring); Gold(world, player, 5400);
        for (int level = 0; level < 10; level++) Check(Buy(world, player, "defense_aura"), "No permite concentrar el oro en un aura.");
        for (int level = 0; level < 5; level++) Check(Buy(world, player, "defense_passive"), "No permite completar la habilidad prioritaria.");
        for (int level = 0; level < 6; level++) Check(Buy(world, player, "defense_upgrade", ring.Id), "El anillo no alcanza su salto del nivel seis.");
        Check(State(world, player).Gold == 30 && !Buy(world, player, "defense_aura", slot: 1),
            "La ruta especializada no ajusta el presupuesto o permite añadir otro poder sin oro.");

        (world, player) = World(); Gold(world, player, 5400);
        for (int slot = 0; slot < 3; slot++)
            for (int level = 0; level < 6; level++) Check(Buy(world, player, "defense_aura", slot: slot), "No permite repartir las auras.");
        foreach (int slot in new[] { 0, 2, 3 })
            for (int level = 0; level < 3; level++) Check(Buy(world, player, "defense_passive", slot: slot), "No permite repartir habilidades.");
        foreach (var slot in new[] { EquipmentSlot.GoldRing, EquipmentSlot.MagicRing })
        {
            ring = Ring(ItemTier.Mythic, slot); Give(world, player, ring);
            for (int level = 0; level < 6; level++) Check(Buy(world, player, "defense_upgrade", ring.Id), "La ruta repartida pierde el nivel seis de los anillos.");
        }
        Check(State(world, player).Gold == 160 && !Buy(world, player, "defense_aura") && State(world, player).Gold == 160,
            "La ruta repartida no respeta el presupuesto o compra un nivel alto sin su coste.");
    }

    private static void Supply()
    {
        var (world, player) = World(); Gold(world, player, 49);
        Check(!Buy(world, player, "defense_supply") && State(world, player).Gold == 49 && State(world, player).Items.Length == 0,
            "El suministro cobra sin oro suficiente.");
        Gold(world, player, 50);
        Check(Buy(world, player, "defense_supply") && State(world, player).Gold == 0 && State(world, player).Items.Length == 1,
            "El suministro no cuesta 50 o no entrega la pieza.");
        for (int i = 1; i < DefenseRules.InventoryLimit; i++) Give(world, player, Ring(ItemTier.Silver));
        Gold(world, player, 50);
        Check(!Buy(world, player, "defense_supply") && State(world, player).Gold == 50 && State(world, player).Items.Length == DefenseRules.InventoryLimit,
            "El inventario lleno consume el oro del suministro.");
        int expectedSalePerHundred = Enum.GetValues<ItemTier>().Sum(tier =>
            EquipmentCatalogue.TierChance(tier) * EquipmentCatalogue.Generate(new(73), tier: tier).SaleGold);
        Check(DefenseRules.SupplyCost * 100 > expectedSalePerHundred, "Comprar y revender suministros genera oro de media.");
    }

    private static void SavedProgress()
    {
        var (world, player) = World(); var ring = Ring(ItemTier.Mythic); Give(world, player, ring); Gold(world, player, 400);
        for (int level = 0; level < 3; level++) Check(Buy(world, player, "defense_upgrade", ring.Id), "No prepara los niveles guardados.");
        // Saldo de una partida anterior: cambiar la curva no vuelve a calcular sus compras.
        Gold(world, player, 160);
        var restored = GameWorld.RestoreSurvival(world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N")));
        var defender = restored.Players[player.Id]; var state = State(restored, defender);
        Check(state.Gold == 160 && state.Items.Single().Upgrades == 3, "Continuar altera el saldo o reinicia las mejoras.");
        Check(Buy(restored, defender, "defense_upgrade", ring.Id) && State(restored, defender).Gold == 20 &&
            State(restored, defender).Items.Single().Upgrades == 4, "La mejora tras continuar no aplica la tarifa temporal.");
        SnapshotValidation.Check(restored.Snapshot());
    }

    private static void PermanentRing()
    {
        var ring = Ring(ItemTier.Mythic);
        var profile = new AccountProfile("cuenta", "Mago", 399, 0, 0, new(), [ring]);
        var request = new RingUpgradeRequest(ring.Id, 0);
        bool rejected = false;
        try { AccountInventory.UpgradeRing(profile, request); } catch (ArgumentException) { rejected = true; }
        Check(rejected && profile.Gold == 399 && ring.UpgradeLevel == 0, "La tarifa temporal se aplica al oro de cuenta.");
        var upgraded = AccountInventory.UpgradeRing(profile with { Gold = 400 }, request);
        Check(upgraded.Gold == 0 && upgraded.Items.Single().UpgradeLevel == 1 &&
            AccountInventory.UpgradeRing(upgraded, request) == upgraded, "La tarifa permanente cambió o un reintento cobra dos veces.");
    }
}
