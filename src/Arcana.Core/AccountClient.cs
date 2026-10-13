using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Arcana.Core;

public sealed record AccountConfiguration(bool Enabled, string Message);
public sealed record LoginChallenge(string Id, string PollSecret, string Url, DateTimeOffset ExpiresAt);
public sealed record LoginPollRequest(string Id, string PollSecret);
public sealed record LoginCompleteRequest(string IdToken, string BrowserSecret);
public sealed record LoginPoll(bool Ready, string AccessToken = "", DateTimeOffset ExpiresAt = default, AccountProfile? Profile = null);

/// <summary>El token permanece en memoria. La caché de perfil sirve para jugar contra IA sin conexión.</summary>
public sealed class AccountClient : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public string ServerAddress { get; private set; } = "";
    public string Token { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public AccountProfile? Profile { get; private set; }
    public bool SignedIn => Token.Length > 0 && ExpiresAt > DateTimeOffset.UtcNow;
    public string TokenFor(string address) => SignedIn ? Token : "";
    private FirebaseProfileReader? _firebaseReader;
    private string _firebaseUid = "";
    public bool IsDirectFirebaseSession => _firebaseReader != null && SignedIn;
    public bool HasDirectFirebaseSession => _firebaseReader != null;
    public async Task SetFirebaseSession(string projectId, string uid, string displayName, string idToken, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        if (expiresAt <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(idToken))
            throw new UnauthorizedAccessException("Token Firebase caducado o vacío.");
        using var reader = new FirebaseProfileReader(projectId);
        var profile = await reader.Read(uid, idToken, displayName, cancellationToken).ConfigureAwait(false);
        _firebaseReader?.Dispose();
        _firebaseReader = new FirebaseProfileReader(projectId);
        _firebaseUid = uid;
        ServerAddress = "";
        Token = idToken;
        ExpiresAt = expiresAt;
        Profile = profile;
        Diagnostics.Log.ProtectSecret(Token);
    }

    public void RestoreOfflineProfile(AccountProfile profile)
    {
        _ = EquipmentModifiers.Calculate(profile);
        Profile = profile;
    }
    public void EquipOffline(EquipmentSlot slot, string itemId)
    {
        if (Profile == null) throw new InvalidOperationException("No hay un inventario guardado para practicar.");
        Profile = AccountInventory.Equip(Profile, new(itemId, slot));
    }
    public async Task<AccountConfiguration> Configuration(string address, CancellationToken token)
    {
        var endpoint = RoomBrowser.Endpoint(address, "account/config");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren una dirección wss:// con HTTPS.");
        using var response = await _http.GetAsync(endpoint, token).ConfigureAwait(false);
        return await Read<AccountConfiguration>(response, token).ConfigureAwait(false);
    }
    public async Task<LoginChallenge> BeginLogin(string address, CancellationToken token)
    {
        var endpoint = RoomBrowser.Endpoint(address, "account/login/start");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren HTTPS.");
        Token = ""; ExpiresAt = default;
        ServerAddress = address;
        using var response = await _http.PostAsJsonAsync(endpoint, new { }, Protocol.Json, token).ConfigureAwait(false);
        var challenge = await Read<LoginChallenge>(response, token).ConfigureAwait(false);
        var login = RoomBrowser.Endpoint(address, "account/login/" + challenge.Id);
        if (!Guid.TryParseExact(challenge.Id, "N", out _) || challenge.PollSecret == null || challenge.PollSecret.Length is < 32 or > 128 ||
            challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6) ||
            !Uri.TryCreate(challenge.Url, UriKind.Absolute, out var url) || url.GetLeftPart(UriPartial.Path) != login.GetLeftPart(UriPartial.Path) ||
            url.UserInfo.Length > 0 || url.Fragment.Length > 0 || !url.Query.StartsWith("?key=", StringComparison.Ordinal) ||
            url.Query.Length is < 37 or > 133 || url.Query[5..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("El servidor ha enviado un acceso Google inválido. Revisa la dirección del servidor.");
        Diagnostics.Log.ProtectSecret(challenge.PollSecret); Diagnostics.Log.ProtectSecret(url.Query[5..]);
        return challenge;
    }
    public async Task<bool> PollLogin(LoginChallenge challenge, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(RoomBrowser.Endpoint(ServerAddress, "account/login/poll"),
            new LoginPollRequest(challenge.Id, challenge.PollSecret), Protocol.Json, token).ConfigureAwait(false);
        var result = await Read<LoginPoll>(response, token).ConfigureAwait(false);
        if (!result.Ready) return false;
        if (result.Profile == null || result.AccessToken.Length < 32 || result.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidDataException("Sesión de cuenta inválida.");
        _ = EquipmentModifiers.Calculate(result.Profile);
        Token = result.AccessToken; ExpiresAt = result.ExpiresAt; Profile = result.Profile; Diagnostics.Log.ProtectSecret(Token);
        return true;
    }
    public async Task<AccountProfile> Refresh(CancellationToken token = default)
    {
        Profile = IsDirectFirebaseSession ? await _firebaseReader!.Read(_firebaseUid, Token, token: token).ConfigureAwait(false) : await Request<AccountProfile>(HttpMethod.Get, "account/profile", null, token).ConfigureAwait(false);
        _ = EquipmentModifiers.Calculate(Profile); return Profile;
    }
    public async Task<InventoryResult> Equip(EquipmentSlot slot, string itemId, CancellationToken token = default)
    {
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/equip", new InventoryRequest(itemId, slot), token).ConfigureAwait(false);
        _ = EquipmentModifiers.Calculate(result.Profile); Profile = result.Profile; return result;
    }
    public async Task<InventoryResult> Sell(string itemId, CancellationToken token = default)
    {
        var result = await Request<InventoryResult>(HttpMethod.Post, "account/sell", new InventoryRequest(itemId), token).ConfigureAwait(false);
        _ = EquipmentModifiers.Calculate(result.Profile); Profile = result.Profile; return result;
    }
    public async Task SignOut(CancellationToken token = default)
    {
        try { if (SignedIn && !IsDirectFirebaseSession) _ = await Request<ApiError>(HttpMethod.Post, "account/logout", new { }, token).ConfigureAwait(false); }
        finally { Token = ""; ExpiresAt = default; Profile = null; _firebaseReader?.Dispose(); _firebaseReader = null; _firebaseUid = ""; }
    }
    private async Task<T> Request<T>(HttpMethod method, string path, object? body, CancellationToken token)
    {
        if (!SignedIn) throw new InvalidOperationException("Inicia sesión con Google para gestionar tu inventario.");
        if (ServerAddress.Length == 0)
            throw new InvalidOperationException("Esta acción requiere un servidor de cuentas disponible para verificarla; tu sesión Google sigue conectada.");
        using var request = new HttpRequestMessage(method, RoomBrowser.Endpoint(ServerAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body != null) request.Content = JsonContent.Create(body, options: Protocol.Json);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) { Token = ""; ExpiresAt = default; }
        return await Read<T>(response, token).ConfigureAwait(false);
    }
    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken token)
    {
        string text = await RoomBrowser.ReadBoundedAsync(response.Content, 1024 * 1024, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(Protocol.Deserialize<ApiError>(text)?.Error ?? "No se pudo gestionar la cuenta.");
        return Protocol.Deserialize<T>(text) ?? throw new InvalidDataException("Respuesta de cuenta incompleta.");
    }
    /// <summary>Dirección opcional HTTPS únicamente para acciones de escritura verificadas.</summary>
    public void SetProtectedActionsServer(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) { ServerAddress = ""; return; }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Las operaciones protegidas necesitan un servidor HTTPS.");
        ServerAddress = address.TrimEnd('/');
    }
    public void Dispose() { _firebaseReader?.Dispose(); _http.Dispose(); }
}
