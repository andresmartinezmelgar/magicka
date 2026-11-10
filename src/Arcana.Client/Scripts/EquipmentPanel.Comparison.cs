using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private void AddComparison(VBoxContainer description, EquipmentItem item, EquipmentPreview? preview = null)
    {
        if (_draft == null || _draft.Loadout.Get(item.Slot) == item.Id) return;
        bool prioritizeArcane = _defensePlayer?.EquipmentSetup.Element == Element.Arcane;
        string current = _draft.Loadout.Get(item.Slot);
        string title = string.IsNullOrEmpty(current) ? "AL EQUIPAR EN LA RANURA VACÍA" : "COMPARACIÓN CON LA PIEZA EQUIPADA";
        var comparison = preview ?? EquipmentQuery.Preview(_draft, item, _itemLevels);
        var label = Ui.Label(title + "\n" + NumberedImprovements(comparison.Describe(inventoryComparison: true, prioritizeArcane: prioritizeArcane)),
            Ui.IsMobile ? 16 : 15, Ui.Muted);
        label.Name = "EquipmentComparison_" + item.Id;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.MouseFilter = MouseFilterEnum.Pass;
        description.AddChild(label);
    }
}
