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

public sealed partial class AccountService
{
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
