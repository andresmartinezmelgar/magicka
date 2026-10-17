namespace Arcana.Core;

public sealed record EquipmentFilter(EquipmentFamily? Family = null, ItemTier? Tier = null, EquipmentStat? Stat = null)
{
    public bool Matches(EquipmentItem item) => (Family == null || item.Family == Family) &&
        (Tier == null || item.Tier == Tier) && (Stat == null || item.Bonuses.Any(b => b.Stat == Stat));
}
public static class EquipmentQuery
{
    /// <summary>Diferencias de los atributos de la pieza, sin atribuirle los efectos del conjunto.</summary>
    public static (EquipmentStat Stat, float Difference)[] Compare(EquipmentItem candidate, EquipmentItem? equipped)
    {
        var old = equipped?.Bonuses.ToDictionary(b => b.Stat, b => b.Current) ?? [];
        var next = candidate.Bonuses.ToDictionary(b => b.Stat, b => b.Current);
        return next.Keys.Concat(old.Keys).Distinct().OrderBy(s => s)
            .Select(s => (s, MathF.Round(next.GetValueOrDefault(s) - old.GetValueOrDefault(s), 4))).Where(v => v.Item2 != 0).ToArray();
    }
}
