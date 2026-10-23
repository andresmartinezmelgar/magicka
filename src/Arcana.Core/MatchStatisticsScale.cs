namespace Arcana.Core;

/// <summary>Escala reversible; las cifras y porcentajes siempre conservan los valores originales.</summary>
public sealed class MatchStatisticsScale
{
    public double Maximum { get; }
    public bool Logarithmic { get; }
    private readonly double _unit, _range;
    public MatchStatisticsScale(IEnumerable<double> values, bool? logarithmic = null)
    {
        var positive = values.Where(v => v > 0 && double.IsFinite(v)).ToArray();
        double largest = positive.Length == 0 ? 0 : positive.Max();
        _unit = positive.Length == 0 ? 1 : positive.Min();
        Maximum = Math.Max(1, largest);
        Logarithmic = largest > 0 && (logarithmic ?? largest / _unit >= 20);
        _range = Logarithmic ? Math.Log(1 + Maximum / _unit) : Maximum;
    }
    public double Normalize(double value) => Math.Clamp(Logarithmic
        ? Math.Log(1 + Math.Max(0, value) / _unit) / _range : value / Maximum, 0, 1);
    public double ValueAt(double fraction) => Math.Clamp(Logarithmic
        ? _unit * (Math.Exp(Math.Clamp(fraction, 0, 1) * _range) - 1) : Math.Clamp(fraction, 0, 1) * Maximum, 0, Maximum);
}
