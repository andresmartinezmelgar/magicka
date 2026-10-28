namespace Arcana.Core;

public sealed record SurvivalEquipmentSetup
{
    public const int MaximumPriorities = 5;
    public bool Enabled { get; init; }
    public Element Element { get; init; } = Element.Life;
    public ItemAffixKind[] Priorities { get; init; } = [];
    public bool Valid => Enum.IsDefined(Element) && Priorities is { Length: <= MaximumPriorities } &&
        Priorities.All(k => Enum.IsDefined(k)) && Priorities.Distinct().Count() == Priorities.Length;
    public SurvivalEquipmentSetup Copy() => this with { Priorities = Priorities.ToArray() };
    public bool SameAs(SurvivalEquipmentSetup? other) => other != null && Enabled == other.Enabled && Element == other.Element &&
        Priorities.AsSpan().SequenceEqual(other.Priorities);
}

/// <summary>Busca la combinación completa de seis ranuras; incluye conjuntos y afijos sin duplicarlos.</summary>
public static class SurvivalEquipmentSelection
{
    public static EquipmentLoadout Select(IReadOnlyList<EquipmentItem> items, SurvivalEquipmentSetup setup, EquipmentLoadout current,
        IReadOnlyDictionary<string, int>? upgrades = null)
    {
        if (!setup.Valid || items.Count > DefenseRules.InventoryLimit + 1 || items.Any(i => !EquipmentCatalogue.Valid(i)) ||
            items.Select(i => i.Id).Distinct().Count() != items.Count) throw new ArgumentException("Selección automática inválida.");
        var slots = Enum.GetValues<EquipmentSlot>();
        var choices = slots.Select(slot => items.Where(i => i.Slot == slot).OrderBy(i => i.Id, StringComparer.Ordinal).Cast<EquipmentItem?>().ToArray()).ToArray();
        for (int i = 0; i < choices.Length; i++) if (choices[i].Length == 0) choices[i] = [null];
        var selected = new EquipmentItem?[slots.Length]; var best = new EquipmentItem?[slots.Length];
        var stats = new double[Enum.GetValues<EquipmentStat>().Length]; var sets = new int[Enum.GetValues<EquipmentFamily>().Length];
        int bestMask = -1, bestRing = -1, bestQuality = -1, bestRetained = -1;
        double bestPower = double.NegativeInfinity, bestUtility = double.NegativeInfinity;
        EquipmentFamily family = setup.Element switch
        { Element.Life => EquipmentFamily.Life, Element.Shield => EquipmentFamily.Shield, Element.Water => EquipmentFamily.Water,
            Element.Cold => EquipmentFamily.Cold, Element.Lightning => EquipmentFamily.Lightning, Element.Arcane => EquipmentFamily.Arcane,
            Element.Earth => EquipmentFamily.Earth, _ => EquipmentFamily.Fire };
        void Search(int index, int mask, int quality, int retained)
        {
            if (index < slots.Length)
            {
                foreach (var item in choices[index])
                {
                    selected[index] = item;
                    if (item == null) { Search(index + 1, mask, quality, retained); continue; }
                    foreach (var b in item.Bonuses) if (EquipmentCatalogue.BonusActive(item.Slot, b.Stat)) stats[(int)b.Stat] += b.Current;
                    bool setPiece = !RingEffects.IsRing(item.Slot) && item.Tier == ItemTier.Mythic;
                    if (setPiece) sets[(int)item.Family]++;
                    int nextMask = mask;
                    if (item.Tier == ItemTier.Mythic && ItemAffixes.Active(item.Affix, item.Slot) is { } affix)
                        for (int p = 0; p < setup.Priorities.Length; p++)
                            if (affix.Kind == setup.Priorities[p])
                            {
                                int shift = (SurvivalEquipmentSetup.MaximumPriorities - p - 1) * 2;
                                int rank = affix.Kind == ItemAffixKind.GoldBonus ? affix.Value / 10 : affix.Value;
                                int value = Math.Max((nextMask >> shift) & 3, Math.Min(3, rank));
                                nextMask = (nextMask & ~(3 << shift)) | (value << shift);
                            }
                    Search(index + 1, nextMask, quality + (int)item.Tier, retained + (current.Get(item.Slot) == item.Id ? 1 : 0));
                    if (setPiece) sets[(int)item.Family]--;
                    foreach (var b in item.Bonuses) if (EquipmentCatalogue.BonusActive(item.Slot, b.Stat)) stats[(int)b.Stat] -= b.Current;
                }
                return;
            }
            if (mask < bestMask) return;
            double Bonus(EquipmentStat stat)
            {
                double value = stats[(int)stat];
                for (int f = 1; f < sets.Length; f++) if (sets[f] >= 2)
                    foreach (var bonus in EquipmentCatalogue.SetBonuses((EquipmentFamily)f)) if (bonus.Stat == stat)
                        value += sets[f] >= 4 ? bonus.FourPieces : bonus.TwoPieces;
                return Math.Clamp(value, -.25, EquipmentCatalogue.Maximum(stat));
            }
            EquipmentStat elemental = setup.Element switch
            { Element.Life => EquipmentStat.HealingPower, Element.Shield => EquipmentStat.ShieldCapacity, Element.Water => EquipmentStat.WaterPower,
                Element.Cold => EquipmentStat.ColdPower, Element.Lightning => EquipmentStat.LightningPower, Element.Arcane => EquipmentStat.ArcanePower,
                Element.Earth => EquipmentStat.EarthPower, _ => EquipmentStat.FirePower };
            double power = Math.Round((1 + Bonus(elemental)) * (setup.Element is Element.Life or Element.Shield ? 1 : 1 + Bonus(EquipmentStat.Power)), 8);
            double utility = Bonus(EquipmentStat.SpellRange) + Bonus(EquipmentStat.EnergyRegeneration) + (setup.Element switch
            { Element.Life => Bonus(EquipmentStat.LifeEfficiency), Element.Shield => Bonus(EquipmentStat.ShieldDuration) + Bonus(EquipmentStat.Defense),
                Element.Fire => Bonus(EquipmentStat.BurnDuration), Element.Water => Bonus(EquipmentStat.WetDuration),
                Element.Cold => Bonus(EquipmentStat.ChillPower) + Bonus(EquipmentStat.FreezeDuration),
                Element.Lightning => Bonus(EquipmentStat.WetLightningPower), Element.Arcane => Bonus(EquipmentStat.BeamChargeSpeed),
                _ => Bonus(EquipmentStat.RockChargeSpeed) });
            utility = Math.Round(utility, 8);
            var magicRing = selected[(int)EquipmentSlot.MagicRing];
            int ring = magicRing?.Family == family ? 1 + (upgrades?.GetValueOrDefault(magicRing.Id, magicRing.UpgradeLevel) ?? magicRing.UpgradeLevel) : 0;
            bool better = mask > bestMask || power > bestPower || power == bestPower && (ring > bestRing || ring == bestRing &&
                (utility > bestUtility || utility == bestUtility && (quality > bestQuality || quality == bestQuality && retained > bestRetained)));
            if (!better) return;
            bestMask = mask; bestPower = power; bestRing = ring; bestUtility = utility; bestQuality = quality; bestRetained = retained;
            Array.Copy(selected, best, selected.Length);
        }
        Search(0, 0, 0, 0);
        var result = new EquipmentLoadout();
        foreach (var item in best) if (item != null) result = result.With(item.Slot, item.Id);
        return result;
    }
}
