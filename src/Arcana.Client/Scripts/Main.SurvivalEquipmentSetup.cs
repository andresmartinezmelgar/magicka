using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private double _equipmentSetupRetry;
    private static bool TemporarySurvivalSettings => OS.GetCmdlineUserArgs().Any(a =>
        a.Contains("smoke", StringComparison.Ordinal) || a.Contains("preview", StringComparison.Ordinal) || a == "--visual-review");
    private InventoryPreferences SurvivalPreferences => DevicePreferences.Inventory("survival:" + (Accounts.Profile?.Uid ?? "guest"), TemporarySurvivalSettings);
    internal bool RememberSurvivalEquipmentSetup(SurvivalEquipmentSetup setup)
    {
        if (!setup.Valid) return false;
        SurvivalPreferences.SurvivalEquipment = setup.Copy();
        return TemporarySurvivalSettings || DevicePreferences.SaveInventory();
    }
    private void InitializeSurvivalEquipmentSetup(WorldSnapshot snapshot, double delta)
    {
        _equipmentSetupRetry = Math.Max(0, _equipmentSetupRetry - delta);
        var player = snapshot.Defense?.Defenders.FirstOrDefault(p => p.PlayerId == _localId);
        if (player == null || player.EquipmentSetupInitialized || snapshot.MatchEnded || _equipmentSetupRetry > 0) return;
        _equipmentSetupRetry = 2;
        var preferred = SurvivalPreferences.SurvivalEquipment;
        if (!preferred.Valid) preferred = new();
        Submit(new() { Type = "defense_equipment_setup", SurvivalEquipmentSetup = preferred.Copy() });
    }
}
