using System.Net.Http.Json;

namespace Arcana.Core;

public sealed record RoomInfo(string Id, string Name, GameMode Mode, int Players, int MaxPlayers,
    int Bots, bool FriendlyFire, int ScoreLimit, int Tickets, bool PasswordRequired);
public sealed record RoomDirectory(ServerInfo Server, RoomInfo[] Rooms);
public sealed record ApiError(string Error);
public sealed class CreateRoomRequest
{
    public string Name { get; set; } = "Nueva sala";
    public string ServerPassword { get; set; } = "";
    public string Password { get; set; } = "";
    public GameMode Mode { get; set; } = GameMode.Warfare;
    public int MaxPlayers { get; set; } = 8;
    public int Bots { get; set; }
    public bool FriendlyFire { get; set; } = true;
    public int ScoreLimit { get; set; } = 15;
    public int Tickets { get; set; } = 75;
}

/// <summary>Directorio de salas. La contraseña de servidor autoriza crear salas y entrar.</summary>
public static class RoomBrowser
{
    public static Uri Endpoint(string address, string operation)
    {
        var ws = ArenaConnection.Address(address);
        // Conserva el prefijo de un proxy: wss://host/arcana/play -> https://host/arcana/rooms.
        string prefix = ws.AbsolutePath[..(ws.AbsolutePath.LastIndexOf('/') + 1)];
        return new UriBuilder(ws) { Scheme = ws.Scheme == "wss" ? "https" : "http",
            Port = ws.Port, Path = prefix + operation, Query = "", Fragment = "" }.Uri;
    }
    public static async Task<RoomDirectory> ListAsync(string address, CancellationToken token = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        using var response = await http.GetAsync(Endpoint(address, "rooms"), token).ConfigureAwait(false);
        var directory = await Read<RoomDirectory>(response, token).ConfigureAwait(false);
        if (directory.Server.Protocol != Protocol.Version)
            throw new InvalidOperationException("Actualiza el juego: la versión del servidor es diferente.");
        return directory;
    }
    public static async Task<RoomInfo> CreateAsync(string address, CreateRoomRequest room, CancellationToken token = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(7) };
        using var response = await http.PostAsJsonAsync(Endpoint(address, "rooms"), room, Protocol.Json, token).ConfigureAwait(false);
        return await Read<RoomInfo>(response, token).ConfigureAwait(false);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken token)
    {
        string json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (json.Length > 64 * 1024) throw new IOException("Respuesta del servidor demasiado grande.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(Protocol.Deserialize<ApiError>(json)?.Error ?? "El servidor rechazó la solicitud.");
        return Protocol.Deserialize<T>(json) ?? throw new IOException("El servidor devolvió una respuesta inválida.");
    }
}
