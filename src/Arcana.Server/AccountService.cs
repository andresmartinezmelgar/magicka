using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Arcana.Core;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed record AccountIdentity(string Uid, string Name);
public interface IAccountIdentityVerifier
{
    Task<AccountIdentity> Verify(string idToken, CancellationToken token);
}
public sealed class GoogleAccountVerifier(FirebaseAuth auth) : IAccountIdentityVerifier
{
    public async Task<AccountIdentity> Verify(string idToken, CancellationToken token)
    {
        var result = await auth.VerifyIdTokenAsync(idToken, true, token);
        return ReadVerifiedIdentity(result.Uid, result.Claims);
    }
    internal static AccountIdentity ReadVerifiedIdentity(string uid, IReadOnlyDictionary<string, object> verifiedClaims)
    {
        // Firebase Admin decodes nested claims with Newtonsoft. System.Text.Json
        // directly serializes its JValue strings as arrays rather than strings.
        using var document = JsonDocument.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(verifiedClaims));
        var claims = document.RootElement;
        if (!claims.TryGetProperty("firebase", out var firebase) || firebase.ValueKind != JsonValueKind.Object ||
            !firebase.TryGetProperty("sign_in_provider", out var provider) || provider.ValueKind != JsonValueKind.String || provider.GetString() != "google.com")
            throw new UnauthorizedAccessException("Inicia sesión con una cuenta de Google.");
        string name = claims.TryGetProperty("name", out var display) && display.ValueKind == JsonValueKind.String ? display.GetString() ?? "Mago" : "Mago";
        return new(uid, new string(name.Where(c => !char.IsControl(c)).Take(64).ToArray()));
    }
}
public sealed record AccountOptions(string ProjectId, string ApiKey, string AuthDomain, string PublicUrl)
{
    public bool Enabled => ProjectId.Length > 0;
    public static AccountOptions FromEnvironment() => Read(Environment.GetEnvironmentVariable);
    public static AccountOptions Read(Func<string, string?> environment)
    {
        AccountOptions? file = null;
        string path = environment("ARCANA_FIREBASE_CONFIG")?.Trim() ?? "";
        if (path.Length > 0)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16 * 1024) throw new ArgumentException("El archivo ARCANA_FIREBASE_CONFIG no existe o es demasiado grande.");
            file = Protocol.Deserialize<AccountOptions>(File.ReadAllText(path)) ?? throw new ArgumentException("Configuración Firebase inválida.");
        }
        string Get(string key, string? fallback = null)
        {
            string? value = environment(key)?.Trim();
            return string.IsNullOrEmpty(value) ? fallback?.Trim() ?? "" : value;
        }
        var options = new AccountOptions(Get("FIREBASE_PROJECT_ID", file?.ProjectId), Get("FIREBASE_API_KEY", file?.ApiKey),
            Get("FIREBASE_AUTH_DOMAIN", file?.AuthDomain), Get("ARCANA_PUBLIC_URL", file?.PublicUrl).TrimEnd('/'));
        if (new[] { options.ProjectId, options.ApiKey, options.AuthDomain, options.PublicUrl }.All(s => s.Length == 0)) return options;
        if (new[] { options.ProjectId, options.ApiKey, options.AuthDomain, options.PublicUrl }.Any(s => s.Length == 0) ||
            !Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url) || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0 ||
            url.Scheme != "https" && !(url.Scheme == "http" && url.IsLoopback) ||
            options.ProjectId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || Uri.CheckHostName(options.AuthDomain) != UriHostNameType.Dns)
            throw new ArgumentException("Configura FIREBASE_PROJECT_ID, FIREBASE_API_KEY, FIREBASE_AUTH_DOMAIN y ARCANA_PUBLIC_URL (HTTPS) para activar las cuentas.");
        return options;
    }
}

public sealed class AccountService
{
    private sealed record Session(AccountIdentity Identity, DateTimeOffset ExpiresAt);
    private sealed class Challenge(LoginChallenge request, string browserSecret)
    {
        public LoginChallenge Request { get; } = request;
        public string BrowserSecret { get; } = browserSecret;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public AccountIdentity? Identity;
        public LoginPoll? Result;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Challenge> _challenges = new(StringComparer.Ordinal);
    private readonly IAccountIdentityVerifier? _verifier;
    public AccountOptions Options { get; }
    public IAccountRepository? Repository { get; }
    public bool Enabled => Repository != null && _verifier != null;
    private static readonly System.Net.Http.HttpClient GameHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static string GameServiceUrl => (Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_URL") ?? "").TrimEnd('/');
    private static string GameServiceKey => Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY") ?? "";
    public bool GameEnabled => Enabled || (GameServiceUrl.Length > 0 && GameServiceKey.Length >= 32);
    private static Uri GameEndpoint(string path)
    {
        if (!Uri.TryCreate(GameServiceUrl + "/internal/" + path, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("Configura ARCANA_ACCOUNT_SERVICE_URL con HTTPS.");
        return uri;
    }
    private static async Task<T> GameRequest<T>(string path, object? payload, string? bearer, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GameEndpoint(path));
        request.Headers.Add("X-Arcana-Service-Key", GameServiceKey);
        if (!string.IsNullOrEmpty(bearer))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        request.Content = System.Net.Http.Json.JsonContent.Create(payload ?? new { }, options: Protocol.Json);
        using var response = await GameHttp.SendAsync(request, token);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("La sesión de Google no es válida.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Protocol.Json, token)
            ?? throw new InvalidDataException("Respuesta incompleta del servidor de cuentas.");
    }
    public async Task<AccountIdentity?> AuthenticateGame(string accessToken, CancellationToken token)
    {
        if (Enabled)
        {
            var legacySession = Authenticate(accessToken);
            if (legacySession != null) return legacySession;
            try { return await _verifier!.Verify(accessToken, token); }
            catch (FirebaseAuthException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        if (!GameEnabled) return null;
        try { return await GameRequest<AccountIdentity>("session", null, accessToken, token); }
        catch (UnauthorizedAccessException) { return null; }
    }
    public Task<AccountProfile> ProfileGame(AccountIdentity identity, CancellationToken token) =>
        Enabled ? Repository!.Profile(identity.Uid, identity.Name, token)
            : GameRequest<AccountProfile>("profile", identity, null, token);
    public async Task AwardGame(RewardMatch match, CancellationToken token)
    {
        if (Enabled) await Repository!.Award(match, token);
        else _ = await GameRequest<ApiError>("award", match, null, token);
    }

    public async Task SaveBestiaryGame(BestiaryProgress progress, CancellationToken token)
    {
        if (Enabled) await Repository!.SaveBestiary(progress, token);
        else _ = await GameRequest<ApiError>("bestiary", progress, null, token);
    }

    public AccountService(AccountOptions options, IAccountIdentityVerifier? verifier = null, IAccountRepository? repository = null)
    {
        Options = options;
        if (verifier != null && repository != null) { _verifier = verifier; Repository = repository; return; }
        if (!options.Enabled) return;
        var credential = GoogleCredential.GetApplicationDefault();
        var app = FirebaseApp.Create(new AppOptions { ProjectId = options.ProjectId, Credential = credential }, "arcana-accounts");
        _verifier = new GoogleAccountVerifier(FirebaseAuth.GetAuth(app));
        Repository = new FirestoreAccountRepository(new FirestoreDbBuilder { ProjectId = options.ProjectId, Credential = credential }.Build());
    }
    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public LoginChallenge StartLogin()
    {
        if (!Enabled) throw new InvalidOperationException("Este servidor todavía no tiene configuradas las cuentas Firebase.");
        Cleanup();
        if (_challenges.Count >= 128 || _sessions.Count >= 10000) throw new InvalidOperationException("Hay demasiados accesos pendientes. Vuelve a intentarlo después.");
        string id = Guid.NewGuid().ToString("N"), poll = Secret(), browser = Secret();
        var request = new LoginChallenge(id, poll, Options.PublicUrl + "/account/login/" + id + "?key=" + browser, DateTimeOffset.UtcNow.AddMinutes(5));
        Diagnostics.Log.ProtectSecret(poll); Diagnostics.Log.ProtectSecret(browser);
        if (!_challenges.TryAdd(id, new(request, browser))) throw new InvalidOperationException("No se pudo iniciar el acceso.");
        return request;
    }
    private Challenge Find(string id)
    {
        if (id == null || !_challenges.TryGetValue(id, out var request) || request.Request.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("El acceso ha caducado. Inícialo de nuevo desde el juego.");
        return request;
    }
    public bool BrowserAllowed(string id, string key)
    { try { return Fixed(key, Find(id).BrowserSecret); } catch (UnauthorizedAccessException) { return false; } }
    private static bool Fixed(string? a, string b) => a != null && a.Length <= 128 &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
    public async Task CompleteLogin(string id, LoginCompleteRequest request, CancellationToken token)
    {
        var challenge = Find(id);
        if (!Fixed(request.BrowserSecret, challenge.BrowserSecret) || string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length > 3500)
            throw new UnauthorizedAccessException("Acceso inválido.");
        await challenge.Gate.WaitAsync(token);
        try
        {
            if (challenge.Identity != null) return;
            var identity = await _verifier!.Verify(request.IdToken, token);
            _ = await Repository!.Profile(identity.Uid, identity.Name, token);
            challenge.Identity = identity;
        }
        finally { challenge.Gate.Release(); }
    }
    public async Task<LoginPoll> Poll(LoginPollRequest request, CancellationToken token)
    {
        var challenge = Find(request.Id);
        if (!Fixed(request.PollSecret, challenge.Request.PollSecret)) throw new UnauthorizedAccessException("Acceso inválido.");
        await challenge.Gate.WaitAsync(token);
        try
        {
            if (challenge.Identity == null) return new(false);
            if (challenge.Result != null) return challenge.Result;
            var profile = await Repository!.Profile(challenge.Identity.Uid, challenge.Identity.Name, token);
            string access = Secret(); var expires = DateTimeOffset.UtcNow.AddHours(8);
            Diagnostics.Log.ProtectSecret(access);
            _sessions[Hash(access)] = new(challenge.Identity, expires);
            return challenge.Result = new(true, access, expires, profile);
        }
        finally { challenge.Gate.Release(); }
    }
    public AccountIdentity? Authenticate(string? accessToken)
    {
        if (!Enabled || accessToken == null || accessToken.Length is < 32 or > 128) return null;
        return _sessions.TryGetValue(Hash(accessToken), out var session) && session.ExpiresAt > DateTimeOffset.UtcNow ? session.Identity : null;
    }
    public void Logout(string accessToken) => _sessions.TryRemove(Hash(accessToken), out _);
    private void Cleanup()
    {
        foreach (var entry in _sessions) if (entry.Value.ExpiresAt <= DateTimeOffset.UtcNow) _sessions.TryRemove(entry.Key, out _);
        foreach (var entry in _challenges) if (entry.Value.Request.ExpiresAt <= DateTimeOffset.UtcNow) _challenges.TryRemove(entry.Key, out _);
    }
}
