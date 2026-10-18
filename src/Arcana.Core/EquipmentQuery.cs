namespace Arcana.Core;

public sealed record EquipmentFilter(EquipmentFamily? Family = null, ItemTier? Tier = null, EquipmentStat? Stat = null)
{
    public bool Matches(EquipmentItem item) => (Family == null || item.Family == Family) &&
        (Tier == null || item.Tier == Tier) && (Stat == null || item.Bonuses.Any(b => b.Stat == Stat));
}
public static class EquipmentQuery
{
    public static EquipmentItem[] Sort(IEnumerable<EquipmentItem> items, EquipmentStat? stat = null) =>
        (stat == null ? items.OrderByDescending(i => i.Tier).ThenBy(i => i.Family) :
            items.OrderByDescending(i => i.Bonuses.Any(b => b.Stat == stat))
                .ThenByDescending(i => i.Bonuses.FirstOrDefault(b => b.Stat == stat)?.Current ?? 0)
                .ThenByDescending(i => i.Tier)).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();

    /// <summary>Previsualiza una sustitución con los cuatro objetos, conjuntos y límites reales de combate.</summary>
    public static EquipmentPreview Preview(AccountProfile profile, EquipmentItem candidate)
    {
        if (!EquipmentCatalogue.Valid(candidate)) throw new ArgumentException("Objeto inválido.");
        var before = EquipmentModifiers.Summarize(profile);
        // Las ofertas no están en el inventario todavía. Esta copia nunca se guarda.
        var items = profile.Items.Where(i => i.Id != candidate.Id).Append(candidate).ToArray();
        var next = profile with { Items = items, Loadout = profile.Loadout.With(candidate.Slot, candidate.Id) };
        // Un inventario lleno puede consultar una oferta sin crear una pieza número 501.
        var equippedIds = Enum.GetValues<EquipmentSlot>().Select(next.Loadout.Get).ToHashSet(StringComparer.Ordinal);
        if (items.Length > EquipmentCatalogue.InventoryLimit) next = next with { Items = items.Where(i => equippedIds.Contains(i.Id)).ToArray() };
        var after = EquipmentModifiers.Summarize(next);
        return new(before, after);
    }

    /// <summary>Diferencias de los atributos de la pieza, sin atribuirle los efectos del conjunto.</summary>
    public static (EquipmentStat Stat, float Difference)[] Compare(EquipmentItem candidate, EquipmentItem? equipped)
    {
        var old = equipped?.Bonuses.ToDictionary(b => b.Stat, b => b.Current) ?? [];
        var next = candidate.Bonuses.ToDictionary(b => b.Stat, b => b.Current);
        return next.Keys.Concat(old.Keys).Distinct().OrderBy(s => s)
            .Select(s => (s, MathF.Round(next.GetValueOrDefault(s) - old.GetValueOrDefault(s), 4))).Where(v => v.Item2 != 0).ToArray();
    }
}

public sealed record EquipmentPreview(EquipmentSummary Before, EquipmentSummary After)
{
    public string Describe()
    {
        var lines = new List<string>();
        foreach (var next in After.Stats)
        {
            var old = Before.Stats.Single(s => s.Stat == next.Stat);
            float delta = MathF.Round(next.Effective - old.Effective, 4);
            if (MathF.Abs(next.Sum - old.Sum) < .00001f && delta == 0) continue;
            lines.Add(EquipmentCatalogue.Percent(old.Effective) + " → " + EquipmentCatalogue.Percent(next.Effective) + " · " +
                EquipmentCatalogue.StatName(next.Stat) + " (" + EquipmentCatalogue.Percent(delta) + ")" +
                (next.Limited ? " · límite aplicado" : ""));
        }
        foreach (var family in EquipmentCatalogue.FocusedFamilies)
        {
            int old = Before.Modifiers.Pieces(family), next = After.Modifiers.Pieces(family);
            if (old == next || old < 2 && next < 2) continue;
            lines.Add("Conjunto " + EquipmentCatalogue.FamilyName(family) + $": {old}/4 → {next}/4 · " +
                (next >= 4 ? "bono de 4" : next >= 2 ? "bono de 2" : "bono perdido"));
        }
        return lines.Count == 0 ? "Sin cambios en los atributos efectivos del equipo." : string.Join('\n', lines);
    }
}
