using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Arcana.Core;

public sealed record LanServer(string Address, ServerInfo Info);

public static class LanDiscovery
{
    public static async Task<LanServer[]> FindAsync(int port = Protocol.DiscoveryPort, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
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
        int queriesSent = 0;
        foreach (var target in targets)
        {
            try { await udp.SendAsync(query, new IPEndPoint(target, port), token).ConfigureAwait(false); queriesSent++; }
            catch (SocketException) { /* Una interfaz bloqueada no debe impedir consultar las demás. */ }
        }
        if (queriesSent == 0) throw new IOException("No se pudieron enviar consultas LAN; revisa la tarjeta de red y el cortafuegos.");
        // UDP puede perder la primera difusión (Wi-Fi, red recién reconectada, ahorro de energía).
        // Reintenta sobre las mismas interfaces sin bloquear la recepción ni duplicar servidores.
        async Task RetryQueries()
        {
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    await Task.Delay(750, timeout.Token).ConfigureAwait(false);
                    foreach (var target in targets)
                    {
                        try { await udp.SendAsync(query, new IPEndPoint(target, port), timeout.Token).ConfigureAwait(false); }
                        catch (SocketException) { /* Una interfaz caída no invalida las respuestas de las demás. */ }
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        }
        Task retries = RetryQueries();
        var found = new Dictionary<string, LanServer>();
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                UdpReceiveResult response;
                try { response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false); }
                catch (SocketException)
                {
                    // El cambio de Wi-Fi/interfaz puede invalidar temporalmente
                    // la recepción. Conservar las respuestas ya descubiertas.
                    await Task.Delay(150, timeout.Token).ConfigureAwait(false);
                    continue;
                }
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
        finally { timeout.Cancel(); await retries.ConfigureAwait(false); }
        Diagnostics.Info("lan.search", "Búsqueda LAN finalizada.", new Dictionary<string, object?> { ["port"] = port, ["servers"] = found.Count });
        return found.Values.OrderBy(s => s.Info.Name).ToArray();
    }
}
