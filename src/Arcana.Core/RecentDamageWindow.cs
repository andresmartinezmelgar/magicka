namespace Arcana.Core;

internal readonly record struct RecentDamageSample(long Tick, double Damage);

/// <summary>Cinco segundos exactos, agrupados por paso; memoria acotada y sin asignación por impacto.</summary>
internal sealed class RecentDamageWindow
{
    private RecentDamageSample[]? _samples;
    private int _start, _count;
    private const int Capacity = CombatAffixes.ArcaneHistoryTicks + 1;
    internal void Clear() { _start = _count = 0; _samples = null; }
    internal void Record(long tick, double damage)
    {
        if (!(damage > 0) || !double.IsFinite(damage)) return;
        _samples ??= new RecentDamageSample[Capacity];
        while (_count > 0 && _samples[_start].Tick < tick - CombatAffixes.ArcaneHistoryTicks)
        { _start = (_start + 1) % Capacity; _count--; }
        int last = (_start + _count - 1 + Capacity) % Capacity;
        if (_count > 0 && _samples[last].Tick == tick)
        { _samples[last] = new(tick, Math.Min(1e18, _samples[last].Damage + damage)); return; }
        if (_count == Capacity) { _start = (_start + 1) % Capacity; _count--; }
        _samples[(_start + _count++) % Capacity] = new(tick, Math.Min(1e18, damage));
    }
    internal double Sum(long tick, int seconds)
    {
        double result = 0;
        for (int i = 0; i < _count; i++)
        {
            var sample = _samples![(_start + i) % Capacity];
            if (sample.Tick >= tick - seconds * 60L && sample.Tick <= tick) result += sample.Damage;
        }
        return Math.Min(1e18, result);
    }
    internal RecentDamageSample[] Capture() => Enumerable.Range(0, _count).Select(i => _samples![(_start + i) % Capacity]).ToArray();
    internal void Restore(RecentDamageSample[] samples)
    { Clear(); foreach (var sample in samples) Record(sample.Tick, sample.Damage); }
    internal static bool Valid(RecentDamageSample[]? samples, long tick = long.MaxValue) =>
        samples is { Length: <= Capacity } && samples.All(s => s.Tick >= 0 && s.Tick <= tick && double.IsFinite(s.Damage) && s.Damage is > 0 and <= 1e18) &&
        samples.Zip(samples.Skip(1)).All(p => p.First.Tick < p.Second.Tick) &&
        (samples.Length == 0 || samples[^1].Tick - samples[0].Tick <= CombatAffixes.ArcaneHistoryTicks);
}
