using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

/// <summary>Red del cliente. Se puede probar sin arrancar el motor gráfico.</summary>
public sealed class ArenaConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Channel<ClientMessage> _outgoing = Channel.CreateBounded<ClientMessage>(new BoundedChannelOptions(256)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly object _sendGate = new();
    private long _sequence;
    private WorldSnapshot? _latest;
    private Task? _reader, _writer;
    private bool _disposing;
    public int PlayerId { get; private set; }
    public ServerInfo? Server { get; private set; }
    public WorldSnapshot? Latest => Volatile.Read(ref _latest);
    public bool Connected => !_lifetime.IsCancellationRequested && _socket.State == WebSocketState.Open;
    public event Action<string>? Disconnected;

    public static Uri Address(string address)
    {
        address = address.Trim();
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Introduce una IP o un nombre de servidor.");
        if (!address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(address, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
                address = $"[{address}]:{Protocol.GamePort}";
            else if (!address.Contains(':')) address += $":{Protocol.GamePort}";
            address = "ws://" + address;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("ws" or "wss") ||
            uri.Host.Length == 0 || uri.UserInfo.Length > 0) throw new ArgumentException("Dirección inválida. Ejemplo: 192.168.1.20:27015");
        if (uri.AbsolutePath == "/") return new UriBuilder(uri) { Path = "/play" }.Uri;
        return uri;
    }

    public async Task ConnectAsync(string address, string name, int team, string password, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(7));
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        await _socket.ConnectAsync(Address(address), timeout.Token).ConfigureAwait(false);
        var hello = new ClientMessage { Type = "hello", Name = name, Team = team, Password = password, Protocol = Protocol.Version };
        await _socket.SendAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(hello)), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
        var welcome = await Read(timeout.Token).ConfigureAwait(false);
        if (welcome?.Type == "error") throw new InvalidOperationException(welcome.Error);
        if (welcome is not { Type: "welcome", Snapshot: not null } || welcome.PlayerId <= 0)
            throw new InvalidOperationException("El servidor no ha enviado un saludo válido.");
        PlayerId = welcome.PlayerId; Server = welcome.Server;
        Volatile.Write(ref _latest, welcome.Snapshot);
        _writer = WriteLoop(); _reader = ReadLoop();
    }

    public bool Send(ClientMessage message)
    {
        lock (_sendGate)
        {
            if (!Connected) return false;
            message.Sequence = ++_sequence;
            return _outgoing.Writer.TryWrite(message);
        }
    }
    private async Task WriteLoop()
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync(_lifetime.Token))
                await _socket.SendAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(message)), WebSocketMessageType.Text, true, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException)
        { if (!_disposing && !_lifetime.IsCancellationRequested) Disconnected?.Invoke("Conexión interrumpida."); _lifetime.Cancel(); }
    }
    private async Task ReadLoop()
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                var message = await Read(_lifetime.Token).ConfigureAwait(false);
                if (message == null) break;
                if (message.Type == "snapshot" && message.Snapshot != null) Volatile.Write(ref _latest, message.Snapshot);
                else if (message.Type == "error") throw new IOException(message.Error);
            }
            if (!_disposing) Disconnected?.Invoke("El servidor ha cerrado la conexión.");
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or System.Text.Json.JsonException)
        { if (!_disposing && !_lifetime.IsCancellationRequested) Disconnected?.Invoke(e is IOException ? e.Message : "Se ha perdido la conexión con el servidor."); }
        finally { _lifetime.Cancel(); _outgoing.Writer.TryComplete(); }
    }
    private async Task<ServerMessage?> Read(CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Respuesta de red inválida.");
            stream.Write(buffer, 0, result.Count);
            if (stream.Length > 256 * 1024) throw new IOException("Respuesta de red demasiado grande.");
            if (result.EndOfMessage) return Protocol.Deserialize<ServerMessage>(Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposing) return;
        _disposing = true;
        if (_socket.State == WebSocketState.Open)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Salir", timeout.Token).ConfigureAwait(false); }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
        }
        _lifetime.Cancel(); _outgoing.Writer.TryComplete();
        if (_reader != null) try { await _reader.ConfigureAwait(false); } catch (OperationCanceledException) { }
        if (_writer != null) try { await _writer.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _socket.Dispose(); _lifetime.Dispose();
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
                if (reply == null || reply.Server == null || reply.Magic != Protocol.DiscoveryMagic || reply.Nonce != nonce ||
                    reply.Server.Protocol != Protocol.Version || reply.Server.Port is < 1 or > 65535 ||
                    reply.Server.Name == null || reply.Server.Name.Length > 64 || !Enum.IsDefined(reply.Server.Mode)) continue;
                string address = response.RemoteEndPoint.Address + ":" + reply.Server.Port;
                found[address] = new(address, reply.Server);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        return found.Values.OrderBy(s => s.Info.Name).ToArray();
    }
}
