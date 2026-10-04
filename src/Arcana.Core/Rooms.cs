using System.Net.Http.Json;
using System.Text;

namespace Arcana.Core;

public sealed record RoomInfo(string Id, string Name, GameMode Mode, int Players, int MaxPlayers,
    int Bots, bool FriendlyFire, int ScoreLimit, int Tickets, bool PasswordRequired, ArenaMapId Map = ArenaMapId.Classic);
public sealed record RoomDirectory(ServerInfo Server, RoomInfo[] Rooms);
public sealed record ApiError(string Error);
public sealed class CreateRoomRequest
{
    public string Name { get; set; } = "Nueva sala";
    public string ServerPassword { get; set; } = "";
    public string Password { get; set; } = "";
    public GameMode Mode { get; set; } = GameMode.Warfare;
    public ArenaMapId Map { get; set; } = ArenaMapId.Classic;
    public int MaxPlayers { get; set; } = 8;
    public int Bots { get; set; }
    public bool FriendlyFire { get; set; } = true;
    public int ScoreLimit { get; set; } = 15;
    public int Tickets { get; set; } = 75;
}

public static class RoomBrowser
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public static Uri Endpoint(string address, string operation)
    {
        var ws = ArenaConnection.Address(address);
        string prefix = ws.AbsolutePath[..(ws.AbsolutePath.LastIndexOf('/') + 1)];
        return new UriBuilder(ws) { Scheme = ws.Scheme == "wss" ? "https" : "http",
            Port = ws.Port, Path = prefix + operation, Query = "", Fragment = "" }.Uri;
    }
    public static async Task<RoomDirectory> ListAsync(string address, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(address, "rooms"));
        var directory = await Request<RoomDirectory>(request, token).ConfigureAwait(false);
        if (directory.Server == null || directory.Rooms is not { Length: <= 16 } || directory.Rooms.Any(r =>
            r == null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 64 || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 32 ||
            !Enum.IsDefined(r.Mode) || !Enum.IsDefined(r.Map) || r.MaxPlayers is < 2 or > 16 || r.Players < 0 || r.Players > r.MaxPlayers ||
            r.Bots < 0 || r.Bots >= r.MaxPlayers)) throw new InvalidDataException("Directorio de salas inválido.");
        if (directory.Server.Protocol != Protocol.Version) throw new InvalidOperationException("Actualiza el juego: la versión del servidor es diferente.");
        return directory;
    }
    public static async Task<RoomInfo> CreateAsync(string address, CreateRoomRequest room, CancellationToken token = default)
    {
        Diagnostics.Log.ProtectSecret(room.Password); Diagnostics.Log.ProtectSecret(room.ServerPassword);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(address, "rooms"))
        { Content = JsonContent.Create(room, options: Protocol.Json) };
        var result = await Request<RoomInfo>(request, token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result.Id) || result.Id.Length > 64) throw new InvalidDataException("El servidor no identificó la sala creada.");
        Diagnostics.Info("room.created", "Sala creada desde el cliente.", new Dictionary<string, object?> { ["roomId"] = result.Id, ["mode"] = result.Mode });
        return result;
    }
    private static async Task<T> Request<T>(HttpRequestMessage request, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(7));
        try
        {
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            string json = await ReadBoundedAsync(response.Content, 64 * 1024, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(Protocol.Deserialize<ApiError>(json)?.Error ?? "El servidor rechazó la solicitud.");
            return Protocol.Deserialize<T>(json) ?? throw new InvalidDataException("El servidor devolvió una respuesta inválida.");
        }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested) Diagnostics.Warn("http.request_failed", "Fallo al consultar el servidor.", e,
                new Dictionary<string, object?> { ["endpoint"] = request.RequestUri, ["operation"] = request.Method.Method });
            throw;
        }
    }
    public static async Task<string> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken token = default)
    {
        if (maximumBytes is < 1 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Respuesta del servidor demasiado grande.");
        await using var input = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); byte[] buffer = new byte[Math.Min(maximumBytes + 1, 8192)];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximumBytes + 1 - output.Length)), token).ConfigureAwait(false);
            if (read == 0) return Encoding.UTF8.GetString(output.ToArray());
            if (output.Length + read > maximumBytes) throw new InvalidDataException("Respuesta del servidor demasiado grande.");
            output.Write(buffer, 0, read);
        }
    }
}
