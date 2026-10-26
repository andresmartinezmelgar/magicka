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
            {
                try
                {
                    var report = MatchStatisticsReport.Parse(doc.GetProperty("fields").GetProperty("payload").GetProperty("stringValue").GetString() ?? "");
                    if (report.Status != MatchRecordStatus.Playing) reports.Add(report);
                }
                catch (Exception error) when (error is InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException)
                { /* Un informe no fiable no bloquea las demás partidas ni la página siguiente. */ }
            }
        }
        string next = document.RootElement.TryGetProperty("nextPageToken", out var nextToken) ? nextToken.GetString() ?? "" : "";
        if (next.Length > 8192) throw new InvalidDataException("Página inválida.");
        return new(reports.ToArray(), next);
    }
    public async Task<int> DeleteMatchStatistics(string uid, string idToken, CancellationToken token = default)
    {
        if (!await CanReadMatchStatistics(uid, idToken, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Esta cuenta no puede borrar estadísticas.");
        string prefix = $"projects/{_projectId}/databases/(default)/documents/matchStatistics/";
        string collection = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/matchStatistics";
        var ids = new HashSet<string>(StringComparer.Ordinal); string cursor = "";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Primero se fija la lista completa: los filtros del visor no limitan el borrado.
        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, collection + "?pageSize=100&mask.fieldPaths=runId" +
                (cursor.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(cursor)));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            await EnsureAllowed(response, "el listado de estadísticas", token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 1024 * 1024, token).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("documents", out var docs))
                foreach (var doc in docs.EnumerateArray())
                {
                    string name = doc.GetProperty("name").GetString() ?? "";
                    if (!name.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Documento fuera del historial.");
                    string id = name[prefix.Length..];
                    if (id.Length is < 1 or > 200 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Informe inválido.");
                    ids.Add(id);
                    if (ids.Count > 100000) throw new InvalidDataException("Historial demasiado grande; no se ha borrado ningún informe.");
                }
            cursor = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() ?? "" : "";
            if (cursor.Length > 8192 || cursor.Length > 0 && !seen.Add(cursor)) throw new InvalidDataException("Paginación inválida; no se ha iniciado el borrado.");
        } while (cursor.Length > 0);
        int deleted = 0;
        foreach (string id in ids)
        {
            token.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Delete, collection + "/" + Uri.EscapeDataString(id));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                await EnsureAllowed(response, "el borrado de estadísticas", token).ConfigureAwait(false);
            deleted++;
        }
        return deleted;
    }

}
