namespace Arcana.Core;

public sealed partial class GameWorld
{
    private MatchStatisticsCollector _statistics = new();
    private StatisticAttack? _statisticsAttack;
    private readonly Dictionary<int, double> _statisticsPerformanceTime = [];
    public string MatchStatisticsRunId => _statistics.RunId;
    internal int StatisticsDifficulty => IsDefense ? _defenseDifficulty : 0;
    public void SetStatisticsNetwork() => _statistics.Network = true;
    public MatchStatisticsReport MatchStatistics(MatchRecordStatus status = MatchRecordStatus.Playing) => _statistics.Capture(this, status);
    private void ResetStatistics()
    {
        _statistics = new() { Network = _statistics.Network }; _statisticsPerformanceTime.Clear();
        foreach (var p in _players.Values) _statistics.Bind(p, spawn: false);
    }
    private void RestoreStatistics(MatchStatisticsReport? report)
    {
        bool carry = report != null && report.GameVersion == BuildInfo.Version;
        _statistics = carry ? MatchStatisticsCollector.Restore(report!) : new() { Continuation = true };
        foreach (var p in _players.Values) _statistics.Bind(p, spawn: !carry);
        foreach (var m in _minions.Values) _statistics.Bind(m.Body, m.Kind, spawn: !carry);
    }
    private void HitAs(StatisticAttack cause, Wizard target, SpellRecipe spell, float scale, int owner, V2 force)
    {
        var previous = _statisticsAttack; _statisticsAttack = cause;
        try { Hit(target, spell, scale, owner, force); } finally { _statisticsAttack = previous; }
    }
    private void DamageAs(StatisticAttack cause, Wizard target, float amount, int owner, Element[]? elements = null)
    {
        var previous = _statisticsAttack; _statisticsAttack = cause;
        try { Damage(target, amount, owner, elements); } finally { _statisticsAttack = previous; }
    }
    private void HealAs(StatisticAttack cause, Wizard target, float amount, int owner)
    {
        float before = target.Health;
        Heal(target, amount);
        _statistics.Heal(owner, target, target.Health - before, cause);
    }
    private void StatisticsEconomy(int id, StatisticMeasure measure, int amount)
    { _statistics.Total(measure, amount); _statistics.Actor(id, measure, amount); }
    private void DamageWall(Wall wall, SpellRecipe attack, int owner, float amount)
    {
        float before = wall.Health;
        wall.Health -= WallDamage(wall, attack, owner, amount);
        _statistics.BarrierDamage(owner, wall.Owner, Math.Min(Math.Max(0, before), before - wall.Health), attack.Elements, MatchStatisticsCollector.Shape(attack.Shape));
    }
    public bool ObserveStatisticsPerformance(int id, float fps, float frameTime95)
    {
        if (!_players.TryGetValue(id, out var p) || p.Bot || !float.IsFinite(fps) || fps is < 1 or > 240 ||
            !float.IsFinite(frameTime95) || frameTime95 is < 0 or > 1000 || Time - _statisticsPerformanceTime.GetValueOrDefault(id, -100) < 1.5) return false;
        _statisticsPerformanceTime[id] = Time;
        _statistics.Add(StatisticGroup.Performance, 2, StatisticMeasure.Samples, 1);
        _statistics.Add(StatisticGroup.Performance, 2, StatisticMeasure.ValueSum, fps);
        _statistics.Add(StatisticGroup.Performance, 3, StatisticMeasure.Samples, 1);
        _statistics.Add(StatisticGroup.Performance, 3, StatisticMeasure.ValueSum, frameTime95);
        if (fps < 30) _statistics.Add(StatisticGroup.Performance, 4, StatisticMeasure.Samples, 1);
        return true;
    }
}
