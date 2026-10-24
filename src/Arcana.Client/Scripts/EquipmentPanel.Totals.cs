using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    internal static bool RenderTotals(GridContainer table, IEnumerable<EquipmentStatTotal> stats,
        IReadOnlyList<PublicEquipmentItem> equipped)
    {
        Clear(table);
        var active = stats.Where(s => Math.Abs(s.Effective) > 0.00001f).OrderByDescending(s => s.Effective).ThenBy(s => s.Stat).ToArray();
        table.Visible = active.Length > 0;
        if (!table.Visible) return false;
        bool compact = table.Columns == 2;
        bool hasRings = equipped.Any(i => RingEffects.IsRing(i.Slot));
        var slots = Enum.GetValues<EquipmentSlot>();
        if (compact) Cell(table, "Aplicado", true, header: true);
        else
        {
            foreach (var slot in slots) Cell(table, EquipmentCatalogue.SlotName(slot), true, header: true);
            foreach (string heading in new[] { "Conjunto", "Suma", "Aplicado" }) Cell(table, heading, true, header: true);
        }
        Cell(table, "Atributo", header: true);
        foreach (var stat in active)
        {
            var lines = new List<string>();
            foreach (var slot in slots)
            {
                // Las dos posiciones nuevas vacías no añaden una línea alta en móvil.
                if (compact && !hasRings && RingEffects.IsRing(slot)) continue;
                var piece = equipped.FirstOrDefault(i => i.Slot == slot);
                float value = piece?.Bonuses.Where(b => b.Stat == stat.Stat).Sum(b => b.Current) ?? 0;
                string amount = piece == null ? "—" : EquipmentCatalogue.Percent(value);
                if (compact) lines.Add(EquipmentCatalogue.SlotName(slot) + ": " + amount);
                else Cell(table, amount, true, value < 0 ? Ui.Red : value > 0 ? Ui.Text : Ui.Muted);
            }
            if (compact)
            {
                var pairs = new List<string>();
                for (int i = 0; i < lines.Count; i += 2)
                    pairs.Add(lines[i] + (i + 1 < lines.Count ? " · " + lines[i + 1] : ""));
                lines = pairs;
                lines.Add("Conjunto: " + EquipmentCatalogue.Percent(stat.Set) + " · Suma: " + EquipmentCatalogue.Percent(stat.Sum));
                lines.Add("Aplicado: " + EquipmentCatalogue.Percent(stat.Effective));
            }
            else
            {
                Cell(table, EquipmentCatalogue.Percent(stat.Set), true, stat.Set != 0 ? Ui.Gold : Ui.Muted);
                Cell(table, EquipmentCatalogue.Percent(stat.Sum), true);
            }
            var applied = Cell(table, compact ? EquipmentCatalogue.Percent(stat.Effective) + "\nSuma: " + EquipmentCatalogue.Percent(stat.Sum) : EquipmentCatalogue.Percent(stat.Effective), true,
                stat.Limited ? Ui.Gold : stat.Effective < 0 ? Ui.Red : Ui.Blue);
            applied.TooltipText = EquipmentCatalogue.StatDescription(stat.Stat) + "\n" + string.Join('\n', lines) +
                "\nSuma = botas + gorro + pecho + arma + anillos + conjunto. — indica que no hay pieza equipada." +
                (stat.Limited ? "\nLa suma está limitada: se aplica el valor indicado en Aplicado." : "");
            Cell(table, StatName(stat.Stat)).TooltipText = EquipmentCatalogue.StatDescription(stat.Stat);
        }
        return true;
    }
}
