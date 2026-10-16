using Godot;

namespace Arcana.Client;

/// <summary>Alterna equipo y atributos por pestañas en vez de obligar a desplazar largas tablas.</summary>
public partial class EquipmentPanel
{
    private ArcanaMenuChrome? _optionAChrome;
    private Button? _optionAItemsTab, _optionAStatsTab;
    private ScrollContainer? _optionAItems, _optionAStats;

    private void ApplyOptionAEquipment(MarginContainer margin, PanelContainer panel,
        VBoxContainer box, HBoxContainer columns, ScrollContainer items, ScrollContainer totals,
        HBoxContainer legacyFooter)
    {
        if (_defensePlayer == null)
            _optionAChrome = ArcanaMenuChrome.Attach(this, margin, _main, "Inventario y equipación",
                4, _main.NavigateOptionAMenu);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 9 : 15);
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0a1d33"), 11, new Color("2b719d")));
        box.AddThemeConstantOverride("separation", 5);

        // Los atributos ya no se esconden al fondo de un scroll: se alternan con el equipo.
        var tabs = new HBoxContainer { Name = "OptionAEquipmentTabs" };
        tabs.AddThemeConstantOverride("separation", 12);
        box.AddChild(tabs);
        box.MoveChild(tabs, 3);
        _optionAItemsTab = Ui.Button("◇  OBJETOS Y EQUIPACIÓN", () => SwitchOptionAEquipment(false));
        _optionAStatsTab = Ui.Button("✧  SUMA DE BONIFICACIONES", () => SwitchOptionAEquipment(true));
        _optionAItemsTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _optionAStatsTab.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _optionAItemsTab.CustomMinimumSize = new(0, Ui.IsMobile ? 58 : 47);
        _optionAStatsTab.CustomMinimumSize = new(0, Ui.IsMobile ? 58 : 47);
        tabs.AddChild(_optionAItemsTab);
        tabs.AddChild(_optionAStatsTab);

        _optionAItems = items;
        _optionAStats = totals;
        items.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        items.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        totals.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        totals.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        Ui.VisibleVerticalScroll(items);
        Ui.VisibleVerticalScroll(totals);
        items.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        totals.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddThemeConstantOverride("separation", 0);
        legacyFooter.Visible = _defensePlayer != null;
        SwitchOptionAEquipment(false);
    }

    private void SwitchOptionAEquipment(bool stats)
    {
        if (_optionAItems == null || _optionAStats == null) return;
        _optionAItems.Visible = !stats;
        _optionAStats.Visible = stats;
        _showingTotals = stats;
        UpdateEquipmentResponsive();
        _optionAItemsTab!.AddThemeStyleboxOverride("normal",
            Ui.Panel(stats ? new Color("12263d") : new Color("185184"), 7, stats ? new Color("294e72") : Ui.Blue));
        _optionAStatsTab!.AddThemeStyleboxOverride("normal",
            Ui.Panel(stats ? new Color("185184") : new Color("12263d"), 7, stats ? Ui.Blue : new Color("294e72")));
        _optionAChrome?.RefreshAccount();
    }
}
