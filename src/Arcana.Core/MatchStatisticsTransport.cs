using System.Text;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Informes anónimos enviados directamente a Firestore, sin sesión ni servidor intermedio.</summary>
public sealed class MatchStatisticsTransport : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _collection;
    private readonly bool _owns;
    public MatchStatisticsTransport(string projectId, HttpClient? http = null)
    {
        if (string.IsNullOrEmpty(projectId) || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto Firebase inválido.", nameof(projectId));
        _owns = http == null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
        _collection = $"https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/matchStatistics";
    }
    public async Task Upload(MatchStatisticsReport report, CancellationToken token = default)
    {
        string payload = report.ToJson();
        if (report.Status == MatchRecordStatus.Playing) throw new InvalidOperationException("El informe sigue en curso.");
        // Inmutables: dos clientes o un reintento usan la misma clave. Una revisión
        // antigua nunca reemplaza a la final; el lector agrupa por runId.
        string id = report.RunId + "-" + report.Revision.ToString(CultureInfo.InvariantCulture) + "-" + ((int)report.Status).ToString(CultureInfo.InvariantCulture);
        var fields = new
        {
            schemaVersion = new { integerValue = report.SchemaVersion.ToString(CultureInfo.InvariantCulture) },
            runId = new { stringValue = report.RunId },
            revision = new { integerValue = report.Revision.ToString(CultureInfo.InvariantCulture) },
            status = new { integerValue = ((int)report.Status).ToString(CultureInfo.InvariantCulture) },
            day = new { stringValue = report.Day }, mode = new { stringValue = report.Mode.ToString() },
            map = new { stringValue = report.Map.ToString() }, gameVersion = new { stringValue = report.GameVersion },
            complete = new { booleanValue = report.Complete }, payload = new { stringValue = payload }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, _collection + "?documentId=" + id + "&mask.fieldPaths=runId")
        { Content = new StringContent(JsonSerializer.Serialize(new { fields }), Encoding.UTF8, "application/json") };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created) return;
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            try
            {
                using var body = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 8192, token).ConfigureAwait(false));
                if (body.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String && status.GetString() == "ALREADY_EXISTS") return;
            }
            catch (JsonException) { }
        }
        throw new IOException("El informe sigue pendiente de sincronización con Firestore (HTTP " + (int)response.StatusCode + ").");
    }
    public void Dispose() { if (_owns) _http.Dispose(); }
}
