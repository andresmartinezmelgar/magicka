using System.Globalization;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Only the public, short-lived server address is read without authentication.</summary>
public static class PublicAccountServer
{
    public static bool NeedsDiscovery(string address, bool mobile) => address == "automatic" || mobile &&
        Uri.TryCreate(address.Contains("://") ? address : "ws://" + address, UriKind.Absolute, out var uri) && uri.IsLoopback;

    public static async Task<string> Resolve(string projectId, CancellationToken token = default)
    {
        if (projectId.Length is < 6 or > 30 || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto de cuentas inválido.");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
        using var response = await http.GetAsync($"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/publicAccountServer/current", token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("El servidor de cuentas está apagado o todavía se está preparando. Inténtalo en unos segundos.");
        return Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 8192, token).ConfigureAwait(false), DateTimeOffset.UtcNow);
    }

    public static string Parse(string json, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement.GetProperty("fields");
        if (fields.EnumerateObject().Count() != 3 || fields.EnumerateObject().Any(f => f.Name != "url" && f.Name != "expiresAt" && f.Name != "protocol"))
            throw new InvalidDataException("El anuncio del servidor contiene campos inesperados.");
        string address = fields.GetProperty("url").GetProperty("stringValue").GetString() ?? "";
        string expiry = fields.GetProperty("expiresAt").GetProperty("timestampValue").GetString() ?? "";
        string protocol = fields.GetProperty("protocol").GetProperty("integerValue").GetString() ?? "";
        if (!DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expires) ||
            expires <= now || expires > now.AddMinutes(3)) throw new IOException("El servidor de cuentas está apagado. Vuelve a intentarlo cuando esté disponible.");
        if (!int.TryParse(protocol, out var version) || version != Protocol.Version)
            throw new IOException("Actualiza la app: el servidor utiliza otra versión del protocolo.");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "wss" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/play" ||
            !uri.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La dirección publicada para las cuentas no es válida.");
        return uri.AbsoluteUri;
    }
}
