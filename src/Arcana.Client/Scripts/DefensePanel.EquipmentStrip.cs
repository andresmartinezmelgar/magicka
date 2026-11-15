using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    internal SurvivalEquipmentHud EquippedHud { get; private set; } = null!;
    internal int EquippedStripSlotsForVerification => EquippedHud.Buttons.Count;
    private void BuildEquippedStrip()
    {
        EquippedHud = new SurvivalEquipmentHud(); AddChild(EquippedHud); EquippedHud.Build(OpenEquippedSlot);
    }
    private void OpenEquippedSlot(EquipmentSlot slot)
    {
        if (_player == null || _wizard == null) return;
        OpenShop(); ShowSection(1); _equipmentInventory?.PressInspect(slot);
    }
    private void UpdateEquippedStrip(DefensePlayerSnapshot player) => EquippedHud.Update(player);
}
