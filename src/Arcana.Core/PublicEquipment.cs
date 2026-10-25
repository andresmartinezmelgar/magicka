namespace Arcana.Core;

public sealed record PublicEquipmentBonus(EquipmentStat Stat, float Current);
public sealed record PublicEquipmentItem(EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family, PublicEquipmentBonus[] Bonuses)
{
    public ItemAffix? Affix { get; init; }
    public int UpgradeLevel { get; init; }
}
public sealed record PublicEquipmentSet(EquipmentFamily Family, int Pieces);
public sealed record EquipmentInspection(int PlayerId, string Name, WizardType WizardType, bool Bot, PublicEquipment Equipment);

/// <summary>Solo el equipo de la ronda actual. No incluye cuenta, inventario, oro ni identificadores de objetos.</summary>
public sealed record PublicEquipment(PublicEquipmentItem[] Items, PublicEquipmentSet[] Sets, EquipmentStatTotal[] Stats)
{
    public static PublicEquipment Empty { get; } = new([], [], Enum.GetValues<EquipmentStat>().Select(s => new EquipmentStatTotal(s, 0, 0, 0)).ToArray());

    public static PreparedEquipment Prepare(AccountProfile profile, IReadOnlyDictionary<string, int>? itemLevels = null)
    {
        var summary = EquipmentModifiers.Summarize(profile, itemLevels);
        var items = Enum.GetValues<EquipmentSlot>().Select(slot => profile.Items.FirstOrDefault(i => i.Id == profile.Loadout.Get(slot)))
            .Where(i => i != null).Select(i => new PublicEquipmentItem(i!.Slot, i.Tier, i.Family,
                i.Bonuses.Where(b => EquipmentCatalogue.BonusActive(i.Slot, b.Stat))
                    .Select(b => new PublicEquipmentBonus(b.Stat, b.Current)).ToArray())
                { Affix = ItemAffixes.Active(i.Affix, i.Slot), UpgradeLevel = itemLevels?.GetValueOrDefault(i.Id, i.UpgradeLevel) ?? i.UpgradeLevel }).ToArray();
        var sets = EquipmentCatalogue.FocusedFamilies.Where(f => summary.Modifiers.Pieces(f) > 0)
            .Select(f => new PublicEquipmentSet(f, summary.Modifiers.Pieces(f))).ToArray();
        return new(summary.Modifiers, new(items, sets, summary.Stats));
    }

    public static void Check(EquipmentInspection inspection)
    {
        if (inspection.PlayerId <= 0 || inspection.Name == null || inspection.Name.Length is < 1 or > 64 ||
            inspection.Name.Any(char.IsControl) || !Enum.IsDefined(inspection.WizardType) || inspection.Equipment == null)
            throw new InvalidDataException("Ficha de mago inválida.");
        var view = inspection.Equipment;
        if (view.Items == null || view.Items.Length > Enum.GetValues<EquipmentSlot>().Length || view.Sets == null || view.Sets.Length > 4 ||
            view.Stats == null || view.Stats.Length != Enum.GetValues<EquipmentStat>().Length)
            throw new InvalidDataException("Ficha de equipamiento inválida.");
        var slots = new HashSet<EquipmentSlot>(); var items = new float[view.Stats.Length];
        foreach (var item in view.Items)
        {
            if (item == null || !Enum.IsDefined(item.Slot) || !slots.Add(item.Slot) || !Enum.IsDefined(item.Tier) ||
                !Enum.IsDefined(item.Family) || !ItemAffixes.Valid(item.Affix, item.Tier) || item.UpgradeLevel is < 0 or > 10 ||
                item.Bonuses == null || item.Bonuses.Length > 4 || item.Bonuses.Length == 0 && item.Slot != EquipmentSlot.MagicRing)
                throw new InvalidDataException("Pieza pública inválida.");
            var bonuses = new HashSet<EquipmentStat>();
            foreach (var bonus in item.Bonuses)
            {
                if (bonus == null || !Enum.IsDefined(bonus.Stat) || !bonuses.Add(bonus.Stat) || !float.IsFinite(bonus.Current) || bonus.Current is < -0.25f or > 1)
                    throw new InvalidDataException("Bonificación pública inválida.");
                items[(int)bonus.Stat] += bonus.Current;
            }
        }
        var setStats = new float[items.Length]; var families = new HashSet<EquipmentFamily>();
        foreach (var set in view.Sets)
        {
            if (set == null || !EquipmentCatalogue.FocusedFamilies.Contains(set.Family) || !families.Add(set.Family) ||
                set.Pieces is < 1 or > 4 || set.Pieces != view.Items.Count(i => !RingEffects.IsRing(i.Slot) && i.Tier == ItemTier.Mythic && i.Family == set.Family))
                throw new InvalidDataException("Conjunto público inválido.");
            if (set.Pieces >= 2) foreach (var bonus in EquipmentCatalogue.SetBonuses(set.Family))
                setStats[(int)bonus.Stat] += set.Pieces == 4 ? bonus.FourPieces : bonus.TwoPieces;
        }
        if (view.Items.Any(i => !RingEffects.IsRing(i.Slot) && i.Tier == ItemTier.Mythic && i.Family != EquipmentFamily.Mixed && !families.Contains(i.Family)))
            throw new InvalidDataException("Falta un conjunto equipado.");
        var stats = new HashSet<EquipmentStat>();
        foreach (var stat in view.Stats)
        {
            if (stat == null || !Enum.IsDefined(stat.Stat) || !stats.Add(stat.Stat) || !float.IsFinite(stat.Items) || !float.IsFinite(stat.Set) ||
                !float.IsFinite(stat.Effective) || Math.Abs(stat.Items - items[(int)stat.Stat]) > 0.00001f ||
                Math.Abs(stat.Set - setStats[(int)stat.Stat]) > 0.00001f ||
                Math.Abs(stat.Effective - Math.Clamp(stat.Sum, -0.25f, EquipmentCatalogue.Maximum(stat.Stat))) > 0.00001f)
                throw new InvalidDataException("Totales públicos inconsistentes.");
        }
    }
}

public sealed record PreparedEquipment(EquipmentModifiers Modifiers, PublicEquipment View);
