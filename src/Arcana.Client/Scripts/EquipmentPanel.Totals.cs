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
        var slots = Enum.GetValues<EquipmentSlot>();
        var sets = EquipmentCatalogue.FocusedFamilies.Select(f => (Family: f,
            Pieces: equipped.Count(i => !RingEffects.IsRing(i.Slot) && i.Tier == ItemTier.Mythic && i.Family == f)))
            .Where(s => s.Pieces >= 2).ToArray();
        if (compact)
        {
            var heading = Cell(table, "Aplicado", true, header: true);
            heading.SizeFlagsHorizontal = SizeFlags.Fill;
            heading.AutowrapMode = TextServer.AutowrapMode.Off;
        }
        else
        {
            foreach (var slot in slots) Cell(table, EquipmentCatalogue.SlotName(slot), true, header: true);
            foreach (string heading in new[] { "Conjunto", "Suma", "Aplicado" }) Cell(table, heading, true, header: true);
        }
        Cell(table, compact ? "Atributo y procedencia" : "Atributo", header: true);
        foreach (var stat in active)
        {
            var lines = new List<string>();
            foreach (var slot in slots)
            {
                var piece = equipped.FirstOrDefault(i => i.Slot == slot);
                float value = piece?.Bonuses.Where(b => b.Stat == stat.Stat).Sum(b => b.Current) ?? 0;
                string amount = piece == null ? "—" : EquipmentCatalogue.Percent(value);
                // La procedencia debe poder leerse también por tacto, sin depender de un tooltip.
                if (compact)
                {
                    if (piece != null && Math.Abs(value) > 0.00001f)
                        lines.Add(EquipmentCatalogue.SlotName(slot) + " · " + EquipmentCatalogue.TierName(piece.Tier) + ": " + amount);
                }
                else Cell(table, amount, true, value < 0 ? Ui.Red : value > 0 ? Ui.Text : Ui.Muted);
            }
            if (compact)
            {
                foreach (var set in sets)
                {
                    float value = EquipmentCatalogue.SetBonuses(set.Family).Where(b => b.Stat == stat.Stat)
                        .Sum(b => set.Pieces == 4 ? b.FourPieces : b.TwoPieces);
                    if (Math.Abs(value) > 0.00001f) lines.Add("Conjunto " + EquipmentCatalogue.FamilyName(set.Family) +
                        " (" + set.Pieces + "/4): " + EquipmentCatalogue.Percent(value));
                }
            }
            else
            {
                Cell(table, EquipmentCatalogue.Percent(stat.Set), true, stat.Set != 0 ? Ui.Gold : Ui.Muted);
                Cell(table, EquipmentCatalogue.Percent(stat.Sum), true);
            }
            var applied = Cell(table, EquipmentCatalogue.Percent(stat.Effective) + (compact && stat.Limited ? "\nSuma: " + EquipmentCatalogue.Percent(stat.Sum) : ""), true,
                stat.Limited ? Ui.Gold : stat.Effective < 0 ? Ui.Red : Ui.Blue);
            applied.Name = "EquipmentTotalApplied_" + stat.Stat;
            if (compact) applied.SizeFlagsHorizontal = SizeFlags.Fill;
            applied.TooltipText = EquipmentCatalogue.StatDescription(stat.Stat) + "\n" + string.Join('\n', lines) +
                "\nSuma = botas + gorro + pecho + arma + anillos + conjunto. — indica que no hay pieza equipada." +
                (stat.Limited ? "\nLa suma está limitada: se aplica el valor indicado en Aplicado." : "");
            Cell(table, StatName(stat.Stat), color: compact ? Ui.Gold : null).TooltipText = applied.TooltipText;
            if (compact)
            {
                if (stat.Limited) lines.Add("Se aplica el límite del atributo.");
                // Cada origen es una fila desplazable: una suma de seis piezas nunca crea
                // una celda más alta que toda la pantalla de un móvil en horizontal.
                for (int i = 0; i < lines.Count; i++)
                {
                    var spacer = Ui.TableCell(table, "", size: 14);
                    spacer.SizeFlagsHorizontal = SizeFlags.Fill;
                    var source = Ui.TableCell(table, lines[i], color: Ui.Muted, size: 14);
                    source.Name = "EquipmentTotalSource_" + stat.Stat + "_" + i;
                    source.TooltipText = applied.TooltipText;
                    foreach (var cell in new[] { spacer, source })
                    {
                        var style = (StyleBoxFlat)cell.GetThemeStylebox("normal").Duplicate();
                        style.ContentMarginTop = style.ContentMarginBottom = 3;
                        cell.AddThemeStyleboxOverride("normal", style);
                    }
                }
            }
        }
        return true;
    }
}
