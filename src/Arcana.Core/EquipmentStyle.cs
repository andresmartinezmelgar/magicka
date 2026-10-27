using System.Text.Json.Serialization;

namespace Arcana.Core;

/// <summary>El estilo ignora las tiradas de estadísticas, el afijo y las mejoras.</summary>
public readonly record struct EquipmentStyle(EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family)
{
    public static readonly int Limit = Enum.GetValues<EquipmentSlot>().Length * Enum.GetValues<ItemTier>().Length * Enum.GetValues<EquipmentFamily>().Length;
    [JsonIgnore] public bool Valid => Enum.IsDefined(Slot) && Enum.IsDefined(Tier) && Enum.IsDefined(Family);
    public static EquipmentStyle Of(EquipmentItem item) => new(item.Slot, item.Tier, item.Family);
    public bool Matches(EquipmentItem item) => this == Of(item);
    public bool SameSlot(EquipmentStyle other) => Slot == other.Slot;
    public bool MatchesLowerQuality(EquipmentItem item) => Slot == item.Slot && item.Tier < Tier;
    public string Describe() => EquipmentCatalogue.SlotName(Slot) + " · " + EquipmentCatalogue.TierName(Tier) + " · " + EquipmentCatalogue.FamilyName(Family);
    public string DescribeAutoSell() => EquipmentCatalogue.SlotName(Slot) + " · calidad inferior a " + EquipmentCatalogue.TierName(Tier) + " · cualquier elemento";

    public static EquipmentStyle[] SlotLimits(IEnumerable<EquipmentStyle> styles) => styles
        .Where(s => s.Valid && s.Tier > ItemTier.Silver).OrderByDescending(s => s.Tier).ThenBy(s => s.Family)
        .DistinctBy(s => s.Slot).OrderBy(s => s.Slot).ToArray();
}

public sealed record AutoSellRule(EquipmentStyle Style, HashSet<string> ExistingIds);
