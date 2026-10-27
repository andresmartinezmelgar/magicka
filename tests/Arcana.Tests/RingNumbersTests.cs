using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class RingNumbersTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static void Near(float actual, float expected, string reason) => Check(Math.Abs(actual - expected) < .003f, reason + $": {actual} != {expected}");
    private static object? Call(GameWorld world, string method, params object[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static AccountProfile Profile(EquipmentItem item) => new("magic-ring", "Mago", 1000000, 0, 0,
        new EquipmentLoadout(MagicRing: item.Id), [item]);
    private static EquipmentItem Legacy(EquipmentStat extra) => new(Guid.NewGuid().ToString("N"), EquipmentSlot.MagicRing,
        ItemTier.Mythic, EquipmentFamily.Fire,
        [new(EquipmentStat.ShieldCapacity, .01f, .5f, .1f), new(extra, .01f, .5f, .2f)], 400, GenerationVersion: 6)
        { Affix = new(ItemAffixKind.DoubleBurn) };

    internal static void Run(Action<string, Action> test)
    {
        test("Anillo mágico: todas las calidades y familias generan solo capacidad de escudo y ningún afijo", () =>
        {
            foreach (var tier in Enum.GetValues<ItemTier>())
            foreach (var family in Enum.GetValues<EquipmentFamily>())
            for (int seed = 0; seed < 50; seed++)
            {
                var item = EquipmentCatalogue.Generate(new(seed), tier: tier, slot: EquipmentSlot.MagicRing, family: family);
                Check(EquipmentCatalogue.Valid(item) && item.Affix == null && item.Bonuses is [{ Stat: EquipmentStat.ShieldCapacity }],
                    "Un anillo nuevo contiene otro atributo o un afijo.");
                var gear = EquipmentModifiers.Calculate(Profile(item));
                Check(gear.ShieldCapacity > 1 && gear.Speed == 1 && gear.Power == 1 && gear.BurnPotency == 1 && gear.MagicRingFamily == family,
                    "El anillo aporta estadísticas ajenas o pierde la habilidad elemental.");
            }
        });
        test("Anillo mágico: atributos y afijos antiguos quedan inactivos sin invalidar ni volver a sortear la pieza", () =>
        {
            var forbidden = Legacy(EquipmentStat.Power);
            Check(!EquipmentCatalogue.Valid(forbidden with { GenerationVersion = 7, Affix = null }) &&
                !EquipmentCatalogue.Valid(forbidden with { GenerationVersion = 7, Bonuses = [forbidden.Bonuses[0]] }),
                "El catálogo nuevo admite estadísticas adicionales o un afijo en el anillo mágico.");
            foreach (var stat in Enum.GetValues<EquipmentStat>().Where(s => s != EquipmentStat.ShieldCapacity))
            {
                var item = Legacy(stat);
                Check(EquipmentCatalogue.Valid(item) && !EquipmentCatalogue.Valid(item with { GenerationVersion = 7 }), "Compatibilidad de catálogo incorrecta.");
                var profile = Protocol.Deserialize<AccountProfile>(Protocol.Serialize(Profile(item)))!;
                var prepared = PublicEquipment.Prepare(profile);
                Check(prepared.Modifiers.Bonus(stat) == 0 && prepared.Modifiers.BurnPotency == 1 &&
                    prepared.View.Items.Single().Bonuses is [{ Stat: EquipmentStat.ShieldCapacity }] && prepared.View.Items.Single().Affix == null,
                    "Un atributo o afijo retirado se aplica o se anuncia como activo.");
                PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, prepared.View));
                var upgraded = AccountInventory.UpgradeRing(profile, new(item.Id, 0));
                var next = upgraded.Items.Single();
                Check(next.Id == item.Id && next.GenerationVersion == 6 && next.Affix == item.Affix &&
                    next.Bonuses.Single(b => b.Stat == stat) == item.Bonuses.Single(b => b.Stat == stat),
                    "La mejora modifica o pierde la tirada antigua inactiva.");
                Near(next.Bonuses.Single(b => b.Stat == EquipmentStat.ShieldCapacity).Current, .11f, "No mejora la capacidad del escudo");
                Check(EquipmentQuery.Compare(next, item).All(x => x.Stat == EquipmentStat.ShieldCapacity) &&
                    !new EquipmentFilter(Stat: stat).Matches(item), "La comparación o el filtro atribuyen efectos al atributo retirado.");
                var shown = RingEffects.EquipmentFor(prepared.View);
                Near(shown.ShieldCapacity, prepared.Modifiers.ShieldCapacity, "La ficha pública cambia el escudo");
                Check(shown.Power == 1 && shown.BurnPotency == 1, "La ficha pública revive un efecto antiguo.");
            }
            var noShield = Legacy(EquipmentStat.Power) with { Bonuses = [new(EquipmentStat.Power, .01f, .5f, .2f)] };
            var empty = PublicEquipment.Prepare(Profile(noShield));
            Check(empty.View.Items.Single().Bonuses.Length == 0 && empty.Modifiers.Power == 1 && empty.Modifiers.MagicRingFamily != null,
                "La pieza antigua sin capacidad válida añade daño o pierde la habilidad.");
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, empty.View));
        });
        test("Anillo mágico: las mejoras de Supervivencia solo alteran capacidad y nivel de la habilidad", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4 });
            var player = world.AddPlayer("Mago")!; var item = Legacy(EquipmentStat.Power);
            Call(world, "GrantDefenseItem", player, item);
            var progress = ((IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!)[player.Id]!;
            progress.GetType().GetField("Gold")!.SetValue(progress, 1000000);
            Check(world.ApplyMessage(player.Id, new() { Type = "defense_shop", Slot = 1, Sequence = player.LastSequence + 1 }) &&
                world.ApplyMessage(player.Id, new() { Type = "defense_upgrade", ItemId = item.Id, Sequence = player.LastSequence + 1 }), "No se puede mejorar la habilidad.");
            var next = world.Snapshot().Defense!.Defenders.Single().Items.Single();
            Check(next.Upgrades == 1 && next.Item.Bonuses.Single(b => b.Stat == EquipmentStat.Power) == item.Bonuses.Single(b => b.Stat == EquipmentStat.Power) &&
                player.Equipment.Power == 1 && player.Equipment.BurnPotency == 1 && player.Equipment.MagicRingLevel == 1,
                "Supervivencia mejora o activa un atributo retirado.");
            var restored = GameWorld.RestoreSurvival(world.SaveSurvival(player.Id, Guid.NewGuid().ToString("N")));
            Check(restored.Players[player.Id].Equipment.Power == 1 && restored.Players[player.Id].Equipment.MagicRingLevel == 1,
                "Continuar pierde el nivel o reactiva atributos antiguos.");
            SnapshotValidation.Check(restored.Snapshot());
        });
        test("Anillo mágico: la ficha distingue escudo, regeneración activa, curación por pulso y medias", () =>
        {
            var life = RingEffects.Numbers(EquipmentFamily.Life, 0);
            Near(life.Shield, 180, "Escudo elemental de nivel cero"); Near(life.Regeneration, 38, "Regeneración de vida");
            Near(life.Healing, 60, "Curación por pulso"); Near(life.MeanSelfHealing, 32.25f, "Media con hueco de cuatro segundos sin escudo");
            var top = RingEffects.Numbers(EquipmentFamily.Life, 10);
            Near(top.Shield, 360, "Escudo duplicado"); Near(top.Cooldown, 8, "Recarga máxima"); Near(top.MeanSelfHealing, 91, "Media con escudo continuo");
            var shield = RingEffects.Numbers(EquipmentFamily.Shield, 6);
            Near(shield.Shield, 840, "Escudo básico duplicado"); Near(shield.AllyShield, 200, "Amparo a aliados");
            foreach (var family in Enum.GetValues<EquipmentFamily>())
            {
                string text = RingEffects.Describe(EquipmentSlot.MagicRing, family, 0);
                var numbers = RingEffects.Numbers(family, 0);
                Check(text.Contains("HP") && text.Contains("recarga") && text.Contains("duración"),
                    "Una familia omite las cifras de su escudo.");
                Check(text.Contains("HP/s") == (numbers.Regeneration > 0 || numbers.Healing > 0) &&
                    text.Contains("DMG/s") == (numbers.Damage > 0 || numbers.BurnPerSecond > 0),
                    "La ficha anuncia curación o daño que este anillo no aporta.");
                Check(!text.Contains("HP/s de curación: 0") && !text.Contains("DMG/s: 0") && !text.Contains("DMG/s contra vida: 0"),
                    "La ficha contiene efectos sin aporte.");
            }
        });
        test("Anillo mágico: daño, quemadura y objetivos usan las reglas reales, sin duplicar el rayo por objetivo", () =>
        {
            var fire = RingEffects.Numbers(EquipmentFamily.Fire, 0);
            Near(fire.Damage, 29, "Impacto de fuego"); Near(fire.BurnPerSecond, 28, "Quemadura"); Near(fire.MeanDamage, 8.8125f, "Media de fuego con quemadura");
            Near(RingEffects.Numbers(EquipmentFamily.Cold, 0).Damage, 12, "Impacto de frío");
            Near(RingEffects.Numbers(EquipmentFamily.Arcane, 0).Damage, 57.5f, "Impacto arcano");
            Near(RingEffects.Numbers(EquipmentFamily.Earth, 0).Damage, 50, "Impacto de tierra");
            var lightning = RingEffects.Numbers(EquipmentFamily.Lightning, 10);
            Near(lightning.Damage, 50, "Daño individual del rayo"); Near(lightning.MeanDamage, 6.25f, "Media individual del rayo");
            Check(lightning.Targets == 2, "El rayo perdió su segundo objetivo.");
            Near(RingEffects.Numbers(EquipmentFamily.Lightning, 6, EquipmentModifiers.None with { WetLightningPower = 1.2f }).WetDamage,
                114, "El daño contra mojado omite el bono del equipo");
            var water = RingEffects.Numbers(EquipmentFamily.Water, 6);
            Check(water.Damage == 0 && water.ShieldPressure == 35, "El agua confunde presión contra escudos con daño de vida.");
            var gear = EquipmentModifiers.None with { ShieldCapacity = 1.25f, Healing = 1.5f, Power = 1.2f, Fire = 1.5f, BurnPotency = 2, BurnDuration = 1.5f };
            var boosted = RingEffects.Numbers(EquipmentFamily.Fire, 0, gear);
            Near(boosted.Shield, 225, "Capacidad con equipo"); Near(boosted.Damage, 52.2f, "Impacto con equipo");
            Near(boosted.BurnPerSecond, 100.8f, "Quemadura con equipo"); Near(boosted.MeanDamage, 41.0625f, "Media con equipo");
        });
        test("Anillo mágico: la ficha pública y la comparación conservan los modificadores del resto del equipo", () =>
        {
            var ring = EquipmentCatalogue.Generate(new(82), tier: ItemTier.Mythic, slot: EquipmentSlot.MagicRing, family: EquipmentFamily.Fire);
            var weapon = new EquipmentItem(Guid.NewGuid().ToString("N"), EquipmentSlot.Weapon, ItemTier.Mythic, EquipmentFamily.Fire,
                [new(EquipmentStat.Power, 0, .5f, .2f), new(EquipmentStat.FirePower, 0, .5f, .2f), new(EquipmentStat.BurnDuration, 0, .5f, .2f)], 400)
                { Affix = new(ItemAffixKind.DoubleBurn) };
            var chest = new EquipmentItem(Guid.NewGuid().ToString("N"), EquipmentSlot.Chest, ItemTier.Mythic, EquipmentFamily.Fire,
                [new(EquipmentStat.ShieldCapacity, 0, .5f, .2f), new(EquipmentStat.ShieldDuration, 0, .5f, .2f), new(EquipmentStat.WetLightningPower, 0, .5f, .2f)], 400);
            var profile = Profile(ring) with { Items = [ring, weapon, chest], Loadout = new(Weapon: weapon.Id, Chest: chest.Id, MagicRing: ring.Id) };
            var prepared = PublicEquipment.Prepare(profile);
            PublicEquipment.Check(new(1, "Mago", WizardType.Balanced, false, prepared.View));
            var shown = RingEffects.EquipmentFor(prepared.View);
            foreach (var family in Enum.GetValues<EquipmentFamily>())
                Check(RingEffects.Numbers(family, 6, shown) == RingEffects.Numbers(family, 6, prepared.Modifiers),
                    "La ficha pública cambia las cifras de " + family);
            var replacement = ring with { Id = Guid.NewGuid().ToString("N"), Bonuses = [new(EquipmentStat.ShieldCapacity, .01f, .5f, .25f)] };
            var preview = EquipmentQuery.Preview(profile, replacement);
            Near(RingEffects.Numbers(EquipmentFamily.Fire, 0, preview.After.Modifiers).Shield, 261, "La comparación usa el anillo anterior o ignora el pecho");
        });
        test("Anillo mágico: las cifras mostradas coinciden con la absorción, daño y curación del combate", () =>
        {
            foreach (var family in new[] { EquipmentFamily.Life, EquipmentFamily.Fire, EquipmentFamily.Cold, EquipmentFamily.Lightning, EquipmentFamily.Arcane, EquipmentFamily.Earth })
            foreach (int level in new[] { 0, 6, 10 })
            {
                var world = new GameWorld(new() { Mode = GameMode.Warfare });
                var player = world.AddPlayer("Mago", 0)!; var enemy = world.AddPlayer("Rival", 1)!;
                player.Position = new(-3, 0); enemy.Position = new(-1, 0); player.Invulnerable = enemy.Invulnerable = 0;
                var ring = EquipmentCatalogue.Generate(new(81), tier: ItemTier.Silver, slot: EquipmentSlot.MagicRing, family: family) with { UpgradeLevel = level };
                world.EquipPlayer(player.Id, PublicEquipment.Prepare(Profile(ring)));
                player.Health = 500;
                var numbers = RingEffects.Numbers(family, level, player.Equipment);
                float before = player.Health, enemyBefore = enemy.Health;
                Call(world, "TickRing", player);
                Near(player.Shield, numbers.Shield, "Absorción mostrada y aplicada");
                Near(enemyBefore - enemy.Health, numbers.Damage, "Impacto mostrado y aplicado");
                Near(player.Health - before, numbers.Healing, "Curación por pulso mostrada y aplicada");
                before = player.Health; enemyBefore = enemy.Health; world.Step();
                Near(player.Health - before, numbers.Regeneration * GameWorld.StepSeconds, "Regeneración mostrada y aplicada");
                if (family == EquipmentFamily.Fire) Near(enemyBefore - enemy.Health, numbers.BurnPerSecond * GameWorld.StepSeconds, "Quemadura mostrada y aplicada");
            }
        });
    }
}
