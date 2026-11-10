namespace Arcana.Core;

public sealed record EquipmentFilter(EquipmentFamily? Family = null, ItemTier? Tier = null, EquipmentStat? Stat = null)
{
    public bool Matches(EquipmentItem item) => (Family == null || item.Family == Family) &&
        (Tier == null || item.Tier == Tier) && (Stat == null || item.Bonuses.Any(b => b.Stat == Stat && EquipmentCatalogue.BonusActive(item.Slot, b.Stat)));
}
public static class EquipmentQuery
{
    public static EquipmentItem[] Sort(IEnumerable<EquipmentItem> items, EquipmentStat? stat = null, string equippedId = "") =>
        (stat == null ? items.OrderByDescending(i => i.Id == equippedId).ThenByDescending(i => i.Tier).ThenBy(i => i.Family) :
            items.OrderByDescending(i => i.Id == equippedId).ThenByDescending(i => i.Bonuses.Any(b => b.Stat == stat && EquipmentCatalogue.BonusActive(i.Slot, b.Stat)))
                .ThenByDescending(i => i.Bonuses.FirstOrDefault(b => b.Stat == stat && EquipmentCatalogue.BonusActive(i.Slot, b.Stat))?.Current ?? 0)
                .ThenByDescending(i => i.Tier)).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();

    /// <summary>Previsualiza una sustitución con las piezas equipadas, conjuntos y límites reales de combate.</summary>
    public static EquipmentPreview Preview(AccountProfile profile, EquipmentItem candidate, IReadOnlyDictionary<string, int>? itemLevels = null)
    {
        if (!EquipmentCatalogue.Valid(candidate)) throw new ArgumentException("Objeto inválido.");
        var before = EquipmentModifiers.Summarize(profile, itemLevels);
        // Las ofertas no están en el inventario todavía. Esta copia nunca se guarda.
        var items = profile.Items.Where(i => i.Id != candidate.Id).Append(candidate).ToArray();
        var next = profile with { Items = items, Loadout = profile.Loadout.With(candidate.Slot, candidate.Id) };
        // Un inventario lleno puede consultar una oferta sin crear una pieza número 501.
        var equippedIds = Enum.GetValues<EquipmentSlot>().Select(next.Loadout.Get).ToHashSet(StringComparer.Ordinal);
        if (items.Length > EquipmentCatalogue.InventoryLimit) next = next with { Items = items.Where(i => equippedIds.Contains(i.Id)).ToArray() };
        var after = EquipmentModifiers.Summarize(next, itemLevels);
        return new(before, after);
    }

    /// <summary>Diferencias de los atributos de la pieza, sin atribuirle los efectos del conjunto.</summary>
    public static (EquipmentStat Stat, float Difference)[] Compare(EquipmentItem candidate, EquipmentItem? equipped)
    {
        var old = equipped?.Bonuses.Where(b => EquipmentCatalogue.BonusActive(equipped.Slot, b.Stat)).ToDictionary(b => b.Stat, b => b.Current) ?? [];
        var next = candidate.Bonuses.Where(b => EquipmentCatalogue.BonusActive(candidate.Slot, b.Stat)).ToDictionary(b => b.Stat, b => b.Current);
        return next.Keys.Concat(old.Keys).Distinct().OrderBy(s => s)
            .Select(s => (s, MathF.Round(next.GetValueOrDefault(s) - old.GetValueOrDefault(s), 4))).Where(v => v.Item2 != 0).ToArray();
    }
}

public sealed record EquipmentPreview(EquipmentSummary Before, EquipmentSummary After)
{
    public string Describe(bool inventoryComparison = false, bool prioritizeArcane = false)
    {
        var lines = new List<string>();
        var oldEffects = ItemAffixes.Effects(Before.Modifiers);
        var newEffects = ItemAffixes.Effects(After.Modifiers);
        if (inventoryComparison && prioritizeArcane) AddEffectChanges(arcane: true);
        var stats = prioritizeArcane ? After.Stats.OrderByDescending(s => s.Stat is EquipmentStat.ArcanePower or EquipmentStat.BeamChargeSpeed)
            : After.Stats.AsEnumerable();
        foreach (var next in stats)
        {
            var old = Before.Stats.Single(s => s.Stat == next.Stat);
            float delta = MathF.Round(next.Effective - old.Effective, 4);
            if (MathF.Abs(next.Sum - old.Sum) < .00001f && delta == 0) continue;
            string values = EquipmentCatalogue.Percent(old.Effective) + " → " + EquipmentCatalogue.Percent(next.Effective);
            string name = EquipmentCatalogue.StatName(next.Stat);
            string limit = next.Limited ? " · límite aplicado" : "";
            string change = delta > 0 ? "Ganas " : delta < 0 ? "Pierdes " : "Sin cambio efectivo en ";
            string points = delta == 0 ? "" : EquipmentCatalogue.Percent(delta).TrimEnd('%') + " pp · ";
            lines.Add(inventoryComparison
                ? change + name + " · " + points + values + limit
                : values + " · " + name + " (" + EquipmentCatalogue.Percent(delta) + ")" + limit);
        }
        foreach (var family in EquipmentCatalogue.FocusedFamilies)
        {
            int old = Before.Modifiers.Pieces(family), next = After.Modifiers.Pieces(family);
            if (old == next || old < 2 && next < 2) continue;
            lines.Add("Conjunto " + EquipmentCatalogue.FamilyName(family) + $": {old}/4 → {next}/4 · " +
                (next >= 4 ? "bono de 4" : next >= 2 ? "bono de 2" : "bono perdido"));
        }
        if (inventoryComparison) AddEffectChanges(arcane: prioritizeArcane ? false : null);
        else
        {
            string beforeEffects = string.Join('\n', oldEffects.Select(effect => effect.Description));
            string afterEffects = string.Join('\n', newEffects.Select(effect => effect.Description));
            if (beforeEffects != afterEffects)
                lines.Add("Efectos especiales al equipar:\n" + (afterEffects.Length == 0 ? "Ninguno." : afterEffects));
        }
        return lines.Count == 0 ? "Sin cambios en los atributos efectivos del equipo." : string.Join('\n', lines);

        void AddEffectChanges(bool? arcane)
        {
            foreach (var next in newEffects.Except(oldEffects))
                if (arcane == null || next.Arcane == arcane)
                    lines.Add("Ganas efecto · " + next.Name + " · " + next.Description);
            foreach (var old in oldEffects.Except(newEffects))
                if (arcane == null || old.Arcane == arcane)
                    lines.Add("Pierdes efecto · " + old.Name + " · " + old.Description);
        }
    }
}
