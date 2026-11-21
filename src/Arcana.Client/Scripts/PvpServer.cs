using System.Text.RegularExpressions;

namespace Arcana.Client;

/// <summary>Destino de PvP; la interfaz utiliza su nombre, nunca su dirección.</summary>
internal static class PvpServer
{
    internal const string Automatic = "automatic";
    internal const string Name = "Servidor PvP";

    private static readonly Regex Endpoint = new(
        @"(?:https?|wss?)://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string DisplayText(string text) => Endpoint.Replace(text, Name);
}
