using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>Lectura Firestore con ID token Firebase, independiente del servidor de partidas/cuentas.</summary>
public sealed partial class FirebaseProfileReader : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _projectId;
    public FirebaseProfileReader(string projectId, HttpClient? httpClient = null)
    {
        if (string.IsNullOrEmpty(projectId) || projectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Proyecto Firebase inválido.", nameof(projectId));
        _projectId = projectId;
        _ownsHttp = httpClient == null;
        _http = httpClient ?? new() { Timeout = TimeSpan.FromSeconds(15) };
    }
    private static async Task EnsureAllowed(HttpResponseMessage response, string action, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        // Leer solo el código normalizado: jamás registrar ID tokens ni respuestas crudas del proveedor.
        string status = "";
        try
        {
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (body.Length <= 8192)
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("status", out var value) &&
                    value.ValueKind == JsonValueKind.String)
                    status = value.GetString() ?? "";
            }
        }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.Unauthorized || status == "UNAUTHENTICATED")
            throw new UnauthorizedAccessException("Firestore (401 UNAUTHENTICATED) rechaza la sesión de Google. Cierra sesión y vuelve a entrar.");
        if (response.StatusCode == HttpStatusCode.Forbidden || status == "PERMISSION_DENIED")
            throw new UnauthorizedAccessException(
                $"Firestore (403 PERMISSION_DENIED) ha denegado {action}. " +
                "El acceso de Google funciona, pero el proyecto no permite esa lectura/escritura. " +
                "Comprueba que las reglas de firebase/firestore.rules estén PUBLICADAS en Firebase Console → Firestore Database → Reglas, " +
                "y que el proveedor Google pertenezca al proyecto arcana-arena-f2997.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Firestore (404 NOT_FOUND) no encuentra {action}. Comprueba que la base de datos (default) exista en arcana-arena-f2997.");
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement?> Document(string path, string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) throw new UnauthorizedAccessException("Falta la sesión de Firebase.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAllowed(response, "la lectura de " + path.Split('/')[0] + "/{tu cuenta}" +
            (path.Contains("/loadouts/", StringComparison.Ordinal) ? "/loadouts/active" : ""), cancellationToken).ConfigureAwait(false);
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
        await EnsureAllowed(response, "la creación del perfil inicial", token).ConfigureAwait(false);
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
    private sealed record Header(string Uid, string Name, long Gold, int Wins, int Losses, int ItemCount, int SchemaVersion)
    {
        public Dictionary<string, long> Bestiary { get; init; } = new(StringComparer.Ordinal);
    }
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
            await EnsureAllowed(response, "la lista de objetos de tu inventario", token).ConfigureAwait(false);
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
            loadout.HasValue ? Parse<EquipmentLoadout>(loadout.Value) : new(), items.ToArray()) { Bestiary = header.Bestiary };
        _ = EquipmentModifiers.Calculate(profile);
        return profile;
    }
    /// <summary>Solo el equipamiento se escribe desde el cliente: reglas verifican propiedad y ranuras.</summary>
    public async Task SaveLoadout(string uid, string idToken, EquipmentLoadout equipment, CancellationToken token = default)
    {
        if (uid.Length is < 1 or > 128 || uid.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new ArgumentException("UID inválido.");
        var values = new Dictionary<string, object>
        {
            ["boots"] = new { stringValue = equipment.Boots },
            ["hat"] = new { stringValue = equipment.Hat },
            ["chest"] = new { stringValue = equipment.Chest },
            ["weapon"] = new { stringValue = equipment.Weapon }
        };
        // Las selecciones antiguas siguen usando cuatro claves. PATCH sin máscara
        // sustituye el documento, por lo que omitir un anillo también lo desequipa.
        if (equipment.GoldRing.Length > 0) values["goldRing"] = new { stringValue = equipment.GoldRing };
        if (equipment.MagicRing.Length > 0) values["magicRing"] = new { stringValue = equipment.MagicRing };
        var fields = new { fields = values };
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/users/{uid}/loadouts/active");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        request.Content = JsonContent.Create(fields);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        await EnsureAllowed(response, "la escritura de tu equipamiento", token).ConfigureAwait(false);
    }
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
