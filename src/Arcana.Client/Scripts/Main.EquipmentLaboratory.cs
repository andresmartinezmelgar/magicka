using Godot;

namespace Arcana.Client;

public partial class Main
{
    private EquipmentLaboratory? _equipmentLaboratory;
    public void OpenEquipmentLaboratory()
    {
        if (_playing || Loading || GodotObject.IsInstanceValid(_equipmentLaboratory)) return;
        _equipmentLaboratory = new EquipmentLaboratory(); _ui.AddChild(_equipmentLaboratory);
        _equipmentLaboratory.Build(CloseEquipmentLaboratory); _launcher.Hide();
    }
    private void CloseEquipmentLaboratory()
    {
        if (GodotObject.IsInstanceValid(_equipmentLaboratory)) { _equipmentLaboratory!.Hide(); _equipmentLaboratory.QueueFree(); }
        _equipmentLaboratory = null; if (!_playing) _launcher.Show();
    }
}
