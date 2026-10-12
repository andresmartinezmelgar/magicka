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
    public string TokenFor(string address) => SignedIn && RoomBrowser.Endpoint(address, "account/profile") == RoomBrowser.Endpoint(ServerAddress, "account/profile") ? Token : "";
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
        return await Read<LoginChallenge>(response, token).ConfigureAwait(false);
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
        Profile = await Request<AccountProfile>(HttpMethod.Get, "account/profile", null, token).ConfigureAwait(false);
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
        try { if (SignedIn) _ = await Request<ApiError>(HttpMethod.Post, "account/logout", new { }, token).ConfigureAwait(false); }
        finally { Token = ""; ExpiresAt = default; Profile = null; }
    }
    private async Task<T> Request<T>(HttpMethod method, string path, object? body, CancellationToken token)
    {
        if (!SignedIn) throw new InvalidOperationException("Inicia sesión con Google para gestionar tu inventario.");
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
    public void Dispose() => _http.Dispose();
}
