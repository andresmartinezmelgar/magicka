using System.Net;
using System.Net.Sockets;
using System.Text;
using Arcana.Core;

namespace Arcana.Server;

public sealed class DiscoveryService(ServerSettings settings, GameService game, ILogger<DiscoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var udp = new UdpClient();
        try
        {
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, settings.DiscoveryPort));
            logger.LogInformation("Descubrimiento LAN en UDP {Port}", settings.DiscoveryPort);
            var last = new Dictionary<IPAddress, long>();
            while (!stoppingToken.IsCancellationRequested)
            {
                var request = await udp.ReceiveAsync(stoppingToken);
                if (request.Buffer.Length > 128) continue;
                var parts = Encoding.UTF8.GetString(request.Buffer).Split(' ');
                if (parts.Length != 2 || parts[0] != Protocol.DiscoveryMagic || parts[1].Length != 16) continue;
                long now = Environment.TickCount64;
                if (last.TryGetValue(request.RemoteEndPoint.Address, out long previous) && now - previous < 150) continue;
                if (last.Count > 1024) last.Clear();
                last[request.RemoteEndPoint.Address] = now;
                var reply = new DiscoveryReply(Protocol.DiscoveryMagic, parts[1], game.Info());
                await udp.SendAsync(Encoding.UTF8.GetBytes(Protocol.Serialize(reply)), request.RemoteEndPoint, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (SocketException e) { logger.LogWarning("No se puede iniciar el descubrimiento LAN: {Error}", e.Message); }
    }
}
