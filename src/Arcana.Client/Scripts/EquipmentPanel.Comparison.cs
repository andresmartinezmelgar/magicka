using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private void AddComparison(VBoxContainer description, EquipmentItem item, EquipmentPreview? preview = null)
    {
        if (_draft == null || _draft.Loadout.Get(item.Slot) == item.Id) return;
        var label = Ui.Label("EQUIPO AL CAMBIAR ESTA PIEZA\n" + NumberedImprovements((preview ?? EquipmentQuery.Preview(_draft, item, _itemLevels)).Describe()),
            Ui.IsMobile ? 16 : 15, Ui.Muted);
        label.Name = "EquipmentComparison_" + item.Id;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.MouseFilter = MouseFilterEnum.Pass;
        description.AddChild(label);
    }
}
