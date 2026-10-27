using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class RingTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static object? Call(GameWorld world, string name, params object[] args) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static EquipmentItem Item(EquipmentSlot slot, EquipmentFamily family = EquipmentFamily.Mixed, int level = 0) =>
        EquipmentCatalogue.Generate(new(81), tier: ItemTier.Silver, slot: slot, family: family) with { UpgradeLevel = level, Affix = null };
    private static AccountProfile Profile(params EquipmentItem[] items)
    {
        var loadout = new EquipmentLoadout();
        foreach (var item in items) loadout = loadout.With(item.Slot, item.Id);
        return new("rings", "Mago", 1000000, 0, 0, loadout, items);
    }
    private static (GameWorld World, Wizard Player, Wizard Enemy) World(EquipmentFamily family, int level = 0)
    {
        var world = new GameWorld(new() { Mode = GameMode.Warfare });
        var p = world.AddPlayer("Mago", 0)!; var e = world.AddPlayer("Rival", 1)!;
        p.Position = new(-3, 0); e.Position = new(-1, 0); p.Invulnerable = e.Invulnerable = 0;
        world.EquipPlayer(p.Id, PublicEquipment.Prepare(Profile(Item(EquipmentSlot.MagicRing, family, level))));
        return (world, p, e);
    }
    public static void Run(Action<string, Action> test)
    {
        RingNumbersTests.Run(test);
        test("Anillos: dos posiciones propias, todas las calidades y compatibilidad con selecciones antiguas", () =>
        {
            var old = Protocol.Deserialize<EquipmentLoadout>("{\"boots\":\"\",\"hat\":\"\",\"chest\":\"\",\"weapon\":\"\"}")!;
            Check(old == new EquipmentLoadout(), "Las cuatro ranuras antiguas pierden compatibilidad.");
            foreach (var tier in Enum.GetValues<ItemTier>())
            foreach (var slot in new[] { EquipmentSlot.GoldRing, EquipmentSlot.MagicRing })
            {
                var item = EquipmentCatalogue.Generate(new(39), tier: tier, slot: slot);
                Check(EquipmentCatalogue.Valid(item) && RingEffects.Describe(item).Length > 0, "Un anillo no es obtenible o explicable.");
            }
            var profile = Profile(Item(EquipmentSlot.Boots), Item(EquipmentSlot.GoldRing), Item(EquipmentSlot.MagicRing, EquipmentFamily.Fire));
            var view = PublicEquipment.Prepare(profile);
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, view.View));
            Check(view.View.Items.Length == 3 && view.Modifiers.ExtraGoldEvery == 11 && view.Modifiers.MagicRingFamily == EquipmentFamily.Fire,
                "Los anillos sustituyen otras piezas o sus efectos requieren un afijo raro.");
            Check(EquipmentQuery.Preview(Profile(), profile.Items.Single(i => i.Slot == EquipmentSlot.MagicRing)).Describe().Contains("Onda de brasas"),
                "La comparación omite el cambio de acción del anillo.");
            foreach (var ring in profile.Items.Where(i => RingEffects.IsRing(i.Slot)))
            {
                try { AccountInventory.Sell(profile, ring.Id); throw new Exception("Se vendió un anillo equipado."); } catch (ArgumentException) { }
                Check(new InventoryPreferences().Sellable(profile, [ring.Id]).Length == 0, "La venta múltiple incluye un anillo equipado.");
            }
        });
        test("Anillos: diez mejoras permanentes pagan una vez y progresan de once a una moneda", () =>
        {
            var ring = Item(EquipmentSlot.GoldRing); var profile = Profile(ring); long gold = profile.Gold;
            for (int level = 0; level < 10; level++)
            {
                gold -= DefenseRules.UpgradeCost(ring.Tier, level);
                var request = new RingUpgradeRequest(ring.Id, level);
                profile = AccountInventory.UpgradeRing(profile, request);
                var retry = AccountInventory.UpgradeRing(profile, request);
                Check(retry == profile && profile.Gold == gold && profile.Items.Single().UpgradeLevel == level + 1 &&
                    EquipmentModifiers.Calculate(profile).ExtraGoldEvery == 10 - level, "La mejora pierde nivel, duplica el cobro o no modifica el oro.");
                PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, PublicEquipment.Prepare(profile).View));
            }
            var copy = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(profile))!;
            Check(copy.Items.Single().UpgradeLevel == 10 && EquipmentModifiers.Calculate(copy).ExtraGoldEvery == 1, "El nivel no se conserva en el perfil.");
            try { AccountInventory.UpgradeRing(copy, new(ring.Id, 10)); throw new Exception("Admite nivel once."); } catch (ArgumentException) { }
            try { AccountInventory.UpgradeRing(Profile(Item(EquipmentSlot.Boots)), new(ring.Id, 0)); throw new Exception("Acepta un anillo ajeno."); } catch (ArgumentException) { }
        });
        test("Anillos: el escudo se repone roto sin consumir selección, energía ni interrumpir canales", () =>
        {
            var (world, player, _) = World(EquipmentFamily.Fire);
            player.Selection.Add(Element.Water); player.Channel = new([Element.Arcane]);
            player.Input.AimX = 10; player.LastInput = world.Time; player.ActionCooldown = 2;
            world.Step();
            Check(player.Shield > 0 && player.Ward.SequenceEqual([Element.Fire]) && player.Channel != null &&
                player.Selection.Elements.SequenceEqual([Element.Water]) && player.Energy == 100 && player.ActionCooldown > 1.9f,
                "El anillo usa el lanzamiento manual o no crea el escudo elemental.");
            player.Shield = 0; float clock = player.RingCooldown;
            world.Step(); Check(player.Shield == 0 && player.RingCooldown < clock, "El escudo se repone sin respetar la recarga.");
            player.RingCooldown = GameWorld.StepSeconds / 2; world.Step();
            Check(player.Shield > 0 && Math.Abs(player.RingCooldown - 16) < 0.01, "Romper el escudo impide su siguiente renovación.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Anillos: cada familia activa su acción, respeta aliados y duplica elemento desde el sexto nivel", () =>
        {
            foreach (var family in Enum.GetValues<EquipmentFamily>())
            {
                var (world, p, e) = World(family, 6);
                p.Health = 500;
                var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(-4, 0); ally.Health = 500; ally.Invulnerable = 0;
                Call(world, "TickRing", p);
                Check(p.Shield > 0 && p.RingCooldown < 16 && RingEffects.ElementCopies(6) == 2 &&
                    RingEffects.ActionName(family).Length > 0, "Falta una variante o el nivel no acorta la recarga.");
                if (family != EquipmentFamily.Mixed && family != EquipmentFamily.Shield)
                    Check(p.Ward.Length == 2 && p.Ward[0] == p.Ward[1], "No genera escudo + elemento + elemento.");
                if (family == EquipmentFamily.Life) Check(p.Health > 500 && ally.Health > 500 && e.Health == 1000, "La curación daña al enemigo o ignora al aliado.");
                else if (family == EquipmentFamily.Shield) Check(ally.Shield > 0 && e.Shield == 0, "El amparo protege al enemigo.");
                else if (family == EquipmentFamily.Mixed) Check(e.Knockback.Length > 0 && e.Health == 1000, "La repulsión no empuja o daña.");
                else if (family == EquipmentFamily.Water) Check(e.Wet > 0 && e.Knockback.Length > 0, "La salpicadura no moja y empuja.");
                else Check(e.Health < 1000, "La acción ofensiva no alcanza al rival: " + family);
                Check(family == EquipmentFamily.Life || ally.Health == 500, "Una acción ofensiva afecta a aliados.");
                SnapshotValidation.Check(world.Snapshot());
            }
            Check(RingEffects.ShieldElements(EquipmentFamily.Fire, 5).Length == 2 && RingEffects.ShieldElements(EquipmentFamily.Fire, 6).Length == 3,
                "La transición de un elemento a dos no ocurre tras el quinto nivel.");
        });
        test("Anillos: escudos mayores se conservan, los muertos no se reaniman y cambiar equipo no reinicia recarga", () =>
        {
            var (world, p, _) = World(EquipmentFamily.Fire);
            p.Shield = 100; p.ShieldMax = 650; p.ShieldTime = p.WardTime = 10; p.Ward = [Element.Earth];
            Call(world, "TickRing", p);
            Check(p.Shield == 100 && p.ShieldMax == 650 && p.Ward.SequenceEqual([Element.Earth]), "El anillo sustituyó una protección más fuerte.");
            float clock = p.RingCooldown;
            world.EquipPlayer(p.Id, EquipmentModifiers.None);
            world.EquipPlayer(p.Id, PublicEquipment.Prepare(Profile(Item(EquipmentSlot.MagicRing, EquipmentFamily.Life, 10))));
            Call(world, "TickRing", p); Check(p.RingCooldown == clock && p.Ward.SequenceEqual([Element.Earth]), "Cambiar anillo permite activar sin recarga.");
            p.Health = p.Shield = 0; p.Respawn = 10; p.RingCooldown = 0;
            world.Step(); Check(!p.Alive && p.Shield == 0, "El anillo se activa estando muerto.");
        });
        test("Anillos: el rayo de nivel seis alcanza a dos rivales distintos y su daño tiene origen directo", () =>
        {
            var (world, p, first) = World(EquipmentFamily.Lightning, 6);
            var second = world.AddPlayer("Otro rival", 1)!; second.Position = new(-2, 1); second.Invulnerable = 0;
            var far = world.AddPlayer("Fuera de alcance", 1)!; far.Position = new(6, 0); far.Invulnerable = 0;
            Call(world, "TickRing", p);
            Check(first.Health < 1000 && second.Health < 1000 && far.Health == 1000, "La descarga repite el mismo blanco o excede el alcance.");
            var report = world.MatchStatistics();
            Check(report.Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, (int)Element.Lightning) > 0,
                "El daño selectivo del rayo aparece como área.");
        });
        test("Anillos: el daño del pulso de fuego se separa como área y su quemadura como quemadura", () =>
        {
            var (world, p, enemy) = World(EquipmentFamily.Fire);
            Call(world, "TickRing", p); world.Step();
            var report = world.MatchStatistics(); report.Validate();
            Check(report.Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, (int)StatisticDamageSource.Area * 9 + (int)Element.Fire) > 0 &&
                report.Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, (int)StatisticDamageSource.Burn * 9 + (int)Element.Fire) > 0 && enemy.Burning > 0,
                "El pulso o la quemadura se mezclan con daño directo.");
        });
        test("Anillos: guardar, continuar y reconectar conservan niveles, piezas y recarga sin renovación gratis", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4 });
            var p = world.AddPlayer("Mago")!; world.AttachDefenseAccount(p.Id, "ring-owner");
            var ring = Item(EquipmentSlot.MagicRing, EquipmentFamily.Water);
            Call(world, "GrantDefenseItem", p, ring);
            var all = (IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
            var progress = all[p.Id]!; var upgrades = (IDictionary)progress.GetType().GetField("Upgrades")!.GetValue(progress)!;
            upgrades[ring.Id] = 6; Call(world, "RecalculateDefender", p);
            Call(world, "TickRing", p); p.Shield = 0; p.RingCooldown = 7;
            var saved = world.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(saved); var continued = restored.Players[p.Id];
            Check(continued.Equipment.MagicRingLevel == 6 && Math.Abs(continued.RingCooldown - 7) < 0.001 && continued.Shield == 0,
                "Continuar pierde la mejora o restaura un escudo gratis.");
            restored.Step(); Check(continued.Shield == 0 && continued.RingCooldown < 7, "Continuar reinicia la recarga.");
            world.RemovePlayer(p.Id); var rejoined = world.AddPlayer("Mago")!;
            Check(world.AttachDefenseAccount(rejoined.Id, "ring-owner") && rejoined.RingCooldown == 7 && rejoined.Shield == 0 && rejoined.Equipment.MagicRingLevel == 6,
                "Reconectar pierde el anillo o renueva su escudo.");
            SnapshotValidation.Check(restored.Snapshot());
        });
    }
}
