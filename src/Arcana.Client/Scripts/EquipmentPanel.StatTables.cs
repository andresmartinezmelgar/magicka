using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private readonly record struct ItemStatRow(EquipmentStat Stat, float Current, float? Maximum);

    internal static string NumberedImprovements(string text) => string.Join("\n\n", text
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select((line, index) => $"{index + 1}. {line}"));

    internal static void RenderItemStats(GridContainer table, EquipmentItem item) => RenderItemStats(table, item.Slot,
        item.Bonuses.Select(b => new ItemStatRow(b.Stat, b.Current, b.Maximum)));

    internal static void RenderItemStats(GridContainer table, PublicEquipmentItem item) => RenderItemStats(table, item.Slot,
        item.Bonuses.Select(b => new ItemStatRow(b.Stat, b.Current, null)));

    private static void RenderItemStats(GridContainer table, EquipmentSlot slot, IEnumerable<ItemStatRow> bonuses)
    {
        Clear(table);
        table.Columns = 2;
        var rows = bonuses.ToArray();
        Cell(table, rows.Any(b => b.Maximum != null) ? "Actual (máx.)" : "Actual", true, header: true);
        Cell(table, "Atributo", header: true);
        int index = 0;
        foreach (var bonus in rows)
        {
            bool active = EquipmentCatalogue.BonusActive(slot, bonus.Stat);
            string value = active ? EquipmentCatalogue.Percent(bonus.Current) : "Inactivo";
            if (bonus.Maximum is { } maximum)
                value += " (Max " + MathF.Round(maximum * 100, 2).ToString("0.##", CultureInfo.InvariantCulture) + ")";
            var applied = Cell(table, value, true, active ? bonus.Current < 0 ? Ui.Red : Ui.Blue : Ui.Muted);
            applied.SizeFlagsHorizontal = SizeFlags.Fill;
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
