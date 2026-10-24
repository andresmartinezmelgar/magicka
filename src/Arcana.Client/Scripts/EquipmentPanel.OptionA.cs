using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private ArcanaMenuChrome? _optionAChrome;
    private Button? _optionAItemsTab, _optionAStatsTab;
    private Control? _optionAStats;

    private void SwitchOptionAEquipment(bool stats)
    {
        if (_optionAStats == null || _itemPicker == null) return;
        _showingTotals = stats; _optionAStats.Visible = stats; _itemPicker.Visible = !stats;
        _optionAItemsTab!.AddThemeColorOverride("font_color", stats ? Ui.Muted : Ui.Gold);
        _optionAStatsTab!.AddThemeColorOverride("font_color", stats ? Ui.Gold : Ui.Muted);
        UpdateEquipmentResponsive(); _optionAChrome?.RefreshAccount();
    }
}
