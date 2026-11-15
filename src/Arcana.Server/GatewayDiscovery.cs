using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Arcana.Core;

namespace Arcana.Server;

internal sealed class GatewayDiscovery(string stateFile, int port, string bind) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient(); using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(2) };
        try
        {
            udp.Client.Bind(new IPEndPoint(IPAddress.Parse(bind), port));
            var last = new Dictionary<IPAddress, long>(); JsonNode? info = null; long refreshed = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(stoppingToken);
                if (packet.Buffer.Length > 128) continue;
                var parts = Encoding.UTF8.GetString(packet.Buffer).Split(' ');
                if (parts.Length != 2 || parts[0] != Protocol.DiscoveryMagic || parts[1].Length != 16) continue;
                long now = Environment.TickCount64;
                if (last.TryGetValue(packet.RemoteEndPoint.Address, out long previous) && now - previous < 150) continue;
                if (last.Count > 1024) last.Clear(); last[packet.RemoteEndPoint.Address] = now;
                try
                {
                    if (info == null || now - refreshed >= 500)
                    {
                        var routes = GatewayRoutes.Read(stateFile);
                        using var response = await http.GetAsync($"http://127.0.0.1:{routes.Current.Port}/info", stoppingToken);
                        response.EnsureSuccessStatusCode();
                        info = JsonNode.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 16384, stoppingToken)); refreshed = now;
                    }
                    var reply = new JsonObject { ["magic"] = parts[0], ["nonce"] = parts[1], ["server"] = info!.DeepClone() };
                    await udp.SendAsync(Encoding.UTF8.GetBytes(reply.ToJsonString()), packet.RemoteEndPoint, stoppingToken);
                }
                catch (Exception error) when (error is IOException or HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
                { if (stoppingToken.IsCancellationRequested) break; info = null; }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (SocketException error) { Diagnostics.Warn("gateway.discovery", "Descubrimiento LAN temporalmente no disponible.", error); }
    }
}
