using Godot;
using Arcana.Core;
namespace Arcana.Client;

public partial class Main
{
    private void AssertInventorySplit(EquipmentPanel panel)
    {
        var left = (Control)panel.FindChild("EquipmentAvatarPane", true, false);
        var right = (Control)panel.FindChild("EquipmentItemsPane", true, false);
        var screen = GetViewport().GetVisibleRect().Grow(2);
        if (!screen.Encloses(left.GetGlobalRect()) || !screen.Encloses(right.GetGlobalRect()) || left.GetGlobalRect().End.X > right.GetGlobalRect().Position.X + 1)
            throw new InvalidOperationException("El mago y los objetos no forman dos paneles visibles sin superposición: " + left.GetGlobalRect() + " / " + right.GetGlobalRect());
        var zones = panel.FindChildren("EquipmentSlot*", "Button", true, false).OfType<Button>().ToArray();
        if (zones.Length != 6 || zones.Any(b => !left.GetGlobalRect().Grow(1).Encloses(b.GetGlobalRect()) || b.Size.X < 44 || b.Size.Y < 44))
            throw new InvalidOperationException("Falta una de las seis zonas del mago o queda fuera de su panel.");
        for (int i = 0; i < zones.Length; i++) for (int j = i + 1; j < zones.Length; j++)
            if (zones[i].GetGlobalRect().Intersects(zones[j].GetGlobalRect())) throw new InvalidOperationException("Dos zonas de equipo se superponen.");
        if (panel.FindChild("EquipmentFilters", true, false) != null || panel.FindChild("EquipmentSort", true, false) != null)
            throw new InvalidOperationException("El inventario conserva filtros.");
        var stats = (Button)panel.FindChild("EquipmentTotalsTab", true, false);
        if (!right.GetGlobalRect().Encloses(stats.GetGlobalRect())) throw new InvalidOperationException("El botón de estadísticas no está a la derecha.");
        var table = (GridContainer)panel.FindChild("EquipmentTotalsTable", true, false);
        var numbers = table.GetChildren().OfType<ElementTextLabel>().Where(l => l.Name.ToString().StartsWith("EquipmentTotalApplied_"))
            .Select(l => float.Parse(l.Text.Split('\n')[0].TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        if (!numbers.SequenceEqual(numbers.OrderByDescending(n => n))) throw new InvalidOperationException("Las estadísticas no están ordenadas de mayor a menor.");
        GD.Print("INVENTORY_SPLIT_OK: seis zonas accesibles, mago a la izquierda, objetos y estadísticas a la derecha; filtros ausentes y totales descendentes.");
    }

    private void AssertHudBounds(Hud hud, string stage)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        foreach (var name in new[] { "MatchBanner", "MatchRewardNotice", "PlayerStatusPanel", "SpellControlsPanel", "MiniMapPanel", "MatchObjective" })
        {
            if (hud.FindChild(name, true, false) is not Control control || !control.IsVisibleInTree()) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
                throw new InvalidOperationException($"HUD {stage}: {name} fuera de pantalla: {rect}, viewport={screen}");
        }
        var status = (Control)hud.FindChild("PlayerStatusPanel", true, false);
        var minimap = (Control)hud.FindChild("MiniMapPanel", true, false);
        if (status.IsVisibleInTree() && minimap.IsVisibleInTree() &&
            status.GetGlobalRect().Intersects(minimap.GetGlobalRect()))
            throw new InvalidOperationException($"HUD {stage}: el minimapa tapa la vida del jugador: {minimap.GetGlobalRect()} / {status.GetGlobalRect()}");
    }

    private void AssertVisibleTableData(Control root, string tableName, bool includeHeader = true)
    {
        var table = root.FindChild(tableName, true, false) as GridContainer
            ?? throw new InvalidOperationException("Falta la tabla " + tableName);
        var cells = table.GetChildren().OfType<ElementTextLabel>().Where(label => label.Visible).ToArray();
        Rect2 visible = GetViewport().GetVisibleRect();
        for (Node? parent = table.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) visible = visible.Intersection(scroll.GetGlobalRect());
        int[] required = includeHeader ? [0, table.Columns - 1, table.Columns, table.Columns * 2 - 1]
            : [table.Columns, table.Columns * 2 - 1];
        if (cells.Length < table.Columns * 2 || required
            .Any(index => !visible.Grow(1).Encloses(cells[index].GetGlobalRect())))
            throw new InvalidOperationException(tableName + ": no se ven la cabecera y una fila completa de datos. Área visible: " + visible + "; celdas: " + string.Join("; ", cells.Take(table.Columns * 2).Select(cell => cell.Text + " " + cell.GetGlobalRect())));
    }

    private static void AssertHeaderAndHome(Launcher launcher, Rect2 screen, string mode)
    {
        if (launcher.FindChild("OptionAHeader", true, false) is not Control header ||
            launcher.FindChild("OptionAAccountSummary", true, false) is not ElementTextLabel summary ||
            launcher.FindChild("OptionAHomeCards", true, false) is not GridContainer home)
            throw new InvalidOperationException($"{mode}: faltan cabecera, resumen o tarjetas.");
        Rect2 head = header.GetGlobalRect();
        Rect2 account = summary.GetGlobalRect();
        Rect2 cards = home.GetGlobalRect();
        var buttons = home.GetChildren().OfType<Button>().ToArray();
        if (head.Size.Y is < 55 or > 80 || head.Position.Y < -1 || !screen.Encloses(head))
            throw new InvalidOperationException($"{mode}: la cabecera invade la pantalla: {head}");
        if (!summary.IsVisibleInTree() || summary.AutowrapMode != TextServer.AutowrapMode.Off ||
            account.Size.X < 140 || account.Size.Y > 56 || account.Position.Y < head.Position.Y ||
            account.End.Y > head.End.Y || account.End.X > screen.End.X)
            throw new InvalidOperationException($"{mode}: perfil inválido: rect={account}, header={head}, visible={summary.IsVisibleInTree()}, wrap={summary.AutowrapMode}, screen={screen}");
        if (home.Columns != 2 || buttons.Length != 4 ||
            cards.Position.Y < head.End.Y || cards.Position.X < 0 ||
            cards.End.X > screen.End.X || cards.Position.Y > screen.End.Y - 80)
            throw new InvalidOperationException($"{mode}: tarjetas 2×2 fuera del área central: {cards}, header={head}");
        foreach (var button in buttons)
        {
            var rect = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || rect.Size.X < 120 || rect.Size.Y < 90 ||
                rect.Position.Y < head.End.Y || rect.Position.X < 0 || rect.End.X > screen.End.X ||
                rect.Position.Y > screen.End.Y - 65)
                throw new InvalidOperationException($"{mode}: tarjeta del inicio fuera de pantalla: {button.Text}, {rect}");
        }
        GD.Print($"HEADER_AND_HOME_OK:{mode}: header={head}, summary={account}, cards={cards}");
    }

    private void AssertMobileActions(string stage, IEnumerable<Button> buttons, int minimumCount)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        Button[] visible = buttons.ToArray();
        if (visible.Length < minimumCount)
            throw new InvalidOperationException($"{stage}: faltan botones ({visible.Length}/{minimumCount}).");
        foreach (var button in visible)
        {
            var bounds = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || bounds.Size.Y < 60 || bounds.Size.X < 95 ||
                !screen.Encloses(bounds))
                throw new InvalidOperationException(
                    $"{stage}: botón fuera de pantalla o demasiado pequeño: {button.Text} rect={bounds} pantalla={screen}.");
            GD.Print($"MOBILE_BUTTON_OK:{stage}: {button.Text} {bounds}");
        }
    }

    private void AssertOptionAScreen(string stage, Control root)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        if (root.FindChild("OptionAHeader", true, false) is not Control ||
            root.FindChild("OptionAFooter", true, false) is not Control)
            throw new InvalidOperationException($"{stage}: faltan barras de navegación de la opción A.");
        var header = ((Control)root.FindChild("OptionAHeader", true, false)).GetGlobalRect();
        var footer = ((Control)root.FindChild("OptionAFooter", true, false)).GetGlobalRect();
        var content = ((Control)root.FindChild("ResponsiveContentHost", true, false)).GetGlobalRect();
        if (!screen.Grow(1).Encloses(header) || !screen.Grow(1).Encloses(footer) ||
            content.Position.Y < header.End.Y - 1 || content.End.Y > footer.Position.Y + 1)
            throw new InvalidOperationException($"{stage}: las barras comunes tapan el contenido o salen de pantalla.");
        foreach (int page in new[] { 1, 2, 3 })
            if (root.FindChild("OptionANav" + page, true, false) != null)
                throw new InvalidOperationException($"{stage}: PvP, IA o Perfil siguen duplicados en la barra inferior.");
        foreach (int page in new[] { 0, 4, 7, 5, 6 })
        {
            var navigation = (Button)root.FindChild("OptionANav" + page, true, false);
            if (!navigation.IsVisibleInTree() || !footer.Grow(1).Encloses(navigation.GetGlobalRect()) ||
                navigation.Size.X < 44 || navigation.Size.Y < 44)
                throw new InvalidOperationException($"{stage}: la opción inferior {page} no es accesible.");
        }
        foreach (var control in root.FindChildren("*", "Control", true, false).OfType<Control>())
        {
            if (!control.IsVisibleInTree()) continue;
            if (control is ScrollContainer sc && sc.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled)
                throw new InvalidOperationException($"{stage}: el panel {sc.Name} permite desbordamiento horizontal.");
            if (control is not BaseButton && control is not LineEdit &&
                control is not SpinBox && control is not ScrollChoiceList) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
            {
                // El contenido puede desplazarse verticalmente, pero nunca
                // desbordarse horizontalmente. Las acciones fijas se prueban
                // por separado y siempre deben quedar dentro de pantalla.
                bool inVerticalScroll = false;
                for (Node? parent = control.GetParent(); parent != null; parent = parent.GetParent())
                    if (parent is ScrollContainer container && container.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
                    { inVerticalScroll = true; break; }
                if (inVerticalScroll && rect.Position.X >= screen.Position.X &&
                    rect.End.X <= screen.End.X) continue;
                throw new InvalidOperationException($"{stage}: control fuera de pantalla: {control.Name} {rect} / {screen}");
            }
        }
        GD.Print($"OPTION_A_SCREEN_OK:{stage}");
    }
}
