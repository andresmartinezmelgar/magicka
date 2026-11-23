using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Arcana.Core;

/// <summary>Calculado una vez al equipar. La simulación no recorre el inventario ni consulta servicios.</summary>
public sealed record EquipmentModifiers
{
    public static EquipmentModifiers None { get; } = new();
    public float Speed { get; init; } = 1;
    public float Range { get; init; } = 1;
    public float Incoming { get; init; } = 1;
    public float Power { get; init; } = 1;
    public float Healing { get; init; } = 1;
    public float Fire { get; init; } = 1;
    public float ShieldCapacity { get; init; } = 1;
    public float ShieldDuration { get; init; } = 1;
    public float EnergyRegeneration { get; init; } = 1;
    public float LifeCost { get; init; } = 1;
    public float BurnDuration { get; init; } = 1;
    public float Water { get; init; } = 1;
    public float Cold { get; init; } = 1;
    public float Lightning { get; init; } = 1;
    public float Arcane { get; init; } = 1;
    public float Earth { get; init; } = 1;
    public float WetDuration { get; init; } = 1;
    public float ChillPower { get; init; } = 1;
    public float FreezeDuration { get; init; } = 1;
    public float WetLightningPower { get; init; } = 1;
    public float BeamChargeSpeed { get; init; } = 1;
    public float RockChargeSpeed { get; init; } = 1;
    public int BeamPierce { get; init; }
    public int LightningChains { get; init; }
    public float BurnPotency { get; init; } = 1;
    public bool WaterShieldPool { get; init; }
    public int Icebreaker { get; init; }
    public int SteamImpact { get; init; }
    public int ArcaneExecution { get; init; }
    public int ShieldBattery { get; init; }
    public int MendingWard { get; init; }
    public int ShieldReprisal { get; init; }
    public int ShieldLeech { get; init; }
    public int ShieldReflection { get; init; }
    public int RockFragmentation { get; init; }
    public int ArcaneMark { get; init; }
    public int ArcaneEcho { get; init; }
    public int AutomaticRock { get; init; }
    public int LightningStun { get; init; }
    public int WaterSlow { get; init; }
    public int ExtraGoldEvery { get; init; }
    public int GoldBonusPercent { get; init; }
    public EquipmentFamily? MagicRingFamily { get; init; }
    public int MagicRingLevel { get; init; }
    public int LifePieces { get; init; }
    public int ShieldPieces { get; init; }
    public int FirePieces { get; init; }
    public int WaterPieces { get; init; }
    public int ColdPieces { get; init; }
    public int LightningPieces { get; init; }
    public int ArcanePieces { get; init; }
    public int EarthPieces { get; init; }
    public EquipmentAppearance Appearance { get; init; } = new();
    public int Pieces(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => LifePieces, EquipmentFamily.Shield => ShieldPieces, EquipmentFamily.Fire => FirePieces,
        EquipmentFamily.Water => WaterPieces, EquipmentFamily.Cold => ColdPieces, EquipmentFamily.Lightning => LightningPieces,
        EquipmentFamily.Arcane => ArcanePieces, EquipmentFamily.Earth => EarthPieces, _ => 0
    };
    public float ElementPower(Element element) => element switch
    { Element.Water => Water, Element.Cold => Cold, Element.Lightning => Lightning, Element.Arcane => Arcane, Element.Earth => Earth, Element.Fire => Fire, _ => 1 };
    public float Bonus(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => Speed - 1, EquipmentStat.SpellRange => Range - 1, EquipmentStat.Defense => 1 - Incoming,
        EquipmentStat.Power => Power - 1, EquipmentStat.HealingPower => Healing - 1, EquipmentStat.FirePower => Fire - 1,
        EquipmentStat.ShieldCapacity => ShieldCapacity - 1, EquipmentStat.ShieldDuration => ShieldDuration - 1,
        EquipmentStat.EnergyRegeneration => EnergyRegeneration - 1, EquipmentStat.LifeEfficiency => 1 - LifeCost,
        EquipmentStat.BurnDuration => BurnDuration - 1, EquipmentStat.WaterPower => Water - 1, EquipmentStat.ColdPower => Cold - 1,
        EquipmentStat.LightningPower => Lightning - 1, EquipmentStat.ArcanePower => Arcane - 1, EquipmentStat.EarthPower => Earth - 1,
        EquipmentStat.WetDuration => WetDuration - 1, EquipmentStat.ChillPower => ChillPower - 1, EquipmentStat.FreezeDuration => FreezeDuration - 1,
        EquipmentStat.WetLightningPower => WetLightningPower - 1, EquipmentStat.BeamChargeSpeed => BeamChargeSpeed - 1,
        EquipmentStat.RockChargeSpeed => RockChargeSpeed - 1, _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };
    public static EquipmentModifiers Calculate(AccountProfile profile, IReadOnlyDictionary<string, int>? itemLevels = null) => Summarize(profile, itemLevels).Modifiers;
    public static EquipmentSummary Summarize(AccountProfile profile, IReadOnlyDictionary<string, int>? itemLevels = null)
    {
        if (profile.Items.Length > EquipmentCatalogue.InventoryLimit) throw new InvalidDataException("Inventario demasiado grande.");
        var stats = new float[Enum.GetValues<EquipmentStat>().Length];
        var setStats = new float[stats.Length];
        var sets = new int[Enum.GetValues<EquipmentFamily>().Length];
        var tiers = new ItemTier?[6];
        ulong mythicFamilies = 0;
        EquipmentOutfit outfit = default;
        var families = new int[sets.Length];
        int beamPierce = 0, lightningChains = 0, extraGoldEvery = 0, goldBonusPercent = 0;
        bool doubleBurn = false, waterPool = false;
        int icebreaker = 0, steamImpact = 0, arcaneExecution = 0, shieldBattery = 0, mendingWard = 0, shieldReprisal = 0;
        int shieldLeech = 0, shieldReflection = 0, rockFragmentation = 0;
        int arcaneMark = 0, arcaneEcho = 0;
        int automaticRock = 0, lightningStun = 0, waterSlow = 0;
        EquipmentFamily? ringFamily = null; int ringLevel = 0;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            string id = profile.Loadout.Get(slot);
            if (id.Length == 0) continue;
            var item = profile.Items.SingleOrDefault(i => i.Id == id);
            if (item == null || item.Slot != slot || !EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Equipamiento inválido o ajeno.");
            int level = itemLevels?.GetValueOrDefault(item.Id, item.UpgradeLevel) ?? item.UpgradeLevel;
            if (level is < 0 or > 10) throw new InvalidDataException("Nivel de pieza inválido.");
            tiers[(int)slot] = item.Tier;
            if (item.Tier == ItemTier.Mythic)
            {
                mythicFamilies += 1UL << ((int)item.Family * 4);
                outfit = outfit.With(slot, item.Family);
            }
            if (!RingEffects.IsRing(slot)) families[(int)item.Family]++;
            if (slot == EquipmentSlot.GoldRing)
            {
                int every = GoldBonuses.EveryForLevel(level);
                extraGoldEvery = extraGoldEvery == 0 ? every : Math.Min(extraGoldEvery, every);
            }
            if (slot == EquipmentSlot.MagicRing) { ringFamily = item.Family; ringLevel = level; }
            if (ItemAffixes.Active(item.Affix, slot) is { } affix)
                switch (affix.Kind)
                {
                    case ItemAffixKind.BeamPierce: beamPierce = Math.Max(beamPierce, affix.Value); break;
                    case ItemAffixKind.LightningChain: lightningChains = Math.Max(lightningChains, affix.Value); break;
                    case ItemAffixKind.DoubleBurn: doubleBurn = true; break;
                    case ItemAffixKind.WaterShieldPool: waterPool = true; break;
                    case ItemAffixKind.CoinDividend:
                        int every = GoldBonuses.EveryForLevel(level);
                        extraGoldEvery = extraGoldEvery == 0 ? every : Math.Min(extraGoldEvery, every);
                        break;
                    case ItemAffixKind.GoldBonus: goldBonusPercent = Math.Max(goldBonusPercent, affix.Value); break;
                    case ItemAffixKind.Icebreaker: icebreaker = Math.Max(icebreaker, affix.Value); break;
                    case ItemAffixKind.SteamImpact: steamImpact = Math.Max(steamImpact, affix.Value); break;
                    case ItemAffixKind.ArcaneExecution: arcaneExecution = Math.Max(arcaneExecution, affix.Value); break;
                    case ItemAffixKind.ShieldBattery: shieldBattery = Math.Max(shieldBattery, affix.Value); break;
                    case ItemAffixKind.MendingWard: mendingWard = Math.Max(mendingWard, affix.Value); break;
                    case ItemAffixKind.ShieldReprisal: shieldReprisal = Math.Max(shieldReprisal, affix.Value); break;
                    case ItemAffixKind.ShieldLeech: shieldLeech = Math.Max(shieldLeech, affix.Value); break;
                    case ItemAffixKind.ShieldReflection: shieldReflection = Math.Max(shieldReflection, affix.Value); break;
                    case ItemAffixKind.RockFragmentation: rockFragmentation = Math.Max(rockFragmentation, affix.Value); break;
                    case ItemAffixKind.ArcaneMark: arcaneMark = Math.Max(arcaneMark, affix.Value); break;
                    case ItemAffixKind.ArcaneEcho: arcaneEcho = Math.Max(arcaneEcho, affix.Value); break;
                    case ItemAffixKind.AutomaticRock: automaticRock = Math.Max(automaticRock, affix.Value); break;
                    case ItemAffixKind.LightningStun: lightningStun = Math.Max(lightningStun, affix.Value); break;
                    case ItemAffixKind.WaterSlow: waterSlow = Math.Max(waterSlow, affix.Value); break;
                }
            foreach (var b in item.Bonuses.Where(b => EquipmentCatalogue.BonusActive(slot, b.Stat))) stats[(int)b.Stat] += b.Current;
            if (!RingEffects.IsRing(slot) && item.Tier == ItemTier.Mythic) sets[(int)item.Family]++;
        }
        foreach (var family in EquipmentCatalogue.FocusedFamilies)
            if (sets[(int)family] >= 2)
                foreach (var bonus in EquipmentCatalogue.SetBonuses(family))
                    setStats[(int)bonus.Stat] += sets[(int)family] == 4 ? bonus.FourPieces : bonus.TwoPieces;
        float Bonus(EquipmentStat s) => Math.Clamp(stats[(int)s] + setStats[(int)s], -0.25f, EquipmentCatalogue.Maximum(s));
        var dominant = Enumerable.Range(1, families.Length - 1).OrderByDescending(i => families[i]).ThenBy(i => i).First();
        var modifiers = new EquipmentModifiers
        {
            ExtraGoldEvery = extraGoldEvery, GoldBonusPercent = goldBonusPercent,
            MagicRingFamily = ringFamily, MagicRingLevel = ringLevel,
            BeamPierce = beamPierce, LightningChains = lightningChains,
            BurnPotency = doubleBurn ? 2 : 1, WaterShieldPool = waterPool,
            Icebreaker = icebreaker, SteamImpact = steamImpact, ArcaneExecution = arcaneExecution,
            ShieldBattery = shieldBattery, MendingWard = mendingWard, ShieldReprisal = shieldReprisal,
            ShieldLeech = shieldLeech, ShieldReflection = shieldReflection, RockFragmentation = rockFragmentation,
            ArcaneMark = arcaneMark, ArcaneEcho = arcaneEcho,
            AutomaticRock = automaticRock, LightningStun = lightningStun, WaterSlow = waterSlow,
            Speed = 1 + Bonus(EquipmentStat.MovementSpeed), Range = 1 + Bonus(EquipmentStat.SpellRange),
            Incoming = 1 - Bonus(EquipmentStat.Defense), Power = 1 + Bonus(EquipmentStat.Power),
            Healing = 1 + Bonus(EquipmentStat.HealingPower), Fire = 1 + Bonus(EquipmentStat.FirePower),
            ShieldCapacity = 1 + Bonus(EquipmentStat.ShieldCapacity), ShieldDuration = 1 + Bonus(EquipmentStat.ShieldDuration),
            EnergyRegeneration = 1 + Bonus(EquipmentStat.EnergyRegeneration), LifeCost = 1 - Bonus(EquipmentStat.LifeEfficiency),
            BurnDuration = 1 + Bonus(EquipmentStat.BurnDuration), LifePieces = sets[1], ShieldPieces = sets[2], FirePieces = sets[3],
            Water = 1 + Bonus(EquipmentStat.WaterPower), Cold = 1 + Bonus(EquipmentStat.ColdPower), Lightning = 1 + Bonus(EquipmentStat.LightningPower),
            Arcane = 1 + Bonus(EquipmentStat.ArcanePower), Earth = 1 + Bonus(EquipmentStat.EarthPower), WetDuration = 1 + Bonus(EquipmentStat.WetDuration),
            ChillPower = 1 + Bonus(EquipmentStat.ChillPower), FreezeDuration = 1 + Bonus(EquipmentStat.FreezeDuration),
            WetLightningPower = 1 + Bonus(EquipmentStat.WetLightningPower), BeamChargeSpeed = 1 + Bonus(EquipmentStat.BeamChargeSpeed),
            RockChargeSpeed = 1 + Bonus(EquipmentStat.RockChargeSpeed), WaterPieces = sets[4], ColdPieces = sets[5],
            LightningPieces = sets[6], ArcanePieces = sets[7], EarthPieces = sets[8],
            Appearance = new(tiers[0], tiers[1], tiers[2], tiers[3], families[dominant] > 0 ? (EquipmentFamily)dominant : EquipmentFamily.Mixed)
            {
                GoldRing = tiers[4], MagicRing = tiers[5], MythicOutfit = outfit,
                MythicFamilies = tiers.All(t => t == ItemTier.Mythic) ? mythicFamilies : 0
            }
        };
        return new(modifiers, Enum.GetValues<EquipmentStat>().Select(s => new EquipmentStatTotal(s, stats[(int)s], setStats[(int)s], modifiers.Bonus(s))).ToArray());
    }
}
