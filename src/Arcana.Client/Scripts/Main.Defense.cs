using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private DefensePanel? _defensePanel;
    private bool _defenseEnded;
    private double _defenseSampleSeconds, _defenseSampleElapsed;
    private int _defenseSampleFrames;
    private void ReportDefensePerformance(WorldSnapshot snapshot, double delta)
    {
        if (snapshot.Defense is not { } defense) return;
        bool reset = defense.Elapsed < _defenseSampleElapsed;
        _defenseSampleElapsed = defense.Elapsed;
        if (reset || snapshot.MatchEnded || snapshot.WaitingForPlayers || defense.Paused || _paused || _help.Visible ||
            _defensePanel?.ShopOpen == true || InspectingEquipment || !GetWindow().HasFocus())
        { _defenseSampleSeconds = 0; _defenseSampleFrames = 0; return; }
        _defenseSampleSeconds += delta; _defenseSampleFrames++;
        if (_defenseSampleSeconds < 5) return;
        Submit(new() { Type = "defense_performance", FramesPerSecond = (float)Math.Clamp(_defenseSampleFrames / _defenseSampleSeconds, 1, 240) });
        _defenseSampleSeconds = 0; _defenseSampleFrames = 0;
    }
    private void BuildDefenseControls(WorldSnapshot snapshot)
    {
        _defensePanel?.QueueFree(); _defensePanel = null; _defenseEnded = false;
        _defenseSampleSeconds = _defenseSampleElapsed = 0; _defenseSampleFrames = 0;
        if (snapshot.Defense == null) return;
        _defensePanel = new DefensePanel(); _ui.AddChild(_defensePanel);
        _defensePanel.Build(this, Submit);
        _defensePanel.OpenChanged += () =>
        {
            StopInput();
            Submit(new() { Type = "defense_shop", Slot = _defensePanel?.ShopOpen == true ? 1 : 0 });
            _ignoreLeftUntilRelease = true; UpdateTouchVisibility();
            if (_defensePanel != null) _ui.MoveChild(_defensePanel, -1);
        };
        if (_touch != null) _touch.PointerBlocked = point => _defensePanel?.BlocksTouch(point) == true;
        _defensePanel.Update(snapshot, _localId, _connection != null, _connection?.Server?.RewardsAvailable == true);
    }
}
