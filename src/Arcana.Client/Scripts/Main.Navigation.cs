using Godot;

namespace Arcana.Client;

public partial class Main
{
    // Ratón, Escape y Atrás de Android cierran la misma pantalla, de dentro hacia fuera.
    private bool NavigateBack()
    {
        if (_updateNotice?.HandleBack() == true) return true;
        var dialog = _ui.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>()
            .LastOrDefault(d => d.Visible && !d.IsQueuedForDeletion());
        if (dialog != null) { dialog.EmitSignal(AcceptDialog.SignalName.Canceled); dialog.Hide(); return true; }
        if (_reconnecting || Loading) { ReturnToLauncher(); return true; }
        if (_profiler?.Running == true) { _profiler.Cancel(); return true; }
        if (ControlsOpen) _controlsSettings!.HandleBack();
        else if (_help.Visible) CloseInGameHelp();
        else if (GodotObject.IsInstanceValid(_statisticsPanel)) CloseMatchStatistics();
        else if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.HandleBack();
        else if (_defensePanel?.ShopOpen == true) _defensePanel.HandleBack();
        else if (InspectingEquipment) ClosePlayerEquipment();
        else if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.HandleBack();
        else if (GodotObject.IsInstanceValid(_bestiaryPanel)) CloseBestiary();
        else if (GodotObject.IsInstanceValid(_accountProfilePanel)) CloseAccountProfile();
        else if (GodotObject.IsInstanceValid(_optionAHelp)) CloseOptionAHelp();
        else if (_playing) TogglePause();
        else return _launcher.HandleBack();
        return true;
    }

    private void CloseInGameHelp() { _help.Hide(); UpdateTouchVisibility(); }
}
