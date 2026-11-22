using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcana.Core;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace Arcana.Server;

internal sealed record PlayReceipt(string Token, string Product, long CheckedAt, bool Acknowledged);
internal sealed record PlayPurchase(bool Purchased, bool Acknowledged);
internal sealed class PlayUnavailableException() : Exception("Google Play no está disponible. Vuelve a consultar la tienda.");

// Solo el servidor consulta al editor de Google. El teléfono nunca decide si se concede la compra.
internal sealed partial class PlayBilling
{
    internal const string Package = "com.arcana.arena";
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 64 * 1024 };
    private readonly Lazy<GoogleCredential> _credential = new(() =>
        GoogleCredential.GetApplicationDefault().CreateScoped("https://www.googleapis.com/auth/androidpublisher"));
    internal string? Product { get; }
    internal StoreProduct[] Products { get; }
    internal PlayBilling(IConfiguration config)
    {
        string? id = config["ARCANA_PLAY_NO_ADS_PRODUCT_ID"];
        bool shop = config.GetValue("ARCANA_PLAY_SHOP_ENABLED", false);
        if (shop && string.IsNullOrWhiteSpace(id)) id = "arcana_no_ads_forever";
        if (id != null && Regex.IsMatch(id, @"\A[a-z0-9][a-z0-9_.]{0,39}\z")) Product = id;
        Products = shop && Product != null ? StoreCatalogue.Products(Product) : Product != null
            ? StoreCatalogue.Products(Product).Where(p => p.Key == "no_ads_forever").ToArray() : [];
    }
    internal static PlayPurchase ReadPurchase(JsonElement json, string uid, string product)
    {
        if (!json.TryGetProperty("obfuscatedExternalAccountId", out var owner) || owner.GetString() != PlayNoAdsRequest.Owner(uid))
            throw new InvalidOperationException("La compra no corresponde a esta cuenta del juego.");
        if (!json.TryGetProperty("productLineItem", out var items) || items.ValueKind != JsonValueKind.Array ||
            !items.EnumerateArray().Any(i => i.TryGetProperty("productId", out var id) && id.GetString() == product))
            throw new InvalidOperationException("La compra no corresponde al producto sin anuncios.");
        if (!json.TryGetProperty("purchaseStateContext", out var context) || !context.TryGetProperty("purchaseState", out var state))
            throw new PlayUnavailableException();
        bool acknowledged = json.TryGetProperty("acknowledgementState", out var ack) &&
            ack.GetString() == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED";
        return state.GetString() switch
        {
            "PURCHASED" => new(true, acknowledged),
            "CANCELLED" or "PENDING" => new(false, false),
            _ => throw new PlayUnavailableException()
        };
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, CancellationToken token)
    {
        try
        {
            string access = await _credential.Value.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: token);
            using var request = new HttpRequestMessage(method, "https://androidpublisher.googleapis.com/androidpublisher/v3/applications/" + Package + "/purchases/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            if (method == HttpMethod.Post) request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            return await Client.SendAsync(request, token);
        }
        catch (Exception) when (!token.IsCancellationRequested) { throw new PlayUnavailableException(); }
    }
    internal async Task Verify(FirestoreAccountRepository db, string uid, string purchaseToken, bool submitted, CancellationToken token)
    {
        if (Product == null) throw new InvalidOperationException("El producto sin anuncios aún no está disponible.");
        if (string.IsNullOrWhiteSpace(purchaseToken) || purchaseToken.Length > 2048 || purchaseToken.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Token de compra inválido.");
        var previous = await db.PlayReceiptFor(uid, token);
        if (!submitted && previous?.Token != purchaseToken) return;
        long checkedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string escaped = Uri.EscapeDataString(purchaseToken);
        using var response = await Send(HttpMethod.Get, "productsv2/tokens/" + escaped, token);
        PlayPurchase purchase;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) purchase = new(false, false);
        else
        {
            if (!response.IsSuccessStatusCode) throw new PlayUnavailableException();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            purchase = ReadPurchase(json.RootElement, uid, Product);
        }
        if (!purchase.Purchased)
        {
            if (previous?.Token == purchaseToken)
                await db.StorePlayReceipt(uid, previous, null, token);
            if (submitted) throw new InvalidOperationException("Google Play no ha confirmado una compra activa. Si el pago está pendiente, vuelve a restaurarlo cuando se complete.");
            return;
        }
        var verified = new PlayReceipt(purchaseToken, Product, checkedAt, purchase.Acknowledged);
        if (!await db.StorePlayReceipt(uid, previous, verified, token))
        {
            if (submitted) throw new InvalidOperationException("La compra ha cambiado en otro dispositivo. Pulsa Restaurar compra.");
            return;
        }
        if (purchase.Acknowledged) return;
        using var ack = await Send(HttpMethod.Post, "products/" + Product + "/tokens/" + escaped + ":acknowledge", token);
        if (!ack.IsSuccessStatusCode) throw new PlayUnavailableException();
        await db.StorePlayReceipt(uid, verified, verified with { Acknowledged = true }, token);
    }
    internal async Task RefreshIfDue(IAccountRepository? repository, string uid, CancellationToken token)
    {
        if (Product == null || repository is not FirestoreAccountRepository db) return;
        if (Products.Any(p => p.Months > 0)) await RefreshPassIfDue(db, uid, token);
        var current = await db.PlayReceiptFor(uid, token);
        if (current == null || current.Product != Product || current.Acknowledged &&
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - current.CheckedAt < TimeSpan.FromHours(12).TotalMilliseconds) return;
        try { await Verify(db, uid, current.Token, false, token); }
        catch (PlayUnavailableException) { /* La tienda sigue abierta. El servidor respeta el vencimiento de la última verificación. */ }
    }
}

public sealed partial class FirestoreAccountRepository
{
    internal async Task<PlayReceipt?> PlayReceiptFor(string uid, CancellationToken token)
    {
        var receipt = await User(uid).Collection("billing").Document("noAds").GetSnapshotAsync(token);
        return receipt.Exists ? FirestoreData.Read<PlayReceipt>(receipt) : null;
    }
    internal Task<bool> StorePlayReceipt(string uid, PlayReceipt? expected, PlayReceipt? next, CancellationToken token) =>
        database.RunTransactionAsync(async tx =>
        {
            if ((await tx.GetSnapshotAsync(Deletion(uid), token)).Exists) throw new UnauthorizedAccessException();
            var doc = User(uid).Collection("billing").Document("noAds");
            var current = await tx.GetSnapshotAsync(doc, token);
            if ((current.Exists ? FirestoreData.Read<PlayReceipt>(current) : null) != expected) return false;
            if (next == null) tx.Delete(doc); else tx.Set(doc, FirestoreData.Map(next));
            long validUntil = next == null ? 0 : DateTimeOffset.FromUnixTimeMilliseconds(next.CheckedAt).AddHours(24).ToUnixTimeSeconds();
            tx.Set(AccountConfigurationDocument(uid), new Dictionary<string, object> { ["playAdsValidUntil"] = validUntil }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: token);
}
