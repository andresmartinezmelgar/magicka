using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var claims = JsonSerializer.SerializeToElement(result.Claims);
        if (!claims.TryGetProperty("firebase", out var firebase) || !firebase.TryGetProperty("sign_in_provider", out var provider) || provider.GetString() != "google.com")
            throw new UnauthorizedAccessException("Inicia sesión con una cuenta de Google.");
        string name = claims.TryGetProperty("name", out var display) ? display.GetString() ?? "Mago" : "Mago";
        return new(result.Uid, new string(name.Where(c => !char.IsControl(c)).Take(64).ToArray()));
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
            challenge.Identity = await _verifier!.Verify(request.IdToken, token);
            _ = await Repository!.Profile(challenge.Identity.Uid, challenge.Identity.Name, token);
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
