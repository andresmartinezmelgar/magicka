using Arcana.Core;

namespace Arcana.Client;

public partial class Main
{
    private DefensePanel? _defensePanel;
    private bool _defenseEnded;
    private void BuildDefenseControls(WorldSnapshot snapshot)
    {
        _defensePanel?.QueueFree(); _defensePanel = null; _defenseEnded = false;
        if (snapshot.Defense == null) return;
        _defensePanel = new DefensePanel(); _ui.AddChild(_defensePanel);
        _defensePanel.Build(Submit, () => ReturnToLauncher());
        _defensePanel.OpenChanged += () =>
        {
            StopInput();
            Submit(new() { Type = "defense_shop", Slot = _defensePanel?.ShopOpen == true ? 1 : 0 });
            _ignoreLeftUntilRelease = true; UpdateTouchVisibility();
            if (_defensePanel != null) _ui.MoveChild(_defensePanel, -1);
        };
        _touch?.ConfigureDefense();
        if (_touch != null) _touch.PointerBlocked = point => _defensePanel?.BlocksTouch(point) == true;
        _defensePanel.Update(snapshot, _localId, _connection != null, _connection?.Server?.RewardsAvailable == true);
    }
}
