using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private (string Run, int Player, int State)? _lastElementSelectionReport;

    private void SynchronizeElementSelectionStatistics()
    {
        if (!_playing || _snapshot == null || Loading || _reconnecting || TutorialActive) return;
        bool automatic = Ui.IsMobile && CurrentSpellShortcuts.Enabled;
        bool blocked = _applicationSuspended || _paused || _help.Visible || InspectingEquipment || ControlsOpen ||
            ViewingDamageSummary || _hud?.ScoreOpen == true || Input.IsPhysicalKeyPressed(Key.Tab) ||
            _relicWindow?.Visible == true || _defensePanel?.ShopOpen == true || _waitingPanel?.Visible == true;
        int state = (automatic ? 1 : 0) | (blocked ? 2 : 0);
        var report = (_snapshot.StatisticsRunId, _localId, state);
        if (_lastElementSelectionReport == report) return;
        _lastElementSelectionReport = report;
        // Solo al entrar, cambiar de modo o abrir/cerrar una interfaz; nunca envía duraciones del cliente.
        Submit(new() { Type = "spell_selection", Slot = state });
    }
}
