using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Arcana.Core;

public sealed partial class AccountClient
{
    public async Task<AccountConfiguration> Configuration(string address, CancellationToken token)
    {
        var endpoint = Endpoint(address, "account/config");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren una dirección wss:// con HTTPS.");
        using var response = await _http.GetAsync(endpoint, token).ConfigureAwait(false);
        return await Read<AccountConfiguration>(response, token).ConfigureAwait(false);
    }

    public async Task<LoginChallenge> BeginLogin(string address, CancellationToken token)
    {
        var endpoint = Endpoint(address, "account/login/start");
        if (endpoint.Scheme != "https" && !endpoint.IsLoopback) throw new ArgumentException("Las cuentas requieren HTTPS.");
        long generation;
        lock (_identityGate)
        {
            generation = ++_identityGeneration;
            Token = ""; ExpiresAt = default; Profile = null; ResetInventorySync();
            _firebaseReader?.Dispose(); _firebaseReader = null; _firebaseUid = ""; _pendingLoginId = "";
            ServerAddress = address;
        }
        using var response = await _http.PostAsJsonAsync(endpoint, new { }, Protocol.Json, token).ConfigureAwait(false);
        var challenge = await Read<LoginChallenge>(response, token).ConfigureAwait(false);
        var login = Endpoint(address, "account/login/" + challenge.Id);
        if (!Guid.TryParseExact(challenge.Id, "N", out _) || challenge.PollSecret == null || challenge.PollSecret.Length is < 32 or > 128 ||
            challenge.ExpiresAt <= DateTimeOffset.UtcNow || challenge.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6) ||
            !Uri.TryCreate(challenge.Url, UriKind.Absolute, out var url) || url.GetLeftPart(UriPartial.Path) != login.GetLeftPart(UriPartial.Path) ||
            url.UserInfo.Length > 0 || url.Fragment.Length > 0 || !url.Query.StartsWith("?key=", StringComparison.Ordinal) ||
            url.Query.Length is < 37 or > 133 || url.Query[5..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("El servidor ha enviado un acceso Google inválido. Revisa la dirección del servidor.");
        Diagnostics.Log.ProtectSecret(challenge.PollSecret); Diagnostics.Log.ProtectSecret(url.Query[5..]);
        lock (_identityGate) { RequireIdentity(generation); _pendingLoginId = challenge.Id; }
        return challenge;
    }

    public async Task<bool> PollLogin(LoginChallenge challenge, CancellationToken token)
    {
        long generation;
        lock (_identityGate)
        {
            generation = _identityGeneration;
            if (challenge.Id != _pendingLoginId) throw new OperationCanceledException("El acceso ya no está vigente.");
        }
        using var response = await _http.PostAsJsonAsync(Endpoint(ServerAddress, "account/login/poll"),
            new LoginPollRequest(challenge.Id, challenge.PollSecret), Protocol.Json, token).ConfigureAwait(false);
        var result = await Read<LoginPoll>(response, token).ConfigureAwait(false);
        if (!result.Ready) return false;
        if (result.Profile == null || result.AccessToken.Length < 32 || result.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidDataException("Sesión de cuenta inválida.");
        AccountInventory.CheckProfile(result.Profile);
        lock (_identityGate)
        {
            RequireIdentity(generation);
            ++_identityGeneration;
            Token = result.AccessToken; ExpiresAt = result.ExpiresAt; Profile = result.Profile; ResetInventorySync();
        }
        Diagnostics.Log.ProtectSecret(result.AccessToken);
        return true;
    }

    public async Task<AccountProfile> Refresh(CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        var profile = identity.Direct ? await identity.Reader!.Read(identity.Uid, identity.AccessToken, token: token).ConfigureAwait(false)
            : await Request<AccountProfile>(HttpMethod.Get, "account/profile", null, token, identity).ConfigureAwait(false);
        PublishProfile(profile, identity.Generation, identity.InventoryRevision);
        return profile;
    }

    public async Task SignOut(CancellationToken token = default)
    {
        string access, address; bool legacy;
        lock (_identityGate)
        {
            access = Token; address = ServerAddress; legacy = SignedIn && !IsDirectFirebaseSession;
            ++_identityGeneration;
            Token = ""; ExpiresAt = default; Profile = null; ResetInventorySync(); _pendingLoginId = "";
            _firebaseReader?.Dispose(); _firebaseReader = null; _firebaseUid = "";
        }
        if (!legacy || address.Length == 0) return;
        var endpoint = Endpoint(address, "account/logout");
        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback) return;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        request.Content = JsonContent.Create(new { }, options: Protocol.Json);
        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
        _ = await Read<ApiError>(response, token).ConfigureAwait(false);
    }
}
