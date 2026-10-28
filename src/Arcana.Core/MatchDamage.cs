using System.Text.Json.Serialization;

namespace Arcana.Core;

/// <summary>Daño efectivo personal de esta partida: ocho elementos y daño sin elemento.</summary>
public sealed record MatchDamageSnapshot(double[] Health, double[] Shield)
{
    public const int ElementCount = 9;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double PersonalShieldAbsorbed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double BarrierAbsorbed { get; init; }
    [JsonIgnore] public double HealthTotal => Health.Sum();
    [JsonIgnore] public double ShieldTotal => Shield.Sum();
    [JsonIgnore] public double Total => HealthTotal + ShieldTotal;
    public bool Valid() => Health is { Length: ElementCount } && Shield is { Length: ElementCount } &&
        Health.All(v => double.IsFinite(v) && v is >= 0 and <= 1e18) &&
        Shield.All(v => double.IsFinite(v) && v is >= 0 and <= 1e18) &&
        double.IsFinite(PersonalShieldAbsorbed) && PersonalShieldAbsorbed is >= 0 and <= 1e18 &&
        double.IsFinite(BarrierAbsorbed) && BarrierAbsorbed is >= 0 and <= 1e18;
}

internal sealed class MatchDamageAccumulator
{
    private readonly double[] _health = new double[MatchDamageSnapshot.ElementCount], _shield = new double[MatchDamageSnapshot.ElementCount];
    private double _personalShieldAbsorbed, _barrierAbsorbed;
    internal void Add(int element, double health, double shield)
    {
        if (element is < 0 or >= MatchDamageSnapshot.ElementCount) return;
        if (double.IsFinite(health) && health > 0) _health[element] = Math.Min(1e18, _health[element] + health);
        if (double.IsFinite(shield) && shield > 0) _shield[element] = Math.Min(1e18, _shield[element] + shield);
    }
    internal void Absorb(double value, bool barrier)
    {
        if (!double.IsFinite(value) || value <= 0) return;
        if (barrier) _barrierAbsorbed = Math.Min(1e18, _barrierAbsorbed + value);
        else _personalShieldAbsorbed = Math.Min(1e18, _personalShieldAbsorbed + value);
    }
    internal void Clear()
    { Array.Clear(_health); Array.Clear(_shield); _personalShieldAbsorbed = _barrierAbsorbed = 0; }
    internal MatchDamageSnapshot? Snapshot() => _health.Any(v => v > 0) || _shield.Any(v => v > 0) ||
        _personalShieldAbsorbed > 0 || _barrierAbsorbed > 0
        ? new(_health.ToArray(), _shield.ToArray()) { PersonalShieldAbsorbed = _personalShieldAbsorbed, BarrierAbsorbed = _barrierAbsorbed } : null;
    internal void Restore(MatchDamageSnapshot? snapshot)
    {
        Clear();
        if (snapshot == null) return;
        if (!snapshot.Valid()) throw new InvalidDataException("Resumen personal de daño inválido.");
        snapshot.Health.CopyTo(_health, 0); snapshot.Shield.CopyTo(_shield, 0);
        _personalShieldAbsorbed = snapshot.PersonalShieldAbsorbed; _barrierAbsorbed = snapshot.BarrierAbsorbed;
    }
}
