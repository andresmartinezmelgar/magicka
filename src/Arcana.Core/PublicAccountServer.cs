using System.Globalization;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Directorio público de PvP y anuncios temporales de anfitriones.</summary>
public static class PublicAccountServer
{
    public static bool NeedsDiscovery(string address, bool mobile) => address == "automatic" || mobile &&
        Uri.TryCreate(address.Contains("://") ? address : "ws://" + address, UriKind.Absolute, out var uri) && uri.IsLoopback;

    /// <summary>Never replace a LAN server with another publicly announced instance.</summary>
    public static bool SameInstance(ServerInfo selected, ServerInfo official) =>
        !string.IsNullOrWhiteSpace(selected.InstanceId) &&
        string.Equals(selected.InstanceId, official.InstanceId, StringComparison.Ordinal) &&
        selected.Protocol == official.Protocol &&
        string.Equals(selected.GameVersion, official.GameVersion, StringComparison.Ordinal);

    public const string Project = "arcana-arena-f2997";
    public const string DocumentPath = "publicConfig/pvpServer";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DirectoryEntry> Entries = new();
    private sealed class DirectoryEntry
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal string Address = "";
        internal long Expires;
    }

    private static string Documents(string projectId)
    {
        if (projectId.Length is < 6 or > 30 || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto de cuentas inválido.");
        return $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/";
    }

    public static async Task<string> Resolve(string projectId, CancellationToken token = default)
    {
        string url = Documents(projectId) + DocumentPath;
        var entry = Entries.GetOrAdd(projectId, _ => new());
        await entry.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Environment.TickCount64 < entry.Expires && entry.Address.Length > 0) return entry.Address;
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                if (!response.IsSuccessStatusCode) throw new IOException("No se ha podido leer la dirección PvP en Firebase. Comprueba la conexión y los permisos del directorio.");
                using var doc = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 8192, token).ConfigureAwait(false));
                var fields = doc.RootElement.GetProperty("fields");
                if (fields.EnumerateObject().Count() != 1 || !fields.TryGetProperty("url", out var value) ||
                    !value.TryGetProperty("stringValue", out var text)) throw new InvalidDataException("El directorio PvP no es válido.");
                entry.Address = ValidateAddress(text.GetString());
                entry.Expires = Environment.TickCount64 + 60_000;
                return entry.Address;
            }
        }
        finally { entry.Gate.Release(); }
        return await ResolveTunnel(projectId, token).ConfigureAwait(false);
    }

    public static string ValidateAddress(string? address)
    {
        if (address is not { Length: <= 256 } || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme != "wss" || uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback ||
            uri.Port != 443 || uri.AbsolutePath != "/play" || address != "wss://" + uri.IdnHost + "/play" ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.IdnHost, @"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?[.])+[a-z]{2,63}$") ||
            uri.IdnHost.EndsWith(".local", StringComparison.Ordinal) || uri.IdnHost.EndsWith(".localhost", StringComparison.Ordinal))
            throw new InvalidDataException("La dirección PvP publicada debe ser una URL WSS pública con ruta /play.");
        return address;
    }

    public static async Task<string> ResolveTunnel(string projectId, CancellationToken token = default)
    {
        using var response = await Http.GetAsync(Documents(projectId) + "publicAccountServer/current",
            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new IOException("El servidor de cuentas está apagado o todavía se está preparando. Inténtalo en unos segundos.");
        return Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 8192, token).ConfigureAwait(false), DateTimeOffset.UtcNow);
    }

    public static async Task<(string Address, RoomDirectory Rooms)> ForInstance(ServerInfo selected, CancellationToken token)
    {
        string? stable = null;
        try
        {
            stable = await Resolve(Project, token).ConfigureAwait(false);
            var rooms = await RoomBrowser.ListAsync(stable, token).ConfigureAwait(false);
            if (SameInstance(selected, rooms.Server)) return (stable, rooms);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or IOException or TaskCanceledException)
        { /* An independently hosted LAN instance can still have a valid temporary announcement. */ }
        string tunnel = await ResolveTunnel(Project, token).ConfigureAwait(false);
        if (tunnel != stable)
        {
            var rooms = await RoomBrowser.ListAsync(tunnel, token).ConfigureAwait(false);
            if (SameInstance(selected, rooms.Server)) return (tunnel, rooms);
        }
        throw new InvalidOperationException("La sala LAN no coincide con el servidor seguro anunciado en Firebase.");
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
