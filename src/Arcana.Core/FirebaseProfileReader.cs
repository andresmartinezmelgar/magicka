using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Lectura Firestore con ID token Firebase, independiente del servidor de partidas/cuentas.</summary>
public sealed class FirebaseProfileReader : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _projectId;
    public FirebaseProfileReader(string projectId)
    {
        if (string.IsNullOrEmpty(projectId) || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto Firebase inválido.", nameof(projectId));
        _projectId = projectId;
    }
    private async Task<JsonElement?> Document(string path, string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) throw new UnauthorizedAccessException("Falta la sesión de Firebase.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("Firestore ha rechazado la lectura. Comprueba las reglas y la sesión.");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }
    /// <summary>Alta sin servidor: solo los campos iniciales permitidos por las reglas de Firestore.</summary>
    private async Task CreateInitialProfile(string uid, string displayName, string idToken, CancellationToken token)
    {
        string name = new string((displayName.Length == 0 ? "Mago" : displayName)
            .Where(ch => !char.IsControl(ch)).Take(64).ToArray());
        if (name.Length == 0) name = "Mago";
        var fields = new
        {
            fields = new
            {
                uid = new { stringValue = uid },
                name = new { stringValue = name },
                gold = new { integerValue = "0" },
                wins = new { integerValue = "0" },
                losses = new { integerValue = "0" },
                itemCount = new { integerValue = "0" },
                schemaVersion = new { integerValue = "1" }
            }
        };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/users?documentId={Uri.EscapeDataString(uid)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        request.Content = JsonContent.Create(fields);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Conflict) return; // Otro dispositivo creó la cuenta.
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Firestore no permite crear el perfil inicial. Publica las reglas del proyecto.");
        response.EnsureSuccessStatusCode();
    }

    private static object? Value(JsonElement value)
    {
        if (value.TryGetProperty("stringValue", out var s)) return s.GetString();
        if (value.TryGetProperty("integerValue", out var i)) return long.Parse(i.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (value.TryGetProperty("doubleValue", out var d)) return d.GetDouble();
        if (value.TryGetProperty("booleanValue", out var b)) return b.GetBoolean();
        if (value.TryGetProperty("nullValue", out _)) return null;
        if (value.TryGetProperty("timestampValue", out var t)) return t.GetString();
        if (value.TryGetProperty("mapValue", out var m))
            return m.TryGetProperty("fields", out var fields) ? Fields(fields) : new Dictionary<string, object?>();
        if (value.TryGetProperty("arrayValue", out var a))
            return a.TryGetProperty("values", out var items) ? items.EnumerateArray().Select(Value).ToArray() : Array.Empty<object?>();
        throw new InvalidDataException("Tipo Firestore no admitido.");
    }
    private static Dictionary<string, object?> Fields(JsonElement fields) =>
        fields.EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value), StringComparer.Ordinal);
    private static T Parse<T>(JsonElement doc)
    {
        if (!doc.TryGetProperty("fields", out var fields)) throw new InvalidDataException("Documento Firestore incompleto.");
        return Protocol.Deserialize<T>(JsonSerializer.Serialize(Fields(fields), Protocol.Json))
            ?? throw new InvalidDataException("Datos Firestore inválidos.");
    }
    private sealed record Header(string Uid, string Name, long Gold, int Wins, int Losses, int ItemCount, int SchemaVersion);
    public async Task<AccountProfile> Read(string uid, string idToken, string displayName = "Mago", CancellationToken token = default)
    {
        if (uid.Length is < 1 or > 128 || uid.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("UID inválido.", nameof(uid));
        var user = await Document("users/" + uid, idToken, token).ConfigureAwait(false);
        if (user is null)
        {
            await CreateInitialProfile(uid, displayName, idToken, token).ConfigureAwait(false);
            user = await Document("users/" + uid, idToken, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("El perfil inicial no aparece en Firestore.");
        }
        var header = Parse<Header>(user.Value);
        if (header.Uid != uid || header.SchemaVersion != 1 || header.Gold < 0 ||
            header.ItemCount is < 0 or > EquipmentCatalogue.InventoryLimit)
            throw new InvalidDataException("Cabecera de cuenta inválida.");
        var loadout = await Document("users/" + uid + "/loadouts/active", idToken, token).ConfigureAwait(false);
        var items = new List<EquipmentItem>();
        string? pageToken = null;
        do
        {
            var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/users/{uid}/items?pageSize=100";
            if (pageToken != null) url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException("No se permite consultar el inventario.");
            response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (result.RootElement.TryGetProperty("documents", out var docs))
                foreach (var doc in docs.EnumerateArray())
                {
                    items.Add(Parse<EquipmentItem>(doc));
                    if (items.Count > EquipmentCatalogue.InventoryLimit) throw new InvalidDataException("Inventario demasiado grande.");
                }
            pageToken = result.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(pageToken));
        if (items.Any(i => !EquipmentCatalogue.Valid(i))) throw new InvalidDataException("Inventario inválido.");
        var profile = new AccountProfile(uid, header.Name, header.Gold, header.Wins, header.Losses,
            loadout.HasValue ? Parse<EquipmentLoadout>(loadout.Value) : new(), items.ToArray());
        _ = EquipmentModifiers.Calculate(profile);
        return profile;
    }
    public void Dispose() => _http.Dispose();
}
