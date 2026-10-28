using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private PersonalDamagePanel? _personalDamagePanel;
    private int _damageSummaryDeaths;
    private bool _damageSummaryEnded, _damageSummaryAuto;
    private double _damageSummaryClock;
    private bool ViewingDamageSummary => GodotObject.IsInstanceValid(_personalDamagePanel);

    private void ResetPersonalDamageSummary()
    {
        ClosePersonalDamageSummary();
        var player = _snapshot?.Players.FirstOrDefault(p => p.Id == _localId);
        _damageSummaryDeaths = player == null ? 0 : player.Alive ? player.Deaths : Math.Max(0, player.Deaths - 1);
        _damageSummaryEnded = false; _damageSummaryClock = 0;
    }
    private void OpenPersonalDamageSummary() => OpenPersonalDamageSummary(automatic: false);
    private void OpenPersonalDamageSummary(bool automatic)
    {
        if (!_playing || _snapshot?.Players.FirstOrDefault(p => p.Id == _localId) is not { } player) return;
        if (ViewingDamageSummary) { _damageSummaryAuto |= automatic; return; }
        _damageSummaryAuto = automatic; _damageSummaryClock = 0;
        ClosePlayerEquipment(); _defensePanel?.CloseShop();
        if (_hud != null) _hud.ScoreOpen = false;
        _personalDamagePanel = new(); _ui.AddChild(_personalDamagePanel);
        _personalDamagePanel.Build(ClosePersonalDamageSummary); _personalDamagePanel.Update(player, _snapshot.MatchEnded);
        _ui.MoveChild(_personalDamagePanel, -1);
        StopInput(); SynchronizeDefenseInterface(); UpdateTouchVisibility();
    }
    private void ClosePersonalDamageSummary()
    {
        bool open = ViewingDamageSummary;
        if (open) { _moveTo = false; _ignoreLeftUntilRelease = true; _touch?.Reset(); _personalDamagePanel!.QueueFree(); }
        _personalDamagePanel = null; _damageSummaryAuto = false;
        if (open) { SynchronizeDefenseInterface(); UpdateTouchVisibility(); }
    }
    private void UpdatePersonalDamageSummary(WorldSnapshot snapshot, double delta)
    {
        var player = snapshot.Players.FirstOrDefault(p => p.Id == _localId);
        if (player == null || snapshot.WaitingForPlayers) return;
        bool newDeath = player.Deaths > _damageSummaryDeaths, newEnd = snapshot.MatchEnded && !_damageSummaryEnded;
        bool newRound = player.Deaths < _damageSummaryDeaths || !snapshot.MatchEnded && _damageSummaryEnded;
        _damageSummaryDeaths = player.Deaths; _damageSummaryEnded = snapshot.MatchEnded;
        if (newRound || _damageSummaryAuto && player.Alive && !snapshot.MatchEnded) ClosePersonalDamageSummary();
        if (newDeath || newEnd) OpenPersonalDamageSummary(automatic: true);
        _damageSummaryClock += delta;
        if (ViewingDamageSummary && (_damageSummaryClock >= 0.25 || newDeath || newEnd))
        { _damageSummaryClock = 0; _personalDamagePanel!.Update(player, snapshot.MatchEnded); }
    }
}
