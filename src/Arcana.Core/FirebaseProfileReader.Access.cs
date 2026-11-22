namespace Arcana.Core;

public sealed partial class FirebaseProfileReader
{
    public async Task<AccountAccess> ReadAccess(string uid, string idToken, CancellationToken token = default)
    {
        if (uid.Length is < 1 or > 128 || uid.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("UID inválido.", nameof(uid));
        System.Text.Json.JsonElement? document;
        try { document = await Document("users/" + uid + "/configuration/account", idToken, token).ConfigureAwait(false); }
        // Una instalación con reglas antiguas sigue cargando el inventario, sin
        // conceder permisos ni exenciones hasta publicar la configuración nueva.
        catch (UnauthorizedAccessException) { return new(); }
        if (document is not { } value || !value.TryGetProperty("fields", out var fields) ||
            fields.ValueKind != System.Text.Json.JsonValueKind.Object) return new();
        bool Flag(string key) => fields.TryGetProperty(key, out var field) && field.ValueKind == System.Text.Json.JsonValueKind.Object &&
            field.TryGetProperty("booleanValue", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True;
        long Expiry(string key) => fields.TryGetProperty(key, out var paid) && paid.ValueKind == System.Text.Json.JsonValueKind.Object &&
            paid.TryGetProperty("integerValue", out var integer) && integer.ValueKind == System.Text.Json.JsonValueKind.String &&
            long.TryParse(integer.GetString(), out var expiry) ? Math.Max(0, expiry) : 0;
        return new(Flag("isAdmin"), Flag("previewUpdates"), Flag("matchStatistics"), Flag("adsRemoved"))
            { PlayAdsValidUntil = Expiry("playAdsValidUntil"), PlayPassValidUntil = Expiry("playPassValidUntil"), PlayPassExpiresAt = Expiry("playPassExpiresAt") };
    }
}
