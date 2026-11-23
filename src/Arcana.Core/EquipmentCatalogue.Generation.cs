using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public static partial class EquipmentCatalogue
{
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
        EquipmentFamily.Life => [EquipmentStat.HealingPower, EquipmentStat.EnergyRegeneration, EquipmentStat.LifeEfficiency, EquipmentStat.ShieldDuration, EquipmentStat.Defense],
        EquipmentFamily.Shield => [EquipmentStat.ShieldCapacity, EquipmentStat.ShieldDuration, EquipmentStat.Defense, EquipmentStat.EnergyRegeneration, EquipmentStat.Power],
        EquipmentFamily.Fire => [EquipmentStat.FirePower, EquipmentStat.BurnDuration, EquipmentStat.Power, EquipmentStat.EnergyRegeneration, EquipmentStat.SpellRange],
        EquipmentFamily.Water => [EquipmentStat.WaterPower, EquipmentStat.WetDuration, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration, EquipmentStat.Power],
        EquipmentFamily.Cold => [EquipmentStat.ColdPower, EquipmentStat.ChillPower, EquipmentStat.FreezeDuration, EquipmentStat.EnergyRegeneration, EquipmentStat.SpellRange],
        EquipmentFamily.Lightning => [EquipmentStat.LightningPower, EquipmentStat.WetLightningPower, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration, EquipmentStat.Power],
        EquipmentFamily.Arcane => [EquipmentStat.ArcanePower, EquipmentStat.BeamChargeSpeed, EquipmentStat.SpellRange, EquipmentStat.EnergyRegeneration, EquipmentStat.Power],
        EquipmentFamily.Earth => [EquipmentStat.EarthPower, EquipmentStat.RockChargeSpeed, EquipmentStat.Defense, EquipmentStat.ShieldCapacity],
        _ => Enum.GetValues<EquipmentStat>()
    };

    public static IEnumerable<(string Id, object Data)> Definitions()
    {
        foreach (var stat in Enum.GetValues<EquipmentStat>())
            yield return (Protocol.Json.PropertyNamingPolicy!.ConvertName(stat.ToString()), new
            {
                stat, generationVersion = GenerationVersion, weight = stat == EquipmentStat.EnergyRegeneration ? 1 : StandardSecondaryWeight,
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
        int extra = position == EquipmentSlot.MagicRing ? 0 : quality == ItemTier.Silver ? 1 : quality == ItemTier.Mythic ? 3 : 2;
        for (int n = 0; n < extra && choices.Count > 0; n++)
        {
            int pick = RollSecondary(random, choices); var stat = choices[pick]; choices.RemoveAt(pick);
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
        { Affix = ItemAffixes.Roll(random, quality, position) };
    }

    private static int RollSecondary(Random random, List<EquipmentStat> choices)
    {
        int energyIndex = choices.IndexOf(EquipmentStat.EnergyRegeneration);
        if (energyIndex < 0) return random.Next(choices.Count);
        // Una papeleta para energía y ocho para cada otro atributo disponible.
        int roll = random.Next(1 + (choices.Count - 1) * StandardSecondaryWeight);
        if (roll == 0) return energyIndex;
        int otherIndex = (roll - 1) / StandardSecondaryWeight;
        return otherIndex < energyIndex ? otherIndex : otherIndex + 1;
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
        ItemAffixes.Valid(item.Affix, item.Tier, item.Slot, item.GenerationVersion) &&
        item.SaleGold is > 0 and <= 100000 && item.Bonuses is { Length: >= 1 and <= 4 } && item.Bonuses.All(b => b != null) &&
        (item.Slot != EquipmentSlot.MagicRing || item.GenerationVersion < 7 ||
            item.Bonuses is [{ Stat: EquipmentStat.ShieldCapacity }]) &&
        item.Bonuses.Select(b => b.Stat).Distinct().Count() == item.Bonuses.Length && item.Bonuses.Any(b => b.Current > 0) &&
        item.Bonuses.All(b => Enum.IsDefined(b.Stat) && float.IsFinite(b.Minimum) && float.IsFinite(b.Maximum) && float.IsFinite(b.Current) &&
            b.Minimum >= -0.50f && b.Maximum <= 0.50f && b.Minimum <= b.Maximum && b.Current >= b.Minimum && b.Current <= b.Maximum);
}
