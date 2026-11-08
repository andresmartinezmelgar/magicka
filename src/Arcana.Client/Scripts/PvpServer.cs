using System.Text.RegularExpressions;

namespace Arcana.Client;

/// <summary>Destino de PvP; la interfaz utiliza su nombre, nunca su dirección.</summary>
internal static class PvpServer
{
    internal const string Host = "familiamelgar.duckdns.org";
    internal const string Address = "wss://" + Host + "/play";
    internal const string Name = "Servidor PvP";

    private static readonly Regex Endpoint = new(
        @"(?:https?://|wss?://)?" + Regex.Escape(Host) + @"(?::\d+)?(?:/[^\s""'<>]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string DisplayText(string text) => Endpoint.Replace(text, Name);
}
