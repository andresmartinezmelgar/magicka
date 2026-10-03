using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

internal sealed class GameRoom(string id, string name, string password, WorldOptions options)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Password { get; } = password;
    public WorldOptions Options { get; } = options;
    public GameWorld World { get; } = new(options);
    public Dictionary<int, PlayerSession> Sessions { get; } = [];
    public long LastOccupied { get; set; } = Environment.TickCount64;
    public RoomInfo Info() => new(Id, Name, Options.Mode, World.Players.Count, Options.MaxPlayers,
        Options.Bots, Options.FriendlyFire, Options.ScoreLimit, Options.Tickets, Password.Length > 0);
}
internal sealed class PlayerSession(int id, GameRoom room, WebSocket socket)
{
    // Los IDs de magos son locales a la sala. La sesión de transporte es única en todo el servidor.
    public Guid Key { get; } = Guid.NewGuid();
    public int Id { get; } = id;
    public GameRoom Room { get; } = room;
    public WebSocket Socket { get; } = socket;
    public Channel<ClientMessage> Commands { get; } = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
}

public sealed class GameService(ServerSettings settings, ILogger<GameService> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GameRoom> _rooms = new(StringComparer.Ordinal)
    { ["default"] = new("default", "Sala principal", "", settings.World) };
    private readonly ConcurrentDictionary<Guid, PlayerSession> _sessions = [];
    private readonly SemaphoreSlim _handshakes = new(32, 32);
    private const int MaxRooms = 16;
    private readonly CancellationTokenSource _stopping = new();

    public ServerInfo Info()
    {
        lock (_gate) return new(settings.Name, settings.Port, settings.DiscoveryPort,
            settings.World.Mode, _rooms.Values.Sum(r => r.World.Players.Count),
            _rooms.Values.Sum(r => r.Options.MaxPlayers), Protocol.Version, settings.Password.Length > 0);
    }
    public long CurrentTick { get { lock (_gate) return _rooms["default"].World.Tick; } }
    public RoomDirectory ListRooms()
    { lock (_gate) return new(Info(), _rooms.Values.Select(r => r.Info()).ToArray()); }

    public (RoomInfo? Room, string Error, int Status) CreateRoom(CreateRoomRequest? request)
    {
        if (request == null) return (null, "Faltan los datos de la sala.", 400);
        if (!PasswordMatches(request.ServerPassword, settings.Password)) return (null, "Contraseña del servidor incorrecta.", 403);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 32 || request.Name.Any(char.IsControl) ||
            request.Password == null || request.Password.Length > 128 || !Enum.IsDefined(request.Mode) ||
            request.MaxPlayers is < 2 or > 16 || request.Bots < 0 || request.Bots >= request.MaxPlayers ||
            request.ScoreLimit is < 1 or > 200 || request.Tickets is < 5 or > 1000 ||
            (request.Mode == GameMode.Duel && request.MaxPlayers != 2))
            return (null, "Datos inválidos. Duelo requiere 2 plazas; deja al menos una plaza libre de bots.", 400);
        lock (_gate)
        {
            RemoveIdleRooms();
            if (_rooms.Count >= MaxRooms) return (null, "El servidor ha alcanzado su límite de 16 salas.", 409);
            var options = new WorldOptions { Mode = request.Mode, MaxPlayers = request.MaxPlayers, Bots = request.Bots,
                FriendlyFire = request.FriendlyFire, ScoreLimit = request.ScoreLimit, Tickets = request.Tickets };
            var room = new GameRoom(Guid.NewGuid().ToString("N"), request.Name.Trim(), request.Password, options);
            _rooms.Add(room.Id, room);
            logger.LogInformation("Sala creada: {Name}, {Id}", room.Name, room.Id);
            return (room.Info(), "", 201);
        }
    }
    private void RemoveIdleRooms()
    {
        foreach (var room in _rooms.Values.Where(r => r.Id != "default" && r.Sessions.Count == 0 &&
            Environment.TickCount64 - r.LastOccupied > 15 * 60 * 1000).ToArray()) _rooms.Remove(room.Id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Arcana Arena v0.2.0: TCP {Port}, hasta {Rooms} salas", settings.Port, MaxRooms);
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds, accumulator = 0;
        long cleanupAt = Environment.TickCount64;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                double now = clock.Elapsed.TotalSeconds;
                accumulator += Math.Min(0.15, now - previous); previous = now;
                var snapshots = new Dictionary<GameRoom, (WorldSnapshot World, PlayerSession[] Recipients)>();
                lock (_gate)
                {
                    while (accumulator >= GameWorld.StepSeconds)
                    {
                        foreach (var room in _rooms.Values)
                        {
                            foreach (var session in room.Sessions.Values)
                                for (int n = 0; n < 32 && session.Commands.Reader.TryRead(out var message); n++)
                                    room.World.ApplyMessage(session.Id, message);
                            room.World.Step();
                            if (room.Sessions.Count > 0)
                            {
                                room.LastOccupied = Environment.TickCount64;
                                if (room.World.Tick % 3 == 0) snapshots[room] = (room.World.Snapshot(), room.Sessions.Values.ToArray());
                            }
                        }
                        accumulator -= GameWorld.StepSeconds;
                    }
                    if (Environment.TickCount64 - cleanupAt > 60000) { RemoveIdleRooms(); cleanupAt = Environment.TickCount64; }
                }
                foreach (var (room, snapshot) in snapshots)
                {
                    var bytes = Encoding.UTF8.GetBytes(Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot.World }));
                    foreach (var session in snapshot.Recipients) session.Outbox.Writer.TryWrite(bytes);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var session in _sessions.Values) { session.Outbox.Writer.TryComplete(); session.Socket.Abort(); }
        }
    }

    public async Task HandleConnection(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!await _handshakes.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        WebSocket? socket = null;
        PlayerSession? session = null;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await Receive(socket, handshake.Token);
            if (hello is not { Type: "hello" } || hello.Protocol != Protocol.Version)
            { await Reject(socket, "La versión del cliente no coincide con la del servidor.", handshake.Token); return; }
            if (!PasswordMatches(hello.Password, settings.Password))
            { await Reject(socket, "Contraseña del servidor incorrecta.", handshake.Token); return; }
            string error = ""; WorldSnapshot? initial = null; RoomInfo? roomInfo = null;
            lock (_gate)
            {
                if (hello.RoomId == null || !_rooms.TryGetValue(hello.RoomId, out var room)) error = "La sala ya no existe. Actualiza la lista.";
                else if (!PasswordMatches(hello.RoomPassword, room.Password)) error = "Contraseña de la sala incorrecta.";
                else
                {
                    var player = room.World.AddPlayer(hello.Name, hello.Team);
                    if (player == null) error = "La sala está llena.";
                    else
                    {
                        session = new(player.Id, room, socket); room.Sessions.Add(player.Id, session);
                        _sessions[session.Key] = session; room.LastOccupied = Environment.TickCount64;
                        initial = room.World.Snapshot(); roomInfo = room.Info();
                    }
                }
            }
            if (session == null) { await Reject(socket, error, handshake.Token); return; }
            await Send(socket, new ServerMessage { Type = "welcome", PlayerId = session.Id, Server = Info(), Room = roomInfo,
                Snapshot = initial }, handshake.Token);
            logger.LogInformation("Conectado: sala {Room}, id {Id}", session.Room.Name, session.Id);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            var sendTask = SendLoop(session, connection.Token);
            try
            {
                long window = Environment.TickCount64; int count = 0;
                while (socket.State == WebSocketState.Open && !connection.IsCancellationRequested)
                {
                    var message = await Receive(socket, connection.Token);
                    if (message == null) break;
                    long now = Environment.TickCount64;
                    if (now - window >= 1000) { window = now; count = 0; }
                    if (++count > 180 || !session.Commands.Writer.TryWrite(message))
                    { socket.Abort(); break; }
                }
            }
            finally
            {
                connection.Cancel(); session.Outbox.Writer.TryComplete();
                try { await sendTask; } catch (OperationCanceledException) { } catch (WebSocketException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException) { }
        catch (InvalidDataException) { }
        finally
        {
            if (session != null)
            {
                _sessions.TryRemove(session.Key, out _);
                lock (_gate) { session.Room.Sessions.Remove(session.Id); session.Room.World.RemovePlayer(session.Id); }
                logger.LogInformation("Desconectado: sala {Room}, id {Id}", session.Room.Name, session.Id);
            }
            if (socket?.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fin de sesión", timeout.Token); }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            }
            socket?.Dispose();
            _handshakes.Release();
        }
    }
    public override Task StopAsync(CancellationToken cancellationToken)
    { _stopping.Cancel(); return base.StopAsync(cancellationToken); }
    private static bool PasswordMatches(string? supplied, string expected)
    {
        var a = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? ""));
        var b = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static async Task SendLoop(PlayerSession session, CancellationToken token)
    {
        await foreach (var data in session.Outbox.Reader.ReadAllAsync(token))
            await session.Socket.SendAsync(data.AsMemory(), WebSocketMessageType.Text, true, token);
    }
    private static Task Send(WebSocket socket, ServerMessage message, CancellationToken token) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(message)), WebSocketMessageType.Text, true, token);
    private static async Task Reject(WebSocket socket, string reason, CancellationToken token)
    {
        await Send(socket, new ServerMessage { Type = "error", Error = reason }, token);
        await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, token);
    }
    private static async Task<ClientMessage?> Receive(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[Protocol.MaxMessageBytes + 1]; int size = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(size), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Solo se permite JSON UTF-8.");
            size += result.Count;
            if (size > Protocol.MaxMessageBytes) throw new InvalidDataException("Mensaje demasiado grande.");
            if (result.EndOfMessage)
            {
                var message = Protocol.Deserialize<ClientMessage>(Encoding.UTF8.GetString(buffer, 0, size));
                if (message != null && ((message.Name?.Length ?? 0) > 64 || (message.Password?.Length ?? 0) > 128 ||
                    (message.RoomPassword?.Length ?? 0) > 128 || (message.RoomId?.Length ?? 0) > 64))
                    throw new InvalidDataException("Datos demasiado largos.");
                return message;
            }
        }
    }
}
