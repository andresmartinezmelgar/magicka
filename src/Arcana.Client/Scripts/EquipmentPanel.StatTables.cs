using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private readonly record struct ItemStatRow(EquipmentStat Stat, float Current, float? Minimum, float? Maximum);

    internal static string NumberedImprovements(string text) => string.Join("\n\n", text
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select((line, index) => $"{index + 1}. {line}"));

    internal static void RenderItemStats(GridContainer table, EquipmentItem item) => RenderItemStats(table, item.Slot,
        item.Bonuses.Select(b => new ItemStatRow(b.Stat, b.Current, b.Minimum, b.Maximum)));

    internal static void RenderItemStats(GridContainer table, PublicEquipmentItem item) => RenderItemStats(table, item.Slot,
        item.Bonuses.Select(b => new ItemStatRow(b.Stat, b.Current, null, null)));

    private static void RenderItemStats(GridContainer table, EquipmentSlot slot, IEnumerable<ItemStatRow> bonuses)
    {
        Clear(table);
        bool compact = table.Columns == 2;
        var rows = bonuses.ToArray();
        Cell(table, compact && rows.Any(b => b.Minimum != null) ? "Actual / límites" : "Actual", true, header: true);
        if (!compact) Cell(table, "Límites (mín. / máx.)", true, header: true);
        Cell(table, "Atributo", header: true);
        int index = 0;
        foreach (var bonus in rows)
        {
            bool active = EquipmentCatalogue.BonusActive(slot, bonus.Stat);
            string value = active ? EquipmentCatalogue.Percent(bonus.Current) : "Inactivo";
            string limits = bonus.Minimum is { } minimum && bonus.Maximum is { } maximum
                ? EquipmentCatalogue.Percent(minimum) + " / " + EquipmentCatalogue.Percent(maximum) : "No publicados";
            if (compact && bonus.Minimum != null)
                value += "\nMín. " + EquipmentCatalogue.Percent(bonus.Minimum.Value) + "\nMáx. " + EquipmentCatalogue.Percent(bonus.Maximum!.Value);
            var applied = Cell(table, value, true, active ? bonus.Current < 0 ? Ui.Red : Ui.Blue : Ui.Muted);
            applied.SizeFlagsHorizontal = SizeFlags.Fill;
            if (!compact) Cell(table, limits, true, Ui.Muted).SizeFlagsHorizontal = SizeFlags.Fill;
            var attribute = Cell(table, $"{++index}. {StatName(bonus.Stat)}");
            attribute.TooltipText = active ? EquipmentCatalogue.StatDescription(bonus.Stat)
                : "Inactivo en esta ranura. El anillo mágico solo aporta capacidad de escudo y su habilidad elemental.";
        }
    }

    internal static string ItemEffects(EquipmentItem item, int level, EquipmentModifiers? equipment = null)
    {
        string affix = item.Affix == null || item.Slot == EquipmentSlot.GoldRing && item.Affix.Kind == ItemAffixKind.CoinDividend
            ? "" : ItemAffixes.Describe(item.Affix, level, item.Slot);
        return string.Join('\n', new[] { affix, RingEffects.Describe(item, level, equipment) }.Where(s => s.Length > 0));
    }

    internal static void AddItemEffects(VBoxContainer box, string effects, string name = "")
    {
        if (effects.Length == 0) return;
        var label = Ui.Label("EFECTOS Y MEJORAS\n" + NumberedImprovements(effects), 15, Ui.Muted);
        if (name.Length > 0) label.Name = name;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(label);
    }
}
