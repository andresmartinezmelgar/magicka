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
    public bool Started { get; set; }
    public string MatchId { get; set; } = Guid.NewGuid().ToString("N");
    public bool ResultQueued { get; set; }
    public bool SeriesFinished { get; set; }
    public bool AwaitingRematch { get; set; }
    public string RewardStatus { get; set; } = "";
    public int[] SeriesWins { get; } = new int[2];
    public int BestOf { get; set; } = 1;
    public int HostPlayerId { get; set; }
    public bool StartRequested { get; set; }
    public Dictionary<string, MatchParticipant> Participants { get; } = new(StringComparer.Ordinal);
    public RoomInfo Info() => new(Id, Name, Options.Mode, World.Players.Count, Options.MaxPlayers,
        Options.Bots, Options.FriendlyFire, Options.ScoreLimit, Options.Tickets, Password.Length > 0, Options.Map, BestOf, Started);
}
internal sealed class PlayerSession(int id, GameRoom room, WebSocket socket)
{
    // Los IDs de magos son locales a la sala. La sesión de transporte es única en todo el servidor.
    public Guid Key { get; } = Guid.NewGuid();
    public int Id { get; } = id;
    public GameRoom Room { get; } = room;
    public WebSocket Socket { get; } = socket;
    public ServerMessage? NetworkBaseline { get; set; }
    public bool Ready { get; set; }
    public bool LobbyReady { get; set; }
    public AccountIdentity? Account { get; init; }
    public PreparedEquipment Equipment { get; set; } = new(EquipmentModifiers.None, PublicEquipment.Empty);
    public SemaphoreSlim SendGate { get; } = new(1, 1);
    public long LastInspectionAt { get; set; }
    public int InspectionsInWindow { get; set; }
    public long LastActivityTick { get; set; }
    public long LoadingStartedAt { get; } = Environment.TickCount64;
    public Channel<ClientMessage> Commands { get; } = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(64)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
}

public sealed class GameService(ServerSettings settings, ILogger<GameService> logger, AccountService accounts, RewardService rewards) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GameRoom> _rooms = new(StringComparer.Ordinal)
    { ["default"] = new("default", "Sala principal", "", settings.World) };
    private readonly ConcurrentDictionary<Guid, PlayerSession> _sessions = [];
    private readonly SemaphoreSlim _handshakes = new(32, 32);
    private const int MaxRooms = 16;
    private readonly CancellationTokenSource _stopping = new();
    private long _tick, _ignoredCommands;

    public ServerInfo Info()
    {
        lock (_gate) return new(settings.Name, settings.Port, settings.DiscoveryPort,
            settings.World.Mode, _rooms.Values.Sum(r => r.World.Players.Count),
            _rooms.Values.Sum(r => r.Options.MaxPlayers), Protocol.Version, settings.Password.Length > 0, settings.InstanceId, BuildInfo.Version, accounts.GameEnabled);
    }
    public long CurrentTick => Interlocked.Read(ref _tick);
    public RoomDirectory ListRooms()
    { lock (_gate) return new(Info(), _rooms.Values.Where(r => r.Id != "default").Select(r => r.Info()).ToArray()); }

    public (RoomInfo? Room, string Error, int Status) CreateRoom(CreateRoomRequest? request)
    {
        if (request == null) return (null, "Faltan los datos de la sala.", 400);
        Diagnostics.Log.ProtectSecret(request.Password);
        Diagnostics.Log.ProtectSecret(request.ServerPassword);
        if (!PasswordMatches(request.ServerPassword, settings.Password)) return (null, "Contraseña del servidor incorrecta.", 403);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 32 || request.Name.Any(char.IsControl) ||
            request.Password == null || request.Password.Length > 128 || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Map) ||
            request.MaxPlayers is < 2 or > 16 || request.Bots < 0 || request.Bots >= request.MaxPlayers ||
            request.ScoreLimit is < 1 or > 200 || request.Tickets is < 5 or > 1000 ||
            request.BestOf is not (1 or 5 or 7 or 9) ||
            request.Mode == GameMode.Arena && request.BestOf > 1 ||
            (request.Mode == GameMode.Duel && request.MaxPlayers != 2))
            return (null, "Datos inválidos. Duelo requiere 2 plazas; deja al menos una plaza libre de bots.", 400);
        if (!ArenaMap.Get(request.Map).SupportsMode(request.Mode))
            return (null, "Frente de conquista requiere el modo Guerra; la victoria se consigue capturando sus cinco puntos.", 400);
        if (ArenaMap.Get(request.Map).IsPractice)
            return (null, "El laboratorio se inicia sin conexión desde la pestaña Práctica.", 400);
        lock (_gate)
        {
            RemoveIdleRooms();
            if (_rooms.Count >= MaxRooms)
            {
                var abandoned = _rooms.Values.Where(r => r.Id != "default" && r.Sessions.Count == 0)
                    .OrderBy(r => r.LastOccupied).FirstOrDefault();
                if (abandoned == null) return (null, "Todas las salas están ocupadas. Espera a que se libere alguna.", 409);
                _rooms.Remove(abandoned.Id);
                logger.LogInformation("Sala vacía sustituida: {RoomId}", abandoned.Id);
            }
            var options = new WorldOptions { Mode = request.Mode, Map = request.Map, MaxPlayers = request.MaxPlayers, Bots = request.Bots,
                FriendlyFire = request.FriendlyFire, ScoreLimit = request.ScoreLimit, Tickets = request.Tickets };
            var room = new GameRoom(Guid.NewGuid().ToString("N"), request.Name.Trim(), request.Password, options);
            room.BestOf = request.BestOf;
            _rooms.Add(room.Id, room);
            logger.LogInformation("Sala creada: {Name}, {Id}", room.Name, room.Id);
            return (room.Info(), "", 201);
        }
    }
    private void RemoveIdleRooms()
    {
        foreach (var room in _rooms.Values.Where(r => r.Id != "default" && r.Sessions.Count == 0 &&
            Environment.TickCount64 - r.LastOccupied > 2 * 60 * 1000).ToArray())
        {
            _rooms.Remove(room.Id);
            Diagnostics.Info("room.expired", "Sala vacía retirada.", new Dictionary<string, object?> { ["room"] = room.Id });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Arcana Arena {Version}: TCP {Port}, hasta {Rooms} salas", BuildInfo.Version, settings.Port, MaxRooms);
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds, accumulator = 0;
        long cleanupAt = Environment.TickCount64, metricsAt = cleanupAt, lagAt = 0;
        var snapshots = new List<(ServerMessage Message, PlayerSession[] Recipients, GameRoom Room)>(MaxRooms);
        var dirty = new HashSet<GameRoom>();
        double maxStepMs = 0;
        long snapshotBytes = 0, snapshotFrames = 0, compressedFrames = 0, fullEquivalentBytes = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(4));
        rewards.Delivered += NotifyAccounts;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                double now = clock.Elapsed.TotalSeconds;
                double elapsed = now - previous;
                if (elapsed > 0.15 && Environment.TickCount64 - lagAt > 10000)
                {
                    Diagnostics.Warn("simulation.lag", "La simulación ha sufrido un retraso.", context:
                        new Dictionary<string, object?> { ["tick"] = CurrentTick, ["elapsedMs"] = elapsed * 1000 });
                    lagAt = Environment.TickCount64;
                }
                accumulator += Math.Min(0.15, elapsed); previous = now;
                snapshots.Clear(); dirty.Clear();
                long stepStarted = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    while (accumulator >= GameWorld.StepSeconds)
                    {
                        foreach (var room in _rooms.Values)
                        {
                            // Pausa los bots y el reloj de las salas sin jugadores humanos.
                            if (room.Sessions.Count == 0) continue;
                            int readyPlayers = 0;
                            foreach (var session in room.Sessions.Values)
                            {
                                if (!session.Ready && Environment.TickCount64 - session.LoadingStartedAt > Protocol.LoadingTimeoutSeconds * 1000L)
                                    session.Socket.Abort();
                                for (int n = 0; n < 32 && session.Commands.Reader.TryRead(out var message); n++)
                                {
                                    if (message.Type == "ready" && message.Sequence > 0) { session.Ready = true; continue; }
                                    if (room.BestOf > 1 && !room.Started && session.Ready)
                                    {
                                        if (message.Type == "lobby-team" && message.Team is 0 or 1)
                                        {
                                            if (room.World.SetPlayerTeam(session.Id, message.Team))
                                            { session.LobbyReady = false; room.StartRequested = false; }
                                            continue;
                                        }
                                        if (message.Type == "lobby-ready")
                                        { session.LobbyReady = !session.LobbyReady; room.StartRequested = false; continue; }
                                        if (message.Type == "lobby-start" && session.Id == room.HostPlayerId)
                                        { room.StartRequested = true; continue; }
                                    }
                                    if (!room.Started && room.BestOf > 1) { _ignoredCommands++; continue; }
                                    if (!session.Ready || !room.World.ApplyMessage(session.Id, message)) _ignoredCommands++;
                                    else if (message.Type != "input" || message.MoveTo || Math.Abs(message.MoveX) + Math.Abs(message.MoveY) > 0.05f)
                                        session.LastActivityTick = room.World.Tick;
                                }
                                if (session.Ready) readyPlayers++;
                            }
                            if (readyPlayers == 0 || !room.Started && readyPlayers != room.Sessions.Count)
                            { if (CurrentTick % 3 == 0) dirty.Add(room); continue; }
                            if (room.BestOf > 1 && !room.Started)
                            {
                                bool enoughPlayers = room.World.Players.Count >= 2;
                                bool everyoneReady = room.Sessions.Values.All(s => s.LobbyReady);
                                bool teamsReady = room.Options.Mode == GameMode.Arena ||
                                    room.World.Players.Values.Any(p => p.Team == 0) &&
                                    room.World.Players.Values.Any(p => p.Team == 1);
                                if (!room.StartRequested || !enoughPlayers || !everyoneReady || !teamsReady)
                                { if (CurrentTick % 3 == 0) dirty.Add(room); continue; }
                            }
                            if (!room.Started && room.AwaitingRematch)
                            {
                                room.AwaitingRematch = false;
                                room.RewardStatus = "";
                            }
                            room.Started = true;
                            // Entrar en una partida iniciada no la pausa: el nuevo jugador
                            // permanece protegido y sin acciones hasta terminar su carga.
                            foreach (var session in room.Sessions.Values)
                                if (!session.Ready && room.World.Players.TryGetValue(session.Id, out var player)) player.Invulnerable = 1.2f;
                            bool endedBefore = room.World.MatchEnded;
                            room.World.Step();
                            TrackAccounts(room, endedBefore);
                            room.LastOccupied = Environment.TickCount64;
                            // Comunicar el desenlace inmediatamente a TODOS los participantes,
                            // sin esperar al siguiente ciclo de envío periódico.
                            // El estado se sigue replicando durante toda la cuenta atrás.
                            if (room.World.Tick % 3 == 0 || endedBefore != room.World.MatchEnded)
                                dirty.Add(room);
                        }
                        Interlocked.Increment(ref _tick);
                        accumulator -= GameWorld.StepSeconds;
                    }
                    // Si hubo varios pasos atrasados, envía únicamente el estado más reciente.
                    foreach (var room in dirty) snapshots.Add((RoomSnapshot(room), room.Sessions.Values.ToArray(), room));
                    if (Environment.TickCount64 - cleanupAt > 60000) { RemoveIdleRooms(); cleanupAt = Environment.TickCount64; }
                    maxStepMs = Math.Max(maxStepMs, Stopwatch.GetElapsedTime(stepStarted).TotalMilliseconds);
                    if (Environment.TickCount64 - metricsAt >= 30000)
                    {
                        Diagnostics.Info("server.metrics", "Estado periódico de la simulación.", new Dictionary<string, object?>
                        { ["tick"] = CurrentTick, ["rooms"] = _rooms.Count, ["connections"] = _sessions.Count,
                            ["activeRooms"] = _rooms.Values.Count(r => r.Sessions.Count > 0), ["maxStepMs"] = maxStepMs,
                            ["ignoredCommands"] = _ignoredCommands, ["managedBytes"] = GC.GetTotalMemory(false),
                             ["queuedSnapshotPayloadBytes"] = Interlocked.Read(ref snapshotBytes),
                             ["fullSnapshotEquivalentBytes"] = Interlocked.Read(ref fullEquivalentBytes),
                             ["snapshotRecipientFrames"] = Interlocked.Read(ref snapshotFrames),
                             ["compressedSnapshotFrames"] = Interlocked.Read(ref compressedFrames) });
                        metricsAt = Environment.TickCount64; maxStepMs = 0; _ignoredCommands = 0;
                        Interlocked.Exchange(ref snapshotBytes, 0); Interlocked.Exchange(ref snapshotFrames, 0);
                        Interlocked.Exchange(ref compressedFrames, 0);
                        Interlocked.Exchange(ref fullEquivalentBytes, 0);
                    }
                }
                foreach (var snapshot in snapshots)
                {
                    // Cada jugador recibe el estado de referencia en el saludo, una sola vez.
                    // Los deltas posteriores son independientes entre sí y se comparan siempre
                    // contra ese estado; DropOldest no invalida los siguientes fotogramas.
                    byte[]? full = null;
                    foreach (var session in snapshot.Recipients)
                    {
                        byte[] bytes;
                        if (session.NetworkBaseline is { } baseline)
                        {
                            byte[] delta = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(snapshot.Message, baseline));
                            full ??= Protocol.SerializeSnapshot(snapshot.Message);
                            bytes = delta.Length < full.Length ? delta : full;
                        }
                        else bytes = full ??= Protocol.SerializeSnapshot(snapshot.Message);
                        if (session.Outbox.Writer.TryWrite(bytes))
                        {
                            Interlocked.Add(ref snapshotBytes, bytes.Length);
                            Interlocked.Add(ref fullEquivalentBytes, (full ??= Protocol.SerializeSnapshot(snapshot.Message)).Length);
                            Interlocked.Increment(ref snapshotFrames);
                            if (bytes.Length > 0 && bytes[0] == 0xA1) Interlocked.Increment(ref compressedFrames);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            rewards.Delivered -= NotifyAccounts;
            foreach (var session in _sessions.Values) { session.Outbox.Writer.TryComplete(); session.Socket.Abort(); }
        }
    }

    public async Task HandleConnection(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!await _handshakes.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        bool handshakeSlotHeld = true;
        WebSocket? socket = null;
        PlayerSession? session = null;
        string connectionKey = Guid.NewGuid().ToString("N");
        Dictionary<string, object?> Context() => new() { ["connection"] = connectionKey, ["session"] = session?.Key,
            ["room"] = session?.Room.Id, ["player"] = session?.Id, ["tick"] = CurrentTick };
        async Task RejectLogged(string reason, CancellationToken token)
        {
            Diagnostics.Warn("connection.rejected", reason, context: Context());
            await Reject(socket!, reason, token);
        }
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            var receiveBuffer = new byte[Protocol.MaxMessageBytes + 1];
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await Receive(socket, receiveBuffer, handshake.Token);
            if (hello is not { Type: "hello" } || hello.Protocol != Protocol.Version)
            { await RejectLogged("La versión del cliente no coincide con la del servidor.", handshake.Token); return; }
            if (!string.Equals(hello.GameVersion, BuildInfo.Version, StringComparison.Ordinal))
            { await RejectLogged($"Versión incompatible. Servidor: {BuildInfo.Version}; cliente: {hello.GameVersion}. Actualiza el juego.", handshake.Token); return; }
            if (!Enum.IsDefined(hello.WizardType))
            { await RejectLogged("Tipo de mago desconocido.", handshake.Token); return; }
            if (!PasswordMatches(hello.Password, settings.Password))
            { await RejectLogged("Contraseña del servidor incorrecta.", handshake.Token); return; }
            AccountIdentity? identity = null; PreparedEquipment equipment = new(EquipmentModifiers.None, PublicEquipment.Empty);
            bool testGuest = Environment.GetEnvironmentVariable("ARCANA_CI_LOOPBACK_GUESTS") == "1" &&
                System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? System.Net.IPAddress.None);
            // Los servidores locales sin cuentas admiten invitados (sin recompensas
            // ni inventario persistente). Los servidores con Firebase requieren Google.
            if (hello.AccountToken.Length == 0 && accounts.GameEnabled && !testGuest)
            { await RejectLogged("Debes iniciar sesión con Google para jugar online.", handshake.Token); return; }
            if (hello.AccountToken.Length > 0 && accounts.GameEnabled)
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(20));
                Diagnostics.Log.ProtectSecret(hello.AccountToken);
                identity = await accounts.AuthenticateGame(hello.AccountToken, handshake.Token);
                if (identity == null) { await RejectLogged("La sesión de cuenta ha caducado. Inicia sesión de nuevo con Google.", handshake.Token); return; }
                var profile = await accounts.ProfileGame(identity, handshake.Token);
                equipment = PublicEquipment.Prepare(profile);
            }
            string error = ""; ServerMessage? welcome = null;
            lock (_gate)
            {
                if (identity != null && _sessions.Values.Any(s => s.Account?.Uid == identity.Uid)) error = "Tu cuenta ya está jugando en este servidor.";
                else if (hello.RoomId == null || !_rooms.TryGetValue(hello.RoomId, out var room)) error = "La sala ya no existe. Actualiza la lista.";
                else if (!PasswordMatches(hello.RoomPassword, room.Password)) error = "Contraseña de la sala incorrecta.";
                else if (room.BestOf > 1 && room.Started) error = "La serie está en curso. Espera al siguiente encuentro o crea otra sala.";
                else
                {
                    var player = room.World.AddPlayer(hello.Name, hello.Team, wizardType: hello.WizardType);
                    if (player == null) error = room.World.Players.Count >= room.Options.MaxPlayers
                        ? "La sala está llena." : "El equipo elegido está ocupado en este duelo. Elige el otro equipo o Automático.";
                    else
                    {
                        room.World.EquipPlayer(player.Id, equipment);
                        session = new(player.Id, room, socket) { Account = identity, Equipment = equipment, LastActivityTick = room.World.Tick };
                        if (room.HostPlayerId == 0) room.HostPlayerId = player.Id;
                        room.Sessions.Add(player.Id, session);
                        _sessions[session.Key] = session; room.LastOccupied = Environment.TickCount64;
                        player.Invulnerable = 1.2f;
                        welcome = RoomSnapshot(room); welcome.Type = "welcome"; welcome.PlayerId = player.Id;
                        welcome.Server = Info(); welcome.Room = room.Info(); welcome.RequiresReady = true;
                        session.NetworkBaseline = welcome;
                    }
                }
            }
            if (session == null) { await RejectLogged(error, handshake.Token); return; }
            // El límite protege solamente la fase de saludo. Las sesiones establecidas no deben ocuparlo.
            _handshakes.Release(); handshakeSlotHeld = false;
            await Send(socket, welcome!, handshake.Token);
            Diagnostics.Info("connection.accepted", "Jugador conectado.", Context());
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
            var sendTask = SendLoop(session, connection.Token);
            try
            {
                long window = Environment.TickCount64; int count = 0;
                while (socket.State == WebSocketState.Open && !connection.IsCancellationRequested)
                {
                    var message = await Receive(socket, receiveBuffer, connection.Token);
                    if (message == null) break;
                    long now = Environment.TickCount64;
                    if (now - window >= 1000) { window = now; count = 0; }
                    if (++count > 180)
                    { Diagnostics.Warn("connection.rate_limit", "Límite de mensajes o cola de comandos alcanzado.", context: Context()); socket.Abort(); break; }
                    if (message.Type == "inspect")
                    {
                        var response = new ServerMessage { Type = "equipment", InspectionRequestId = message.InspectionRequestId };
                        lock (_gate)
                        {
                            if (now - session.LastInspectionAt >= 1000) { session.LastInspectionAt = now; session.InspectionsInWindow = 0; }
                            if (++session.InspectionsInWindow > 10) response.Error = "Espera un momento antes de consultar otra ficha.";
                            else if (!session.Ready || message.InspectionRequestId <= 0 || !session.Room.World.Players.TryGetValue(message.InspectPlayerId, out var target))
                                response.Error = "Este mago ya no está disponible en la sala.";
                            else response.Inspection = new(target.Id, target.Name, target.WizardType, target.Bot, target.PublicEquipment);
                        }
                        // Respuesta fiable fuera de la simulación; no pasa por la cola de snapshots descartables.
                        await SendSession(session, Protocol.SerializeUtf8(response), connection.Token);
                    }
                    else if (!session.Commands.Writer.TryWrite(message))
                    { Diagnostics.Warn("connection.rate_limit", "Cola de comandos alcanzada.", context: Context()); socket.Abort(); break; }
                }
            }
            finally
            {
                connection.Cancel(); session.Outbox.Writer.TryComplete();
                try { await sendTask; } catch (OperationCanceledException) { }
                catch (WebSocketException e) { Diagnostics.Warn("connection.send_failed", "Fallo al enviar el estado.", e, Context()); }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_stopping.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
                Diagnostics.Warn("connection.timeout", "El saludo del cliente no llegó a tiempo.", context: Context());
        }
        catch (WebSocketException e) { Diagnostics.Warn("connection.transport_failed", "La conexión se interrumpió.", e, Context()); }
        catch (Exception e) when (e is JsonException or InvalidDataException)
        { Diagnostics.Warn("connection.invalid_message", "El cliente envió un mensaje inválido.", e, Context()); }
        finally
        {
            if (session != null)
            {
                _sessions.TryRemove(session.Key, out _);
                lock (_gate)
                {
                    session.Room.Sessions.Remove(session.Id);
                    session.Room.World.RemovePlayer(session.Id);
                    if (session.Room.HostPlayerId == session.Id)
                        session.Room.HostPlayerId = session.Room.Sessions.Keys.FirstOrDefault();
                    if (session.Room.BestOf > 1 && session.Room.Started && !session.Room.SeriesFinished)
                    {
                        // Una serie incompleta no puede continuar sin un rival.
                        // Permite recibir sustitutos sin regalar puntos ni recompensas.
                        session.Room.World.ResetForWaitingRoom();
                        session.Room.Started = false;
                        session.Room.StartRequested = false;
                        session.Room.SeriesWins[0] = session.Room.SeriesWins[1] = 0;
                        session.Room.SeriesFinished = false;
                        session.Room.ResultQueued = false;
                        session.Room.MatchId = Guid.NewGuid().ToString("N");
                        session.Room.Participants.Clear();
                        session.Room.AwaitingRematch = true;
                        session.Room.RewardStatus = "La serie se canceló al desconectarse un jugador. Preparad otra partida.";
                        foreach (var remaining in session.Room.Sessions.Values) remaining.LobbyReady = false;
                    }
                    if (!session.Room.Started) session.Room.StartRequested = false;
                }
                Diagnostics.Info("connection.closed", "Jugador desconectado.", Context());
            }
            if (socket?.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fin de sesión", timeout.Token); }
                catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            }
            socket?.Dispose();
            if (handshakeSlotHeld) _handshakes.Release();
        }
    }
    public override Task StopAsync(CancellationToken cancellationToken)
    { _stopping.Cancel(); return base.StopAsync(cancellationToken); }
    private void TrackAccounts(GameRoom room, bool endedBefore)
    {
        if (endedBefore && !room.World.MatchEnded)
        {
            room.ResultQueued = false;
            if (room.SeriesFinished)
            {
                room.SeriesWins[0] = room.SeriesWins[1] = 0;
                room.SeriesFinished = false;
                room.MatchId = Guid.NewGuid().ToString("N");
                room.Participants.Clear();
                room.Started = false;
                room.StartRequested = false;
                room.AwaitingRematch = true;
                foreach (var participant in room.Sessions.Values) participant.LobbyReady = false;
            }
            foreach (var session in room.Sessions.Values)
                room.World.EquipPlayer(session.Id, session.Equipment);
        }

        if (!room.World.MatchEnded)
        {
            foreach (var session in room.Sessions.Values)
            {
                if (session.Account == null || !session.Ready ||
                    !room.World.Players.TryGetValue(session.Id, out var player)) continue;
                var account = session.Account;
                if (!room.Participants.TryGetValue(account.Uid, out var entry))
                    entry = new(account.Uid, session.Id, player.Team, 0, false);
                room.Participants[account.Uid] = entry with
                {
                    PlayerId = player.Id, Team = player.Team,
                    ActiveSeconds = entry.ActiveSeconds + (room.World.Tick - session.LastActivityTick < 300 ||
                        player.Alive && (player.Channel != null || player.Charging != null ||
                        room.Options.Mode == GameMode.Warfare &&
                        room.World.Map.ControlPoints.Any(point => V2.Distance(player.Position, point) < 3))
                        ? GameWorld.StepSeconds : 0)
                };
            }
            return;
        }

        if (room.ResultQueued) return;
        room.ResultQueued = true;
        int roundWinner = room.World.Winner;
        int winningTeam = room.Options.Mode == GameMode.Arena
            ? room.World.Players.Values.FirstOrDefault(p => p.Id == roundWinner)?.Team ?? -1
            : roundWinner;
        room.SeriesFinished = winningTeam is 0 or 1 &&
            MatchSeries.RecordRound(room.SeriesWins, room.BestOf, winningTeam);
        if (!room.SeriesFinished) return;

        var participants = room.Participants.Values.Select(p => p with
        {
            Winner = room.Options.Mode == GameMode.Arena
                ? p.PlayerId == roundWinner
                : p.Team == winningTeam
        }).ToArray();
        int humans = room.World.Players.Values.Count(p => !p.Bot);
        // Premios solo al terminar toda la serie; ambos equipos humanos, mismo número,
        // sin bots, y cada participante autenticado con al menos 15 s de actividad.
        var match = new RewardMatch(room.MatchId, settings.InstanceId, room.Options.Mode,
            room.Options.Map, DateTimeOffset.UtcNow, participants);
        bool balanced = BalancedMatchRules.RewardsEnabled(
            room.World.Players.Values.Count(p => !p.Bot && p.Team == 0),
            room.World.Players.Values.Count(p => !p.Bot && p.Team == 1), 0, 0);
        if (!accounts.GameEnabled)
            room.RewardStatus = "Sin recompensa: el servidor no tiene Firebase habilitado.";
        else if (humans != participants.Length || room.World.Players.Values.Any(p => p.Bot) || !balanced)
            room.RewardStatus = "Sin recompensa: se requieren equipos humanos equilibrados, sin bots y con Google.";
        else if (RewardRules.Eligible(match).Length != participants.Length)
            room.RewardStatus = "Sin recompensa: todos deben acreditar al menos 15 s de actividad.";
        else
        {
            room.RewardStatus = "Recompensa pendiente de confirmar en Firebase.";
            rewards.Enqueue(match);
        }
        _ = RefreshEquipment(room.Sessions.Values.ToArray());
    }
    private async Task RefreshEquipment(PlayerSession[] sessions)
    {
        foreach (var session in sessions)
        {
            if (session.Account == null) continue;
            try
            {
                var profile = await accounts.ProfileGame(session.Account, _stopping.Token);
                var equipment = PublicEquipment.Prepare(profile);
                lock (_gate) session.Equipment = equipment;
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (Exception error) { logger.LogWarning(error, "No se pudo actualizar el equipo para la siguiente partida."); }
        }
    }
    private void NotifyAccounts(RewardMatch match)
    {
        var eligible = RewardRules.Eligible(match);
        lock (_gate)
        {
            foreach (var room in _rooms.Values)
                if (room.MatchId == match.Id)
                    room.RewardStatus = "Resultado registrado en Firebase. La recompensa del equipo ganador está guardada.";
            foreach (var session in _sessions.Values)
            {
                var participant = eligible.FirstOrDefault(p => p.Uid == session.Account?.Uid);
                if (participant == null) continue;
                session.Outbox.Writer.TryWrite(Protocol.SerializeUtf8(new ServerMessage { Type = "account",
                    AccountNotice = participant.Winner ? "Recompensa PvP guardada. Revisa tu inventario." : "Resultado PvP guardado." }));
            }
        }
    }
    private static ServerMessage RoomSnapshot(GameRoom room) => new()
    {
        Type = "snapshot", Snapshot = room.World.Snapshot() with
        {
            WaitingForPlayers = room.BestOf > 1 && !room.Started,
            LobbyHostId = room.HostPlayerId,
            LobbyReadyPlayerIds = room.Sessions.Values.Where(s => s.LobbyReady).Select(s => s.Id).ToArray(),
            BestOf = room.BestOf,
            SeriesWins = room.SeriesWins.ToArray(),
            RewardStatus = room.RewardStatus,
            AwaitingRematch = room.AwaitingRematch
        },
        PreparingPlayers = room.Started ? 0 : room.Sessions.Values.Count(s => !s.Ready),
        ReadyPlayerIds = room.Sessions.Values.Where(s => s.Ready).Select(s => s.Id).ToArray()
    };
    private static bool PasswordMatches(string? supplied, string expected)
    {
        var a = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? ""));
        var b = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static async Task SendLoop(PlayerSession session, CancellationToken token)
    {
        await foreach (var data in session.Outbox.Reader.ReadAllAsync(token))
            await SendSession(session, data, token);
    }
    private static async Task SendSession(PlayerSession session, byte[] data, CancellationToken token)
    {
        await session.SendGate.WaitAsync(token);
        try { await session.Socket.SendAsync(data.AsMemory(), data.Length > 0 && data[0] == 0xA1 ? WebSocketMessageType.Binary : WebSocketMessageType.Text, true, token); }
        finally { session.SendGate.Release(); }
    }
    private static Task Send(WebSocket socket, ServerMessage message, CancellationToken token) =>
        socket.SendAsync(Protocol.SerializeUtf8(message), WebSocketMessageType.Text, true, token);
    private static async Task Reject(WebSocket socket, string reason, CancellationToken token)
    {
        await Send(socket, new ServerMessage { Type = "error", Error = reason }, token);
        await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, token);
    }
    private static async Task<ClientMessage?> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
    {
        int size = 0;
        bool binary = false;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(size), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary)) throw new InvalidDataException("Tipo de mensaje no admitido.");
            if (size > 0 && binary != (result.MessageType == WebSocketMessageType.Binary)) throw new InvalidDataException("Fragmentos incompatibles.");
            binary = result.MessageType == WebSocketMessageType.Binary;
            size += result.Count;
            if (size > Protocol.MaxMessageBytes) throw new InvalidDataException("Mensaje demasiado grande.");
            if (result.EndOfMessage)
            {
                var message = binary ? Protocol.DeserializeBinaryAction(buffer.AsSpan(0, size)) : Protocol.Deserialize<ClientMessage>(buffer.AsSpan(0, size));
                if (message != null && ((message.Name?.Length ?? 0) > 64 || (message.Password?.Length ?? 0) > 128 ||
                    (message.RoomPassword?.Length ?? 0) > 128 || (message.RoomId?.Length ?? 0) > 64 ||
                    message.AccountToken == null || message.AccountToken.Length > 8192))
                    throw new InvalidDataException("Datos demasiado largos.");
                return message;
            }
        }
    }
}
