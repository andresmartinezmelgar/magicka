using Godot;

namespace Arcana.Client;

public partial class Main
{
    // Ratón, Escape y Atrás de Android cierran la misma pantalla, de dentro hacia fuera.
    internal bool NavigateBack()
    {
        if (_updateNotice?.HandleBack() == true) return true;
        var choices = _ui.FindChildren(Ui.TouchScrollPopupName, "PopupPanel", true, false)
            .OfType<PopupPanel>().LastOrDefault(popup => popup.Visible);
        if (choices != null) { choices.Hide(); return true; }
        var dialog = _ui.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>()
            .LastOrDefault(d => d.Visible && !d.IsQueuedForDeletion());
        if (dialog != null) { dialog.EmitSignal(AcceptDialog.SignalName.Canceled); dialog.Hide(); return true; }
        if (_reconnecting || Loading) { ReturnToLauncher(); return true; }
        if (_profiler?.Running == true) { _profiler.Cancel(); return true; }
        if (ViewingDamageSummary) ClosePersonalDamageSummary();
        else if (ControlsOpen) _controlsSettings!.HandleBack();
        else if (_help.Visible) { if (!_help.HandleBack()) CloseInGameHelp(); }
        else if (GodotObject.IsInstanceValid(_statisticsPanel)) CloseMatchStatistics();
        else if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.HandleBack();
        else if (_defensePanel?.ShopOpen == true) _defensePanel.HandleBack();
        else if (_tutorialInventory != null) _tutorialInventory.HandleBack();
        else if (InspectingEquipment) ClosePlayerEquipment();
        else if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.HandleBack();
        else if (GodotObject.IsInstanceValid(_bestiaryPanel)) CloseBestiary();
        else if (GodotObject.IsInstanceValid(_accountProfilePanel)) CloseAccountProfile();
        else if (GodotObject.IsInstanceValid(_menuHelp)) { if (!_menuHelp!.HandleBack()) CloseMenuHelp(); }
        else if (_playing && _hud?.ScoreOpen == true) { _hud.ScoreOpen = false; SynchronizeDefenseInterface(); }
        else if (_playing) TogglePause();
        else return _launcher.HandleBack();
        return true;
    }

    private void CloseInGameHelp() { _help.Hide(); SynchronizeDefenseInterface(); UpdateTouchVisibility(); }
}
