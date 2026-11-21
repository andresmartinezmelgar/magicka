using System.Text.RegularExpressions;

namespace Arcana.Client;

/// <summary>Destino de PvP; la interfaz utiliza su nombre, nunca su dirección.</summary>
internal static class PvpServer
{
    internal const string Automatic = "automatic";
    internal const string Name = "Servidor PvP";

    internal static string DisplayText(string text, string? verifiedHost = null)
    {
        string host = verifiedHost ?? Arcana.Core.PublicAccountServer.AdvertisedHost;
        if (host.Length == 0 || !text.Contains(host, StringComparison.OrdinalIgnoreCase)) return text;
        string pattern = @"(?<![a-z0-9.-])(?:(?:https?|wss?)://)?" + Regex.Escape(host) +
            @"(?![a-z0-9.-])(?::\d+)?(?:/[^\s""'<>]*)?";
        return Regex.Replace(text, pattern, Name, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
