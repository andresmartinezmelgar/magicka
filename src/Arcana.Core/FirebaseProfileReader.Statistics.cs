using System.Net.Http.Headers;
using System.Text.Json;

namespace Arcana.Core;

public sealed record MatchStatisticsPage(MatchStatisticsReport[] Reports, string NextPageToken);
public sealed partial class FirebaseProfileReader
{
    public async Task<bool> CanReadMatchStatistics(string uid, string idToken, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 128 || uid.Any(c => c == '/' || char.IsControl(c))) throw new ArgumentException("Cuenta inválida.");
        var doc = await Document("statisticsReaders/" + Uri.EscapeDataString(uid), idToken, token).ConfigureAwait(false);
        return doc is {} value && value.TryGetProperty("fields", out var fields) && fields.TryGetProperty("enabled", out var enabled) &&
            enabled.TryGetProperty("booleanValue", out var flag) && flag.ValueKind == JsonValueKind.True;
    }
    public async Task<MatchStatisticsPage> ReadMatchStatistics(string idToken, string pageToken = "", CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(idToken)) throw new UnauthorizedAccessException("Inicia sesión con Google.");
        if (pageToken.Length > 8192) throw new InvalidDataException("Página inválida.");
        string url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/matchStatistics?pageSize=10&orderBy=day%20desc&mask.fieldPaths=payload";
        if (pageToken.Length > 0) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await EnsureAllowed(response, "las estadísticas globales", token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 4 * 1024 * 1024, token).ConfigureAwait(false));
        var reports = new List<MatchStatisticsReport>();
        if (document.RootElement.TryGetProperty("documents", out var docs))
        {
            if (docs.ValueKind != JsonValueKind.Array || docs.GetArrayLength() > 10) throw new InvalidDataException("Página demasiado grande.");
            foreach (var doc in docs.EnumerateArray())
                reports.Add(MatchStatisticsReport.Parse(doc.GetProperty("fields").GetProperty("payload").GetProperty("stringValue").GetString() ?? ""));
        }
        string next = document.RootElement.TryGetProperty("nextPageToken", out var nextToken) ? nextToken.GetString() ?? "" : "";
        if (next.Length > 8192) throw new InvalidDataException("Página inválida.");
        return new(reports.ToArray(), next);
    }
}
