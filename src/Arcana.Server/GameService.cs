using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

public sealed partial class GameService(ServerSettings settings, ILogger<GameService> logger, AccountService accounts, RewardService rewards, MatchStatisticsService statistics) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, GameRoom> _rooms = new(StringComparer.Ordinal)
    { ["default"] = new(RoomGeneration.Scope(settings.Generation, "default"), "Sala principal", "", settings.World) };
    private readonly ConcurrentDictionary<Guid, PlayerSession> _sessions = [];
    private readonly SemaphoreSlim _handshakes = new(32, 32);
    private readonly SemaphoreSlim _roomGeneration = new(2, 2);
    private const int MaxRooms = 16;
    private readonly CancellationTokenSource _stopping = new();
    private long _tick, _ignoredCommands;

    public ServerInfo Info()
    {
        lock (_gate) return new(settings.Name, settings.AdvertisedPort == 0 ? settings.Port : settings.AdvertisedPort,
            settings.AdvertisedDiscoveryPort == 0 ? settings.DiscoveryPort : settings.AdvertisedDiscoveryPort,
            settings.World.Mode, _rooms.Values.Sum(r => r.World.Players.Count),
            _rooms.Values.Sum(r => r.Options.MaxPlayers), Protocol.Version, settings.Password.Length > 0, settings.InstanceId, BuildInfo.Version, accounts.GameEnabled);
    }
    public long CurrentTick => Interlocked.Read(ref _tick);
    public RoomDirectory ListRooms()
    { lock (_gate) return new(Info(), _rooms.Where(r => r.Key != "default").Select(r => r.Value.Info()).ToArray()); }

    public (RoomInfo? Room, string Error, int Status) CreateRoom(CreateRoomRequest? request)
    {
        if (Volatile.Read(ref _draining)) return (null, "Iniciando servidor con nueva versión. Actualiza la lista de salas.", 503);
        if (request == null) return (null, "Faltan los datos de la sala.", 400);
        Diagnostics.Log.ProtectSecret(request.Password);
        Diagnostics.Log.ProtectSecret(request.ServerPassword);
        if (!PasswordMatches(request.ServerPassword, settings.Password)) return (null, "Contraseña del servidor incorrecta.", 403);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 32 || request.Name.Any(char.IsControl) ||
            request.Password == null || request.Password.Length > 128 || !Enum.IsDefined(request.Mode) || !Enum.IsDefined(request.Map) ||
            request.MaxPlayers < (request.Mode == GameMode.Defense ? 1 : 2) || request.MaxPlayers > (request.Mode == GameMode.Defense ? DefenseRules.MaxDefenders : 16) || request.Bots < 0 || request.Bots >= request.MaxPlayers ||
            request.ScoreLimit is < 1 or > 200 || request.Tickets is < 5 or > 1000 ||
            request.BestOf is not (1 or 5 or 7 or 9) ||
            (request.Mode is GameMode.Arena or GameMode.Defense) && request.BestOf > 1 || !Enum.IsDefined(request.DefenseFlow) ||
            (request.Mode == GameMode.Duel && request.MaxPlayers != 2))
            return (null, "Datos inválidos. Duelo requiere 2 plazas; deja al menos una plaza libre de bots.", 400);
        if (!ArenaMap.Get(request.Map).SupportsMode(request.Mode))
            return (null, "Frente de conquista requiere el modo Guerra; la victoria se consigue capturando sus cinco puntos.", 400);
        if (ArenaMap.Get(request.Map).IsPractice)
            return (null, "El laboratorio se inicia sin conexión desde la pestaña Práctica.", 400);
        var options = new WorldOptions { Mode = request.Mode, Map = request.Map, MaxPlayers = request.MaxPlayers, Bots = request.Bots,
            FriendlyFire = request.Mode != GameMode.Defense && request.FriendlyFire, ScoreLimit = request.ScoreLimit, Tickets = request.Tickets, DefenseFlow = request.DefenseFlow };
        // La generación de mapas no mantiene bloqueadas las partidas existentes.
        if (!_roomGeneration.Wait(0)) return (null, "Hay otras salas preparándose. Espera unos segundos.", 429);
        GameRoom candidate;
        string localId = Guid.NewGuid().ToString("N");
        try { candidate = new GameRoom(RoomGeneration.Scope(settings.Generation, settings.Generation.Length == 0 ? localId : localId[..24]), request.Name.Trim(), request.Password, options) { BestOf = request.BestOf }; }
        finally { _roomGeneration.Release(); }
        lock (_gate)
        {
            if (!AcceptingNewGames) return (null, UpdatingMessage, 503);
            RemoveIdleRooms();
            if (_rooms.Count >= MaxRooms)
            {
                var abandoned = _rooms.Values.Where(r => r != _rooms["default"] && r.Sessions.Count == 0 && r.Reservations.Count == 0)
                    .OrderBy(r => r.LastOccupied).FirstOrDefault();
                if (abandoned == null) return (null, "Todas las salas están ocupadas. Espera a que se libere alguna.", 409);
                _rooms.Remove(abandoned.Id);
                logger.LogInformation("Sala vacía sustituida: {RoomId}", abandoned.Id);
            }
            var room = candidate;
            _rooms.Add(room.Id, room);
            logger.LogInformation("Sala creada: {Name}, {Id}", room.Name, room.Id);
            return (room.Info(), "", 201);
        }
    }
    private void RemoveIdleRooms()
    {
        foreach (var room in _rooms.Values.Where(r => r != _rooms["default"] && r.Sessions.Count == 0 && r.Reservations.Count == 0 &&
            Environment.TickCount64 - r.LastOccupied > 2 * 60 * 1000).ToArray())
        {
            _rooms.Remove(room.Id);
            Diagnostics.Info("room.expired", "Sala vacía retirada.", new Dictionary<string, object?> { ["room"] = room.Id });
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] writes;
        lock (_gate)
        {
            foreach (var room in _rooms.Values)
                { if (!room.StatisticsFinal) CaptureStatistics(room, MatchRecordStatus.Interrupted);
                foreach (var session in room.Sessions.Values) SaveSessionBestiary(session, force: true); }
            writes = _rooms.Values.SelectMany(r => r.PendingRewardWrites.Values.Cast<Task>())
                .Concat(_sessions.Values.Where(s => s.BestiaryWrite != null).Select(s => (Task)s.BestiaryWrite!)).ToArray();
        }
        _stopping.Cancel(); await base.StopAsync(cancellationToken);
        await Task.WhenAll(writes.Concat(_persistenceWrites.Values)).WaitAsync(cancellationToken);
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
}
