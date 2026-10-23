using Arcana.Core;
using System.Globalization;

namespace Arcana.Client;

internal static class StatisticsPresentation
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    internal static string Number(double value) => value == 0 ? "0" : value < .01 ? value.ToString("G3", Spanish)
        : value.ToString("N2", Spanish).TrimEnd('0').TrimEnd(',');
    internal static string Percent(double value, double total)
    {
        double percent = total > 0 ? 100 * value / total : 0;
        return percent > 0 && percent < .01 ? "<0,01 %" : percent.ToString("0.##", Spanish) + " %";
    }
    internal static string ScaleNote(MatchStatisticsScale scale) => scale.Logarithmic
        ? "Escala logarítmica: amplía los valores pequeños. Compara las cifras y porcentajes para conocer las proporciones reales."
        : "Escala lineal: el tamaño es proporcional al valor.";
}
