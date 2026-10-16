using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    internal static bool RenderTotals(GridContainer table, IEnumerable<EquipmentStatTotal> stats,
        IReadOnlyList<PublicEquipmentItem> equipped)
    {
        Clear(table);
        var active = stats.Where(s => Math.Abs(s.Effective) > 0.00001f).ToArray();
        table.Visible = active.Length > 0;
        if (!table.Visible) return false;
        bool compact = table.Columns == 2;
        var slots = Enum.GetValues<EquipmentSlot>();
        Cell(table, "Atributo", header: true);
        if (compact) Cell(table, "Piezas y total aplicado", true, header: true);
        else
        {
            foreach (var slot in slots) Cell(table, EquipmentCatalogue.SlotName(slot), true, header: true);
            foreach (string heading in new[] { "Conjunto", "Suma", "Aplicado" }) Cell(table, heading, true, header: true);
        }
        foreach (var stat in active)
        {
            Cell(table, StatName(stat.Stat)).TooltipText = EquipmentCatalogue.StatDescription(stat.Stat);
            var lines = new List<string>();
            foreach (var slot in slots)
            {
                var piece = equipped.FirstOrDefault(i => i.Slot == slot);
                float value = piece?.Bonuses.Where(b => b.Stat == stat.Stat).Sum(b => b.Current) ?? 0;
                string amount = piece == null ? "—" : EquipmentCatalogue.Percent(value);
                if (compact) lines.Add(EquipmentCatalogue.SlotName(slot) + ": " + amount);
                else Cell(table, amount, true, value < 0 ? Ui.Red : value > 0 ? Ui.Text : Ui.Muted);
            }
            if (compact)
            {
                lines[0] += " · " + lines[1];
                lines[1] = lines[2] + " · " + lines[3];
                lines.RemoveRange(2, 2);
                lines.Add("Conjunto: " + EquipmentCatalogue.Percent(stat.Set) + " · Suma: " + EquipmentCatalogue.Percent(stat.Sum));
                lines.Add("Aplicado: " + EquipmentCatalogue.Percent(stat.Effective));
            }
            else
            {
                Cell(table, EquipmentCatalogue.Percent(stat.Set), true, stat.Set != 0 ? Ui.Gold : Ui.Muted);
                Cell(table, EquipmentCatalogue.Percent(stat.Sum), true);
            }
            var applied = Cell(table, compact ? string.Join('\n', lines) : EquipmentCatalogue.Percent(stat.Effective), true,
                stat.Limited ? Ui.Gold : stat.Effective < 0 ? Ui.Red : Ui.Blue);
            applied.TooltipText = EquipmentCatalogue.StatDescription(stat.Stat) +
                "\nSuma = botas + gorro + pecho + arma + conjunto. — indica que no hay pieza equipada." +
                (stat.Limited ? "\nLa suma está limitada: se aplica el valor indicado en Aplicado." : "");
        }
        return true;
    }
}
