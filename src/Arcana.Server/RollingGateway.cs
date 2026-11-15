using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Arcana.Core;

namespace Arcana.Server;

public sealed record GatewayNode(string Id, string Version, int Port);
public sealed record GatewayRoutes(string Phase, string Active, string TargetVersion, GatewayNode[] Nodes)
{
    public GatewayNode Current => Nodes.Single(n => n.Id == Active);
    public string Notice => "Iniciando servidor con la nueva versión" + (TargetVersion.Length > 0 ? " " + TargetVersion : "") + ". Las partidas en curso continúan.";
    public static GatewayRoutes Read(string file)
    {
        if (new FileInfo(file).Length > 16 * 1024) throw new InvalidDataException("Rutas demasiado grandes.");
        var routes = Protocol.Deserialize<GatewayRoutes>(File.ReadAllText(file));
        if (routes == null || routes.Phase is not ("ready" or "starting") || routes.TargetVersion is not { Length: <= 32 } ||
            routes.Nodes is not { Length: > 0 and <= 2 } || routes.Nodes.Select(n => n.Id).Distinct().Count() != routes.Nodes.Length ||
            routes.Nodes.Any(n => !Guid.TryParseExact(n.Id, "N", out _) || n.Id != n.Id.ToLowerInvariant() ||
                n.Port is < 1024 or > 65535 || !Version.TryParse(n.Version, out _)) || !routes.Nodes.Any(n => n.Id == routes.Active))
            throw new InvalidDataException("Rutas del servidor inválidas.");
        return routes;
    }
    public GatewayNode? Route(string room, bool resume)
    {
        string? generation = RoomGeneration.Of(room);
        if (generation == null) return Phase == "ready" && !resume ? Current : null;
        var node = Nodes.SingleOrDefault(n => n.Id == generation);
        return node != null && (node.Id == Active && Phase == "ready" || resume) ? node : null;
    }
}

/// <summary>Entrada estable: cada WebSocket queda unido a su proceso hasta que acaba la conexión.</summary>
internal sealed class RollingGateway(string stateFile)
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        AutomaticDecompression = DecompressionMethods.None, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _handshakes = new(32, 32);
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Connection", "Keep-Alive", "Transfer-Encoding", "TE", "Trailer", "Upgrade", "Proxy-Authorization", "Proxy-Authenticate", "Host", "X-Arcana-Management" };
    private static Uri Backend(GatewayNode node, string path, string scheme = "http") => new($"{scheme}://127.0.0.1:{node.Port}" + path);

    internal static WebApplication Build(string stateFile, int port, bool discovery = true, int discoveryPort = Protocol.DiscoveryPort, string bind = "0.0.0.0")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        var address = IPAddress.Parse(bind);
        builder.WebHost.UseUrls($"http://{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + address + "]" : address.ToString())}:{port}");
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = MatchStatisticsReport.MaxBytes);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        var gateway = new RollingGateway(stateFile);
        if (discovery) builder.Services.AddHostedService(_ => new GatewayDiscovery(stateFile, discoveryPort,
            Environment.GetEnvironmentVariable("ARCANA_GATEWAY_DISCOVERY_BIND") ?? "0.0.0.0"));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.Run(gateway.Forward);
        return app;
    }
    internal static async Task Run(string[] args)
    {
        string file = Environment.GetEnvironmentVariable("ARCANA_GATEWAY_CONFIG") ?? throw new ArgumentException("Falta ARCANA_GATEWAY_CONFIG.");
        int Port(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int p) && p is >= 1024 and <= 65535 ? p : fallback;
        await using var app = Build(file, Port("GAME_PORT", Protocol.GamePort), Environment.GetEnvironmentVariable("ARCANA_DISCOVERY_ENABLED") != "false", Port("DISCOVERY_PORT", Protocol.DiscoveryPort),
            Environment.GetEnvironmentVariable("ARCANA_GATEWAY_BIND") ?? "0.0.0.0");
        await app.RunAsync();
    }
    private static async Task Error(HttpContext context, string message, string code = "server_unavailable")
    {
        context.Response.StatusCode = 503;
        context.Response.Headers.RetryAfter = "2";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ApiError(message, code), Protocol.Json, context.RequestAborted);
    }
    private async Task Forward(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/internal", StringComparison.OrdinalIgnoreCase))
        { context.Response.StatusCode = 404; return; }
        try
        {
            var routes = GatewayRoutes.Read(stateFile);
            if (context.Request.Path == "/health")
            {
                await context.Response.WriteAsJsonAsync(new { status = "ok", role = "gateway", version = routes.Current.Version,
                    instanceId = routes.Current.Id, gatewayVersion = BuildInfo.Version, phase = routes.Phase }, Protocol.Json, context.RequestAborted); return;
            }
            if (context.WebSockets.IsWebSocketRequest && context.Request.Path == "/play")
            { await Connect(context); return; }
            if (routes.Phase == "starting" && (context.Request.Path == "/rooms" || context.Request.Path == "/info"))
            { await Error(context, routes.Notice, "server_starting"); return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), Backend(routes.Current,
                context.Request.Path.ToUriComponent() + context.Request.QueryString.ToUriComponent()));
            if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                request.Content = new StreamContent(context.Request.Body);
            var hop = new HashSet<string>(HopHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (string name in context.Request.Headers.Connection.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) hop.Add(name);
            foreach (var header in context.Request.Headers)
                if (!hop.Contains(header.Key) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
                if (!HopHeaders.Contains(header.Key)) context.Response.Headers[header.Key] = header.Value.ToArray();
            await response.Content.CopyToAsync(context.Response.Body, timeout.Token);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException or InvalidDataException or OperationCanceledException or WebSocketException)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            await Error(context, "El servidor está temporalmente indisponible. Vuelve a consultar las salas.");
        }
    }
    private async Task Connect(HttpContext context)
    {
        if (!await _handshakes.WaitAsync(0, context.RequestAborted))
        { await Error(context, "El servidor está atendiendo otras conexiones. Reintenta en un momento."); return; }
        bool held = true;
        WebSocket accepted;
        try { accepted = await context.WebSockets.AcceptWebSocketAsync(); }
        catch { _handshakes.Release(); throw; }
        using var socket = accepted;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = new byte[Protocol.MaxMessageBytes + 1]; int count = 0;
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(bytes.AsMemory(count), handshake.Token);
                count += received.Count;
                if (received.MessageType != WebSocketMessageType.Text || count > Protocol.MaxMessageBytes)
                    throw new InvalidDataException("Saludo inválido.");
            } while (!received.EndOfMessage);
            using var hello = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = Protocol.Json.MaxDepth });
            var root = hello.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "hello")
                throw new InvalidDataException("Falta el saludo de conexión.");
            string room = root.TryGetProperty("roomId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "default";
            bool resume = root.TryGetProperty("resumeToken", out var token) && token.ValueKind == JsonValueKind.String && token.GetString() is { Length: 64 };
            var routes = GatewayRoutes.Read(stateFile); var node = routes.Route(room, resume);
            if (node == null)
            {
                string message = routes.Phase == "starting" ? routes.Notice : "La partida anterior ya no está disponible. Actualiza la lista de salas.";
                await socket.SendAsync(Protocol.SerializeUtf8(new ServerMessage { Type = "error", Error = message }), WebSocketMessageType.Text, true, handshake.Token);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Servidor actualizándose", handshake.Token); return;
            }
            using var upstream = new ClientWebSocket();
            upstream.Options.Proxy = null;
            upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            await upstream.ConnectAsync(Backend(node, "/play", "ws"), handshake.Token);
            await upstream.SendAsync(bytes.AsMemory(0, count), WebSocketMessageType.Text, true, handshake.Token);
            _handshakes.Release(); held = false;
            // No se inspeccionan ni vuelven a serializar snapshots ni acciones de combate.
            var inbound = Pump(socket, upstream, lifetime.Token); var outbound = Pump(upstream, socket, lifetime.Token);
            try { await await Task.WhenAny(inbound, outbound); }
            finally
            {
                lifetime.Cancel();
                try { await Task.WhenAll(inbound, outbound); } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { }
            }
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or OperationCanceledException or WebSocketException)
        {
            if (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "Servidor temporalmente indisponible", timeout.Token); }
                catch (Exception close) when (close is WebSocketException or OperationCanceledException) { }
            }
        }
        finally { if (held) _handshakes.Release(); }
    }
    private static async Task Pump(WebSocket source, WebSocket target, CancellationToken token)
    {
        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            var result = await source.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (target.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await target.CloseOutputAsync(source.CloseStatus ?? WebSocketCloseStatus.NormalClosure, "Fin de sesión", token);
                return;
            }
            await target.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, token);
        }
    }
}
