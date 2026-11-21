using System.Net;
using System.Text;

namespace Arcana.Core;

public sealed class StatisticsRejectedException : IOException { }

/// <summary>Ingesta HTTPS acotada. Ningún cliente escribe estadísticas directamente en Firestore.</summary>
public sealed class MatchStatisticsTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<CancellationToken, Task<string>> _resolve;
    private readonly bool _owns;
    public MatchStatisticsTransport(string projectId, HttpClient? http = null, Func<CancellationToken, Task<string>>? resolve = null)
    {
        if (string.IsNullOrEmpty(projectId) || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto Firebase inválido.", nameof(projectId));
        _owns = http == null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _resolve = resolve ?? (token => PublicAccountServer.Resolve(projectId, token));
    }
    public async Task Upload(MatchStatisticsReport report, CancellationToken token = default)
    {
        if (report.Status == MatchRecordStatus.Playing || report.Network)
            throw new InvalidOperationException("Los informes online solo los guarda el servidor de la partida.");
        string address = PublicAccountServer.ValidateAddress(await _resolve(token).ConfigureAwait(false));
        var endpoint = new UriBuilder(address) { Scheme = "https", Port = 443, Path = "/analytics/matches" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        { Content = new StringContent(report.ToJson(), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.Created) return;
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Gone or HttpStatusCode.RequestEntityTooLarge)
            throw new StatisticsRejectedException();
        throw new IOException("El informe sigue pendiente de sincronización (HTTP " + (int)response.StatusCode + ").");
    }
    public void Dispose() { if (_owns) _http.Dispose(); }
}
