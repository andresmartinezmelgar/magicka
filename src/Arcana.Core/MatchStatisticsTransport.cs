using System.Text;

namespace Arcana.Core;

/// <summary>Envío sin sesión al relé oficial; la escritura Firestore queda en el servicio Admin.</summary>
public sealed class MatchStatisticsTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _resolve;
    private readonly bool _owns;
    public MatchStatisticsTransport(string projectId, HttpClient? http = null, Func<CancellationToken, Task<string>>? resolve = null)
    {
        _owns = http == null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _resolve = resolve ?? (token => PublicAccountServer.Resolve(projectId, token));
    }
    public async Task Upload(MatchStatisticsReport report, CancellationToken token = default)
    {
        string address = await _resolve(token).ConfigureAwait(false);
        var endpoint = RoomBrowser.Endpoint(address, "analytics/matches");
        if (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)) throw new InvalidDataException("El relé necesita HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(report.ToJson(), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Accepted) throw new IOException("El informe sigue pendiente de sincronización.");
    }
    public void Dispose() { if (_owns) _http.Dispose(); }
}
