using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private DefensePanel? _defensePanel;
    private bool _defenseEnded;
    private double _defenseSampleSeconds, _defenseSampleElapsed;
    private int _defenseSampleFrames, _defenseSlowWindows;
    private readonly int[] _defenseFrameTimes = new int[1001];
    private void ReportDefensePerformance(WorldSnapshot snapshot, double delta)
    {
        var defense = snapshot.Defense;
        bool reset = defense != null && defense.Elapsed < _defenseSampleElapsed;
        _defenseSampleElapsed = defense?.Elapsed ?? 0;
        if (reset || snapshot.MatchEnded || snapshot.WaitingForPlayers || defense?.Paused == true || DefenseInterfaceOpen || _paused || _help.Visible ||
            _defensePanel?.ShopOpen == true || InspectingEquipment || !GetWindow().HasFocus())
        { ResetDefenseSample(); return; }
        _defenseSampleSeconds += delta; _defenseSampleFrames++;
        _defenseFrameTimes[Math.Clamp((int)Math.Ceiling(delta * 1000), 1, 1000)]++;
        if (_defenseSampleSeconds < 2) return;
        int percentile = 1, count = 0;
        for (; percentile < 1000; percentile++)
        { count += _defenseFrameTimes[percentile]; if (count >= Math.Ceiling(_defenseSampleFrames * 0.95)) break; }
        float fps = (float)Math.Clamp(_defenseSampleFrames / _defenseSampleSeconds, 1, 240);
        int target = Math.Clamp(Engine.MaxFps > 0 ? Engine.MaxFps : 60, 30, 60);
        bool slow = fps < target * .8f || percentile > 1000f / target * 1.7f;
        _defenseSlowWindows = slow ? _defenseSlowWindows + 1 : 0;
        _renderer.Effects.DetailScale = slow ? percentile > 45 ? 0.35f : 0.6f : 1;
        // Los fixtures de imagen usan un paso fijo y no representan FPS reales del dispositivo.
        if (_verification?.FixedRenderStep != true)
            SpellEffectQuality.Observe(fps, percentile, _defenseSampleSeconds);
        // Primero reduce partículas decorativas; los avisos y las áreas de daño conservan su representación.
        Submit(new() { Type = defense != null ? "defense_performance" : "match_performance", FramesPerSecond = fps,
            FrameTime95 = percentile });
        ResetDefenseSample();
    }
    private void ResetDefenseSample()
    { _defenseSampleSeconds = 0; _defenseSampleFrames = 0; Array.Clear(_defenseFrameTimes); }
    private void BuildDefenseControls(WorldSnapshot snapshot)
    {
        _lastDefenseInterface = null;
        _equipmentSetupRetry = 0;
        _defensePanel?.QueueFree(); _defensePanel = null; _defenseEnded = false;
        ResetDefenseSample(); _defenseSampleElapsed = 0; _defenseSlowWindows = 0; _renderer.Effects.DetailScale = 1;
        if (snapshot.Defense == null) return;
        _defensePanel = new DefensePanel(); _ui.AddChild(_defensePanel);
        _defensePanel.Build(this, Submit);
        _defensePanel.OpenChanged += () =>
        {
            StopInput();
            SynchronizeDefenseInterface();
            _ignoreLeftUntilRelease = true; UpdateTouchVisibility();
            if (_defensePanel != null) _ui.MoveChild(_defensePanel, -1);
        };
        if (_touch != null) _touch.PointerBlocked = point => _defensePanel?.BlocksTouch(point) == true;
        _defensePanel.Update(snapshot, _localId, _connection != null, _connection?.Server?.RewardsAvailable == true);
    }
}
