using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

/// <summary>Red independiente del motor. Todos los envíos, incluido el cierre, se serializan.</summary>
public sealed class ArenaConnection : IAsyncDisposable
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
    private WorldSnapshot? _latest;
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
    public Task ConnectAsync(string address, string name, int team, string password, CancellationToken token = default,
        string roomId = "default", string roomPassword = "", WizardType wizardType = WizardType.Balanced, bool deferReady = false, string accountToken = "")
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
            if (_connecting != null) throw new InvalidOperationException("Esta conexión ya se ha iniciado.");
            if (!Enum.IsDefined(wizardType)) throw new ArgumentOutOfRangeException(nameof(wizardType));
            return _connecting = ConnectCore(address, name, team, password, token, roomId, roomPassword, wizardType, deferReady, accountToken);
        }
    }
    private async Task ConnectCore(string address, string name, int team, string password, CancellationToken token,
        string roomId, string roomPassword, WizardType wizardType, bool deferReady, string accountToken)
    {
        Diagnostics.Log.ProtectSecret(password); Diagnostics.Log.ProtectSecret(roomPassword);
        Diagnostics.Log.ProtectSecret(accountToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(accountToken.Length > 0 ? 25 : 7));
        try
        {
            var endpoint = Address(address);
            Diagnostics.Info("network.connect", "Iniciando conexión.", new Dictionary<string, object?> { ["endpoint"] = endpoint, ["roomId"] = roomId, ["connection"] = _connectionId });
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            await _socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);
            var hello = new ClientMessage { Type = "hello", Name = name, Team = team, Password = password,
                RoomId = roomId, RoomPassword = roomPassword, Protocol = Protocol.Version, WizardType = wizardType, AccountToken = accountToken };
            await SendFrame(hello, timeout.Token).ConfigureAwait(false);
            var welcome = await Read(timeout.Token).ConfigureAwait(false);
            if (welcome?.Type == "error") throw new InvalidOperationException(welcome.Error);
            if (welcome is not { Type: "welcome", Snapshot: not null, Room: not null } || welcome.PlayerId <= 0 ||
                !LobbyValidation.IsValid(welcome.Server) || !welcome.Snapshot.Players.Any(p => p.Id == welcome.PlayerId))
                throw new InvalidDataException("El servidor no ha enviado un saludo válido.");
            LobbyValidation.Check(welcome.Room);
            if (welcome.Room.Id != roomId || welcome.Room.Mode != welcome.Snapshot.Mode || welcome.Room.Map != welcome.Snapshot.Map ||
                welcome.Room.ScoreLimit != welcome.Snapshot.ScoreLimit || welcome.Room.Tickets != welcome.Snapshot.StartingTickets ||
                welcome.Room.Players != welcome.Snapshot.Players.Length)
                throw new InvalidDataException("El saludo no corresponde a la sala solicitada.");
            PlayerId = welcome.PlayerId; Server = welcome.Server; Room = welcome.Room;
            _requiresReady = welcome.RequiresReady;
            ApplyReadiness(welcome);
            Volatile.Write(ref _latest, welcome.Snapshot); Volatile.Write(ref _connected, 1);
            Diagnostics.Info("network.connected", "Conexión establecida.", Context());
            _writer = WriteLoop(); _reader = ReadLoop();
            if (!deferReady && !CompleteLoading()) throw new IOException("No se pudo confirmar la preparación del cliente.");
        }
        catch (Exception e)
        {
            Diagnostics.Warn("network.connect_failed", "No se pudo establecer la conexión.", e, Context());
            Volatile.Write(ref _connected, 0); _socket.Abort(); throw;
        }
    }
    public bool CompleteLoading()
    {
        if (!Connected) return false;
        if (!_requiresReady || Interlocked.Exchange(ref _readyRequested, 1) != 0) return true;
        if (Send(new() { Type = "ready" })) return true;
        Volatile.Write(ref _readyRequested, 0); return false;
    }
    private void ApplyReadiness(ServerMessage message)
    {
        if (!_requiresReady) return;
        if (message.ReadyPlayerIds == null) throw new InvalidDataException("Falta el estado de preparación de la sala.");
        Volatile.Write(ref _preparingPlayers, message.PreparingPlayers);
        Volatile.Write(ref _ready, message.ReadyPlayerIds.Contains(PlayerId) ? 1 : 0);
    }
    public bool Send(ClientMessage message)
    {
        lock (_sendGate)
        {
            if (!Connected) return false;
            var queued = new ClientMessage { Type = message.Type, Sequence = ++_sequence,
                InspectionRequestId = message.InspectionRequestId, InspectPlayerId = message.InspectPlayerId,
                Element = message.Element, Slot = message.Slot, MoveX = message.MoveX, MoveY = message.MoveY,
                MoveTo = message.MoveTo, TargetX = message.TargetX, TargetY = message.TargetY,
                AimX = message.AimX, AimY = message.AimY };
            if (_outgoing.Writer.TryWrite(queued)) return true;
            // Perder release/input puede dejar un canal activo: no continúa una conexión saturada.
            Fail("Cola de conexión saturada. Vuelve a conectar.", null, "network.queue_full"); return false;
        }
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
    private async Task SendFrame(ClientMessage message, CancellationToken token)
    {
        await _socketSend.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposing) != 0) return;
            await _socket.SendAsync(Protocol.SerializeAction(message), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        }
        finally { _socketSend.Release(); }
    }
    private async Task WriteLoop()
    {
        try { await foreach (var message in _outgoing.Reader.ReadAllAsync(_lifetime.Token)) await SendFrame(message, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        { if (Volatile.Read(ref _disposing) == 0 && !_lifetime.IsCancellationRequested) Fail("Conexión interrumpida.", e, "network.send_failed"); }
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await Read(_lifetime.Token).ConfigureAwait(false);
                if (message == null) { Fail("El servidor ha cerrado la conexión.", null, "network.server_closed"); break; }
                if (message.Type == "snapshot" && message.Snapshot != null)
                { ApplyReadiness(message); Volatile.Write(ref _latest, message.Snapshot); }
                else if (message.Type == "account" && message.AccountNotice != null) AccountChanged?.Invoke(message.AccountNotice);
                else if (message.Type == "equipment")
                {
                    var pending = Volatile.Read(ref _inspection);
                    if (pending == null || pending.Id != message.InspectionRequestId) continue;
                    if (message.Error.Length > 0) pending.Completion.TrySetException(new InvalidOperationException(message.Error));
                    else
                    {
                        if (message.Inspection == null || message.Inspection.PlayerId != pending.PlayerId)
                            throw new InvalidDataException("La ficha no corresponde al mago solicitado.");
                        PublicEquipment.Check(message.Inspection);
                        pending.Completion.TrySetResult(message.Inspection);
                    }
                }
                else if (message.Type == "error") throw new IOException(message.Error);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or System.Text.Json.JsonException or ObjectDisposedException)
        { if (Volatile.Read(ref _disposing) == 0 && !_lifetime.IsCancellationRequested) Fail(e is IOException ? e.Message : "Se ha perdido la conexión con el servidor.", e, "network.receive_failed"); }
        finally { Volatile.Write(ref _connected, 0); _lifetime.Cancel(); _outgoing.Writer.TryComplete(); }
    }
    private void Fail(string reason, Exception? error, string code)
    {
        Volatile.Write(ref _connected, 0);
        if (Volatile.Read(ref _disposing) == 0 && Interlocked.Exchange(ref _notified, 1) == 0)
        {
            Diagnostics.Warn(code, reason, error, Context());
            try { Disconnected?.Invoke(reason); } catch (Exception e) { Diagnostics.Error("network.callback_failed", "Fallo en el manejador de desconexión.", e, Context()); }
        }
        _lifetime.Cancel(); _outgoing.Writer.TryComplete(); _socket.Abort();
    }
    private async Task<ServerMessage?> Read(CancellationToken token)
    {
        var stream = _receiveStream; var buffer = _receiveBuffer;
        stream.SetLength(0);
        WebSocketMessageType? frameType = null;
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary) ||
                frameType != null && frameType != result.MessageType) throw new IOException("Respuesta de red inválida.");
            frameType = result.MessageType;
            if (stream.Length + result.Count > 256 * 1024) throw new IOException("Respuesta de red demasiado grande.");
            stream.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            var message = Protocol.DeserializeFrame(stream.GetBuffer().AsSpan(0, (int)stream.Length), frameType == WebSocketMessageType.Binary);
            if (message?.Snapshot != null) SnapshotValidation.Check(message.Snapshot);
            if (message?.Snapshot != null) CheckReadiness(message);
            if (message?.Type == "snapshot" && message.Snapshot == null) throw new InvalidDataException("Estado de partida ausente.");
            return message;
        }
    }
    private static void CheckReadiness(ServerMessage message)
    {
        var players = message.Snapshot!.Players;
        if (message.PreparingPlayers is < 0 or > 16 || message.PreparingPlayers > players.Length ||
            message.ReadyPlayerIds is { Length: > 16 })
            throw new InvalidDataException("Estado de preparación de sala inválido.");
        if (message.ReadyPlayerIds is not { } ids) return;
        for (int i = 0; i < ids.Length; i++)
        {
            bool found = false;
            foreach (var player in players) if (player.Id == ids[i]) { found = true; break; }
            if (!found) throw new InvalidDataException("Jugador preparado desconocido.");
            for (int j = 0; j < i; j++) if (ids[j] == ids[i]) throw new InvalidDataException("Jugador preparado repetido.");
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCore());
    }
    private async Task DisposeCore()
    {
        Interlocked.Exchange(ref _disposing, 1); Volatile.Write(ref _connected, 0); _outgoing.Writer.TryComplete();
        if (_connecting is { IsCompleted: false })
        {
            _lifetime.Cancel(); _socket.Abort();
            try { await _connecting.ConfigureAwait(false); }
            catch (Exception) { } // ConnectCore ya registra y propaga el fallo a quien inició la conexión.
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _socketSend.WaitAsync(timeout.Token).ConfigureAwait(false);
            try { if (_socket.State == WebSocketState.Open) await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Salir", timeout.Token).ConfigureAwait(false); }
            finally { _socketSend.Release(); }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        _lifetime.Cancel(); _socket.Abort();
        if (_reader != null) await _reader.ConfigureAwait(false);
        if (_writer != null) await _writer.ConfigureAwait(false);
        Diagnostics.Info("network.disposed", "Conexión cerrada.", Context());
        _socket.Dispose(); _lifetime.Dispose(); _socketSend.Dispose(); _receiveStream.Dispose();
    }
}

public sealed record LanServer(string Address, ServerInfo Info);
public static class LanDiscovery
{
    public static async Task<LanServer[]> FindAsync(int port = Protocol.DiscoveryPort, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        string nonce = Guid.NewGuid().ToString("N")[..16];
        byte[] query = Encoding.UTF8.GetBytes(Protocol.DiscoveryMagic + " " + nonce);
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        // Algunas redes restringen la enumeración de interfaces; broadcast global y loopback siguen disponibles.
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var address in network.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork || address.IPv4Mask == null) continue;
                    var ip = address.Address.GetAddressBytes(); var mask = address.IPv4Mask.GetAddressBytes();
                    targets.Add(new IPAddress(ip.Zip(mask, (a, b) => (byte)(a | ~b)).ToArray()));
                }
            }
        }
        catch (NetworkInformationException) { }
        foreach (var target in targets)
            try { await udp.SendAsync(query, new IPEndPoint(target, port), token).ConfigureAwait(false); }
            catch (SocketException) { }
        var found = new Dictionary<string, LanServer>();
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (response.Buffer.Length > 4096) continue;
                DiscoveryReply? reply;
                try { reply = Protocol.Deserialize<DiscoveryReply>(Encoding.UTF8.GetString(response.Buffer)); }
                catch (System.Text.Json.JsonException) { continue; }
                if (reply == null || reply.Magic != Protocol.DiscoveryMagic || reply.Nonce != nonce || !LobbyValidation.IsValid(reply.Server)) continue;
                string address = response.RemoteEndPoint.Address + ":" + reply.Server.Port;
                if (found.Count >= 128 && !found.ContainsKey(address)) continue;
                found[address] = new(address, reply.Server);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        Diagnostics.Info("lan.search", "Búsqueda LAN finalizada.", new Dictionary<string, object?> { ["port"] = port, ["servers"] = found.Count });
        return found.Values.OrderBy(s => s.Info.Name).ToArray();
    }
}
