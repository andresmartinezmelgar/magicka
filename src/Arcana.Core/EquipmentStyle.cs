using System.Text.Json.Serialization;

namespace Arcana.Core;

/// <summary>El estilo ignora las tiradas de estadísticas, el afijo y las mejoras.</summary>
public readonly record struct EquipmentStyle(EquipmentSlot Slot, ItemTier Tier, EquipmentFamily Family)
{
    public static readonly int Limit = Enum.GetValues<EquipmentSlot>().Length * Enum.GetValues<ItemTier>().Length * Enum.GetValues<EquipmentFamily>().Length;
    [JsonIgnore] public bool Valid => Enum.IsDefined(Slot) && Enum.IsDefined(Tier) && Enum.IsDefined(Family);
    public static EquipmentStyle Of(EquipmentItem item) => new(item.Slot, item.Tier, item.Family);
    public bool Matches(EquipmentItem item) => this == Of(item);
    public bool SameKind(EquipmentStyle other) => Slot == other.Slot && Family == other.Family;
    public bool MatchesLowerQuality(EquipmentItem item) => Slot == item.Slot && Family == item.Family && item.Tier < Tier;
    public string Describe() => EquipmentCatalogue.SlotName(Slot) + " · " + EquipmentCatalogue.TierName(Tier) + " · " + EquipmentCatalogue.FamilyName(Family);
    public string DescribeAutoSell() => EquipmentCatalogue.SlotName(Slot) + " · " + EquipmentCatalogue.FamilyName(Family) +
        " · calidad inferior a " + EquipmentCatalogue.TierName(Tier);
}

public sealed record AutoSellRule(EquipmentStyle Style, HashSet<string> ExistingIds);
