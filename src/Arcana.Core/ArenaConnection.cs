using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

/// <summary>Red independiente del motor. Todos los envíos, incluido el cierre, se serializan.</summary>
public sealed partial class ArenaConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _socketSend = new(1, 1);
    private readonly SemaphoreSlim _inspectionGate = new(1, 1);
    private sealed record PendingInspection(long Id, int PlayerId, TaskCompletionSource<EquipmentInspection> Completion);
    private PendingInspection? _inspection;
    private long _inspectionSequence;
    private readonly byte[] _receiveBuffer = new byte[8192];
    private readonly MemoryStream _receiveStream = new(8192);
    private readonly Channel<ClientMessage> _outgoing = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(256)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly object _sendGate = new(), _disposeGate = new();
    private readonly string _connectionId = Guid.NewGuid().ToString("N");
    private long _sequence;
    private bool _compactActions;
    private long _bytesSent, _bytesReceived;
    public long BytesSent => Interlocked.Read(ref _bytesSent);
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);
    private WorldSnapshot? _latest;
    private MatchStatisticsReport? _latestStatistics;
    public MatchStatisticsReport? LatestStatistics => Volatile.Read(ref _latestStatistics);
    public bool RequestMatchStatistics() => Send(new ClientMessage { Type = "statistics" });
    private SnapshotReceiver? _snapshotReceiver;
    public string ResumeToken { get; private set; } = "";
    private Task? _reader, _writer, _disposal, _connecting;
    private int _disposing, _connected, _notified;
    private int _ready, _readyRequested, _preparingPlayers;
    private bool _requiresReady;
    public int PlayerId { get; private set; }
    public ServerInfo? Server { get; private set; }
    public RoomInfo? Room { get; private set; }
    public WorldSnapshot? Latest => Volatile.Read(ref _latest);
    public bool Connected => Volatile.Read(ref _connected) == 1 && Volatile.Read(ref _disposing) == 0;
    public bool Ready => !_requiresReady || Volatile.Read(ref _ready) == 1;
    public int PreparingPlayers => Volatile.Read(ref _preparingPlayers);
    public event Action<string>? Disconnected;
    public event Action<string>? AccountChanged;
    private Dictionary<string, object?> Context() => new() { ["connection"] = _connectionId, ["roomId"] = Room?.Id,
        ["playerId"] = PlayerId, ["tick"] = Latest?.Tick };

    public static Uri Address(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        address = address.Trim();
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Introduce una IP o un nombre de servidor.");
        bool bare = !address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) && !address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);
        if (bare && address.Contains("://", StringComparison.Ordinal)) throw new ArgumentException("La dirección debe usar ws:// o wss://.");
        if (bare)
        {
            string literal = address.Trim('[', ']');
            if (IPAddress.TryParse(literal, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                address = $"[{ip}]:{Protocol.GamePort}";
            else
            {
                string authority = address.Split('/')[0];
                bool port = authority.StartsWith('[') ? authority.Contains("]:") : authority.Contains(':');
                if (!port)
                {
                    int slash = address.IndexOf('/');
                    address = slash < 0 ? address + ":" + Protocol.GamePort : address.Insert(slash, ":" + Protocol.GamePort);
                }
            }
            address = "ws://" + address;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss") ||
            uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Dirección inválida. Ejemplo: 192.168.1.20:27015. Usa la casilla de contraseña para las claves.");
        return uri.AbsolutePath == "/" ? new UriBuilder(uri) { Path = "/play" }.Uri : uri;
    }
    public async Task<EquipmentInspection> InspectEquipmentAsync(int playerId, CancellationToken token = default)
    {
        if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));
        await _inspectionGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        var request = new PendingInspection(Interlocked.Increment(ref _inspectionSequence), playerId,
            new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            Volatile.Write(ref _inspection, request);
            if (!Send(new() { Type = "inspect", InspectionRequestId = request.Id, InspectPlayerId = playerId }))
                throw new IOException("No hay conexión para consultar el equipamiento.");
            return await request.Completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally { Interlocked.CompareExchange(ref _inspection, null, request); _inspectionGate.Release(); }
    }
}
