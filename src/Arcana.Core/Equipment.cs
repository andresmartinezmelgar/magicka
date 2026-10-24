using System.Globalization;
using System.Security.Cryptography;

namespace Arcana.Core;

public enum EquipmentSlot { Boots, Hat, Chest, Weapon, GoldRing, MagicRing }
public enum ItemTier { Silver, Gold, Platinum, Diamond, Mythic }
// Se añaden valores al final: las instancias existentes conservan sus identificadores.
public enum EquipmentFamily { Mixed = 0, Life = 1, Shield = 2, Fire = 3, Water = 4, Cold = 5, Lightning = 6, Arcane = 7, Earth = 8 }
public enum EquipmentStat { MovementSpeed, SpellRange, Defense, Power, HealingPower, FirePower, ShieldCapacity, ShieldDuration, EnergyRegeneration, LifeEfficiency, BurnDuration,
    WaterPower, ColdPower, LightningPower, ArcanePower, EarthPower, WetDuration, ChillPower, FreezeDuration, WetLightningPower, BeamChargeSpeed, RockChargeSpeed }

// Los intervalos pertenecen a la instancia: una nueva versión del catálogo no vuelve a sortear un objeto.
public sealed record ItemBonus(EquipmentStat Stat, float Minimum, float Maximum, float Current);
public sealed record EquipmentItem(string Id, EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family,
    ItemBonus[] Bonuses, int SaleGold, int GenerationVersion = 1, string SourceMatchId = "", DateTimeOffset CreatedAt = default)
{
    public ItemAffix? Affix { get; init; }
    public int UpgradeLevel { get; init; }
}
public sealed record EquipmentLoadout(string Boots = "", string Hat = "", string Chest = "", string Weapon = "", string GoldRing = "", string MagicRing = "")
{
    public string Get(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => Boots, EquipmentSlot.Hat => Hat, EquipmentSlot.Chest => Chest, EquipmentSlot.Weapon => Weapon, EquipmentSlot.GoldRing => GoldRing, EquipmentSlot.MagicRing => MagicRing, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public EquipmentLoadout With(EquipmentSlot slot, string id) => slot switch
    { EquipmentSlot.Boots => this with { Boots = id }, EquipmentSlot.Hat => this with { Hat = id }, EquipmentSlot.Chest => this with { Chest = id }, EquipmentSlot.Weapon => this with { Weapon = id },
        EquipmentSlot.GoldRing => this with { GoldRing = id }, EquipmentSlot.MagicRing => this with { MagicRing = id }, _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
}
public sealed record AccountProfile(string Uid, string Name, long Gold, int Wins, int Losses, EquipmentLoadout Loadout,
    EquipmentItem[] Items, int SchemaVersion = 1)
{
    public Dictionary<string, long> Bestiary { get; init; } = new(StringComparer.Ordinal);
}
public sealed record InventoryRequest(string ItemId, EquipmentSlot Slot = EquipmentSlot.Boots);
public sealed record InventoryResult(AccountProfile Profile, string Message);
public sealed record EquipmentAppearance(ItemTier? Boots = null, ItemTier? Hat = null, ItemTier? Chest = null, ItemTier? Weapon = null,
    EquipmentFamily Family = EquipmentFamily.Mixed);
public sealed record EquipmentStatTotal(EquipmentStat Stat, float Items, float Set, float Effective)
{
    public float Sum => Items + Set;
    public bool Limited => Math.Abs(Sum - Effective) > 0.00001f;
}
public sealed record EquipmentSummary(EquipmentModifiers Modifiers, EquipmentStatTotal[] Stats);

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
        var tiers = new ItemTier?[4];
        var families = new int[sets.Length];
        int beamPierce = 0, lightningChains = 0, extraGoldEvery = 0, goldBonusPercent = 0;
        bool doubleBurn = false, waterPool = false;
        EquipmentFamily? ringFamily = null; int ringLevel = 0;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            string id = profile.Loadout.Get(slot);
            if (id.Length == 0) continue;
            var item = profile.Items.SingleOrDefault(i => i.Id == id);
            if (item == null || item.Slot != slot || !EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Equipamiento inválido o ajeno.");
            int level = itemLevels?.GetValueOrDefault(item.Id, item.UpgradeLevel) ?? item.UpgradeLevel;
            if (level is < 0 or > 10) throw new InvalidDataException("Nivel de pieza inválido.");
            if (!RingEffects.IsRing(slot)) { tiers[(int)slot] = item.Tier; families[(int)item.Family]++; }
            if (slot == EquipmentSlot.GoldRing)
            {
                int every = GoldBonuses.EveryForLevel(level);
                extraGoldEvery = extraGoldEvery == 0 ? every : Math.Min(extraGoldEvery, every);
            }
            if (slot == EquipmentSlot.MagicRing) { ringFamily = item.Family; ringLevel = level; }
            if (item.Affix is { } affix)
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
                }
            foreach (var b in item.Bonuses) stats[(int)b.Stat] += b.Current;
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
        };
        return new(modifiers, Enum.GetValues<EquipmentStat>().Select(s => new EquipmentStatTotal(s, stats[(int)s], setStats[(int)s], modifiers.Bonus(s))).ToArray());
    }
}

public static partial class EquipmentCatalogue
{
    public const int GenerationVersion = 5, InventoryLimit = 500;
    private static readonly int[] Sales = [10, 25, 60, 150, 400];
    private static readonly float[] MainMin = [0.01f, 0.02f, 0.03f, 0.06f, 0.08f], MainMax = [0.05f, 0.07f, 0.09f, 0.11f, 0.13f];
    private static readonly float[] ExtraMin = [-0.04f, -0.03f, -0.02f, 0.04f, 0.07f], ExtraMax = [0.06f, 0.08f, 0.10f, 0.10f, 0.14f];
    private static readonly float[] RollBudget = [0.10f, 0.19f, 0.25f, 0.30f, 0.46f];
    public static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.Boots => "Botas", EquipmentSlot.Hat => "Gorro", EquipmentSlot.Chest => "Pecho", EquipmentSlot.Weapon => "Arma",
        EquipmentSlot.GoldRing => "Anillo de oro", EquipmentSlot.MagicRing => "Anillo mágico", _ => throw new ArgumentOutOfRangeException(nameof(slot)) };
    public static string TierName(ItemTier tier) => tier switch { ItemTier.Silver => "Plata", ItemTier.Gold => "Oro", ItemTier.Platinum => "Platino", ItemTier.Diamond => "Diamante", _ => "Mítico" };
    public static string FamilyName(EquipmentFamily family) => family switch
    { EquipmentFamily.Life => "Vida", EquipmentFamily.Shield => "Escudo", EquipmentFamily.Fire => "Fuego", EquipmentFamily.Water => "Agua",
        EquipmentFamily.Cold => "Frío", EquipmentFamily.Lightning => "Rayo", EquipmentFamily.Arcane => "Arcano", EquipmentFamily.Earth => "Tierra", _ => "Mixto" };
    public static string StatName(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => "Velocidad", EquipmentStat.SpellRange => "Alcance", EquipmentStat.Defense => "Defensa",
        EquipmentStat.Power => "Potencia de ataque", EquipmentStat.HealingPower => "Curación", EquipmentStat.FirePower => "Daño de fuego",
        EquipmentStat.ShieldCapacity => "Absorción del escudo", EquipmentStat.ShieldDuration => "Duración del escudo",
        EquipmentStat.EnergyRegeneration => "Regeneración de energía", EquipmentStat.LifeEfficiency => "Ahorro de energía al reanimar",
        EquipmentStat.BurnDuration => "Duración de quemaduras", EquipmentStat.WaterPower => "Potencia de agua", EquipmentStat.ColdPower => "Daño de frío",
        EquipmentStat.LightningPower => "Daño de rayo", EquipmentStat.ArcanePower => "Daño arcano", EquipmentStat.EarthPower => "Daño de tierra",
        EquipmentStat.WetDuration => "Duración de mojado", EquipmentStat.ChillPower => "Potencia de enfriamiento", EquipmentStat.FreezeDuration => "Duración de congelación",
        EquipmentStat.WetLightningPower => "Rayo contra mojados", EquipmentStat.BeamChargeSpeed => "Velocidad de carga del haz", EquipmentStat.RockChargeSpeed => "Velocidad de carga de rocas",
        _ => throw new ArgumentOutOfRangeException(nameof(stat))
    };
    public static string Percent(float value) => MathF.Round(value * 100, 2).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "%";
    public static string ItemName(EquipmentItem item) => SlotName(item.Slot) + " · " + TierName(item.Tier) + " · " + FamilyName(item.Family);
    public static EquipmentStat MainStat(EquipmentSlot slot, EquipmentFamily family) => slot switch
    {
        EquipmentSlot.Boots => EquipmentStat.MovementSpeed, EquipmentSlot.Hat => EquipmentStat.SpellRange, EquipmentSlot.Chest => EquipmentStat.Defense,
        EquipmentSlot.GoldRing => EquipmentStat.EnergyRegeneration, EquipmentSlot.MagicRing => EquipmentStat.ShieldCapacity,
        _ => family switch
        {
            EquipmentFamily.Life => EquipmentStat.HealingPower, EquipmentFamily.Shield => EquipmentStat.ShieldCapacity, EquipmentFamily.Fire => EquipmentStat.FirePower,
            EquipmentFamily.Water => EquipmentStat.WaterPower, EquipmentFamily.Cold => EquipmentStat.ColdPower, EquipmentFamily.Lightning => EquipmentStat.LightningPower,
            EquipmentFamily.Arcane => EquipmentStat.ArcanePower, EquipmentFamily.Earth => EquipmentStat.EarthPower, _ => EquipmentStat.Power
        }
    };
    public static EquipmentStat[] Pool(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => [EquipmentStat.HealingPower, EquipmentStat.EnergyRegeneration, EquipmentStat.LifeEfficiency, EquipmentStat.ShieldDuration],
        EquipmentFamily.Shield => [EquipmentStat.ShieldCapacity, EquipmentStat.ShieldDuration, EquipmentStat.Defense, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Fire => [EquipmentStat.FirePower, EquipmentStat.BurnDuration, EquipmentStat.Power, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Water => [EquipmentStat.WaterPower, EquipmentStat.WetDuration, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Cold => [EquipmentStat.ColdPower, EquipmentStat.ChillPower, EquipmentStat.FreezeDuration, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Lightning => [EquipmentStat.LightningPower, EquipmentStat.WetLightningPower, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Arcane => [EquipmentStat.ArcanePower, EquipmentStat.BeamChargeSpeed, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration],
        EquipmentFamily.Earth => [EquipmentStat.EarthPower, EquipmentStat.RockChargeSpeed, EquipmentStat.Defense, EquipmentStat.ShieldCapacity],
        _ => Enum.GetValues<EquipmentStat>()
    };
    public static IEnumerable<(string Id, object Data)> Definitions()
    {
        foreach (var stat in Enum.GetValues<EquipmentStat>())
            yield return (Protocol.Json.PropertyNamingPolicy!.ConvertName(stat.ToString()), new
            {
                stat, generationVersion = GenerationVersion, weight = 1,
                primarySlots = Enum.GetValues<EquipmentSlot>().Where(slot => Enum.GetValues<EquipmentFamily>().Any(family => MainStat(slot, family) == stat)).ToArray(),
                families = Enum.GetValues<EquipmentFamily>().Where(family => Pool(family).Contains(stat)).ToArray(),
                tiers = Enum.GetValues<ItemTier>().Select(tier => new { tier, primaryMinimum = MainMin[(int)tier], primaryMaximum = MainMax[(int)tier],
                    minimum = ExtraMin[(int)tier], maximum = ExtraMax[(int)tier], initialBudget = RollBudget[(int)tier] }).ToArray()
            });
    }
    public static EquipmentItem Generate(Random random, string sourceMatchId = "", ItemTier? tier = null, EquipmentSlot? slot = null,
        EquipmentFamily? family = null, string? id = null)
    {
        int roll = random.Next(100);
        var quality = tier ?? TierForRoll(roll);
        var position = slot ?? (EquipmentSlot)random.Next(Enum.GetValues<EquipmentSlot>().Length);
        if (family.HasValue && !Enum.IsDefined(family.Value)) throw new ArgumentException("Familia de objeto inválida.");
        var identity = position == EquipmentSlot.MagicRing ? family ?? (EquipmentFamily)random.Next(Enum.GetValues<EquipmentFamily>().Length)
            : quality >= ItemTier.Diamond ? family.HasValue && family != EquipmentFamily.Mixed ? family.Value
            : FocusedFamilies[random.Next(FocusedFamilies.Count)] : EquipmentFamily.Mixed;
        if (!Enum.IsDefined(quality) || !Enum.IsDefined(position)) throw new ArgumentException("Tipo de objeto inválido.");
        var primary = MainStat(position, identity);
        float Value(float min, float max) => MathF.Round(min + (float)random.NextDouble() * (max - min), 4);
        int t = (int)quality;
        var bonuses = new List<ItemBonus> { new(primary, MainMin[t], MainMax[t], Value(MainMin[t], MainMax[t])) };
        var choices = Pool(identity).Where(s => s != primary).ToList();
        int extra = quality == ItemTier.Silver ? 1 : quality == ItemTier.Mythic ? 3 : 2;
        for (int n = 0; n < extra && choices.Count > 0; n++)
        {
            int pick = random.Next(choices.Count); var stat = choices[pick]; choices.RemoveAt(pick);
            bonuses.Add(new(stat, ExtraMin[t], ExtraMax[t], Value(ExtraMin[t], ExtraMax[t])));
        }
        float excess = bonuses.Sum(b => Math.Max(0, b.Current)) - RollBudget[t];
        for (int n = 1; n < bonuses.Count && excess > 0.0001f; n++)
        {
            var b = bonuses[n]; float decrease = Math.Min(excess, Math.Max(0, b.Current - Math.Max(0, b.Minimum)));
            bonuses[n] = b with { Current = MathF.Round(b.Current - decrease, 4) }; excess -= decrease;
        }
        if (RingEffects.IsRing(position))
            bonuses = bonuses.Select(b => b with { Maximum = MathF.Round(b.Current + 0.10f, 4) }).ToList();
        return new(id ?? Guid.NewGuid().ToString("N"), position, quality, identity, bonuses.ToArray(), Sales[t], GenerationVersion, sourceMatchId, DateTimeOffset.UtcNow)
        { Affix = ItemAffixes.Roll(random, quality) };
    }
    public static int TierChance(ItemTier tier) => tier switch
    { ItemTier.Silver => 48, ItemTier.Gold => 27, ItemTier.Platinum => 15, ItemTier.Diamond => 8, ItemTier.Mythic => 2, _ => 0 };
    private static ItemTier TierForRoll(int roll)
    {
        foreach (var tier in Enum.GetValues<ItemTier>())
        { roll -= TierChance(tier); if (roll < 0) return tier; }
        throw new ArgumentOutOfRangeException(nameof(roll));
    }
    public static bool Valid(EquipmentItem item) => item != null && Guid.TryParseExact(item.Id, "N", out _) &&
        item.UpgradeLevel is >= 0 and <= 10 && Enum.IsDefined(item.Slot) && Enum.IsDefined(item.Tier) && Enum.IsDefined(item.Family) && item.GenerationVersion > 0 &&
        ItemAffixes.Valid(item.Affix, item.Tier) &&
        item.SaleGold is > 0 and <= 100000 && item.Bonuses is { Length: >= 1 and <= 4 } && item.Bonuses.All(b => b != null) &&
        item.Bonuses.Select(b => b.Stat).Distinct().Count() == item.Bonuses.Length && item.Bonuses.Any(b => b.Current > 0) &&
        item.Bonuses.All(b => Enum.IsDefined(b.Stat) && float.IsFinite(b.Minimum) && float.IsFinite(b.Maximum) && float.IsFinite(b.Current) &&
            b.Minimum >= -0.50f && b.Maximum <= 0.50f && b.Minimum <= b.Maximum && b.Current >= b.Minimum && b.Current <= b.Maximum);
}

public sealed class LootRandom : Random
{
    public override int Next(int maxValue) => RandomNumberGenerator.GetInt32(maxValue);
    public override int Next(int minValue, int maxValue) => RandomNumberGenerator.GetInt32(minValue, maxValue);
    public override double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8]; RandomNumberGenerator.Fill(bytes);
        return (BitConverter.ToUInt64(bytes) >> 11) / 9007199254740992d;
    }
}
