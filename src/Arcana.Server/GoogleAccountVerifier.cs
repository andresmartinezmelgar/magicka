using System.Text.Json;
using FirebaseAdmin.Auth;

namespace Arcana.Server;

public sealed record AccountIdentity(string Uid, string Name) { public long AuthTime { get; init; } }

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
        return new(uid, new string(name.Where(c => !char.IsControl(c)).Take(64).ToArray()))
            { AuthTime = claims.TryGetProperty("auth_time", out var at) && at.ValueKind == JsonValueKind.Number && at.TryGetInt64(out long seconds) ? seconds : 0 };
    }
}
