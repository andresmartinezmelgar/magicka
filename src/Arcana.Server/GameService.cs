using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

internal sealed class PlayerSession(int id, WebSocket socket)
{
    public int Id { get; } = id;
    public WebSocket Socket { get; } = socket;
    public Channel<ClientMessage> Commands { get; } = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
}

public sealed class GameService(ServerSettings settings, ILogger<GameService> logger) : BackgroundService
{
    private readonly GameWorld _world = new(settings.World);
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, PlayerSession> _sessions = [];
    private readonly SemaphoreSlim _handshakes = new(32, 32);

    public ServerInfo Info()
    {
        lock (_gate) return new(settings.Name, settings.Port, settings.DiscoveryPort,
            settings.World.Mode, _world.Players.Count, settings.World.MaxPlayers, Protocol.Version, settings.Password.Length > 0);
    }
    public long CurrentTick { get { lock (_gate) return _world.Tick; } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Arcana Arena v0.1.0: {Mode}, {Players} jugadores, {Bots} bots", settings.World.Mode,
            settings.World.MaxPlayers, settings.World.Bots);
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds, accumulator = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                double now = clock.Elapsed.TotalSeconds;
                accumulator += Math.Min(0.15, now - previous); previous = now;
                WorldSnapshot? snapshot = null;
                lock (_gate)
                {
                    while (accumulator >= GameWorld.StepSeconds)
                    {
                        foreach (var session in _sessions.Values)
                            for (int n = 0; n < 32 && session.Commands.Reader.TryRead(out var message); n++)
                                _world.ApplyMessage(session.Id, message);
                        _world.Step(); accumulator -= GameWorld.StepSeconds;
                        if (_world.Tick % 3 == 0) snapshot = _world.Snapshot();
                    }
                }
                if (snapshot == null || _sessions.IsEmpty) continue;
                var bytes = Encoding.UTF8.GetBytes(Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot }));
                foreach (var session in _sessions.Values) session.Outbox.Writer.TryWrite(bytes);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            foreach (var session in _sessions.Values)
            {
                session.Outbox.Writer.TryComplete(); session.Socket.Abort();
            }
        }
    }

    public async Task HandleConnection(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!await _handshakes.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        PlayerSession? session = null;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await Receive(socket, handshake.Token);
            if (hello is not { Type: "hello" } || hello.Protocol != Protocol.Version)
            { await Reject(socket, "La versión del cliente no coincide con la del servidor.", context.RequestAborted); return; }
            if (!PasswordMatches(hello.Password, settings.Password))
            { await Reject(socket, "Contraseña incorrecta.", context.RequestAborted); return; }
            Wizard? player;
            lock (_gate) player = _world.AddPlayer(hello.Name, hello.Team);
            if (player == null) { await Reject(socket, "El servidor está lleno.", context.RequestAborted); return; }
            session = new(player.Id, socket);
            WorldSnapshot initial;
            lock (_gate) initial = _world.Snapshot();
            await Send(socket, new ServerMessage { Type = "welcome", PlayerId = player.Id, Server = Info(), Snapshot = initial }, context.RequestAborted);
            _sessions[player.Id] = session;
            logger.LogInformation("Conectado: {Name}, id {Id}", player.Name, player.Id);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, StoppingToken);
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
                    { await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Demasiados mensajes", connection.Token); break; }
                    if ((message.Name?.Length ?? 0) > 64 || (message.Password?.Length ?? 0) > 128) break;
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
                _sessions.TryRemove(session.Id, out _);
                lock (_gate) _world.RemovePlayer(session.Id);
                logger.LogInformation("Desconectado: id {Id}", session.Id);
            }
            if (socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fin de sesión", timeout.Token); }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            }
            _handshakes.Release();
        }
    }

    // BackgroundService no expone su token: se guarda en StartAsync para cancelar las conexiones al apagar.
    private CancellationToken StoppingToken => _stopping.Token;
    private readonly CancellationTokenSource _stopping = new();
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
            if (result.EndOfMessage) return Protocol.Deserialize<ClientMessage>(Encoding.UTF8.GetString(buffer, 0, size));
        }
    }
}
