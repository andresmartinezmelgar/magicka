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
            if (legacySession != null) return await Repository!.IsDeletionRequested(legacySession.Uid, token) ? null : legacySession;
            try
            {
                var identity = await _verifier!.Verify(accessToken, token);
                return await Repository!.IsDeletionRequested(identity.Uid, token) ? null : identity;
            }
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

    public async Task SaveMatchStatisticsGame(MatchStatisticsReport report, CancellationToken token)
    {
        if (Enabled) await Repository!.SaveMatchStatistics(report, token);
        else if (GameEnabled) _ = await GameRequest<ApiError>("statistics", report, null, token);
        else throw new InvalidOperationException("Firebase no está disponible.");
    }

    public async Task SaveBestiaryGame(BestiaryProgress progress, CancellationToken token)
    {
        if (Enabled) await Repository!.SaveBestiary(progress, token);
        else _ = await GameRequest<ApiError>("bestiary", progress, null, token);
    }
}
