using Arcana.Core;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Arcana.Server;

internal sealed record PlayPassReceipt(string Token, string Product, string BasePlan, long ExpiresAt, long CheckedAt, bool Acknowledged);
internal sealed record PlayPassPurchase(bool Purchased, bool Acknowledged, string BasePlan, long ExpiresAt);

internal sealed partial class PlayBilling
{
    private static void ValidPurchaseToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(c => !char.IsAscii(c) || char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new ArgumentException("Token de compra inválido.");
    }

    internal static bool GoldConsumed(JsonElement json, string product)
    {
        var lines = json.GetProperty("productLineItem").EnumerateArray().ToArray();
        if (lines.Length != 1 || lines[0].GetProperty("productId").GetString() != product ||
            !lines[0].TryGetProperty("productOfferDetails", out var offer))
            throw new InvalidOperationException("El recibo no contiene el paquete de oro esperado.");
        if (offer.TryGetProperty("quantity", out var quantity) && (!quantity.TryGetInt32(out var count) || count != 1))
            throw new InvalidOperationException("La compra debe contener una unidad del paquete.");
        if (offer.TryGetProperty("refundableQuantity", out var refundable) && (!refundable.TryGetInt32(out var remaining) || remaining < 1))
            throw new InvalidOperationException("La compra de oro ha sido reembolsada.");
        if (offer.TryGetProperty("rentOfferDetails", out var rental) && rental.ValueKind != JsonValueKind.Null ||
            offer.TryGetProperty("preorderOfferDetails", out var preorder) && preorder.ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("El oro no admite alquileres ni reservas.");
        return offer.TryGetProperty("consumptionState", out var consumed) ? consumed.GetString() switch
        {
            "CONSUMPTION_STATE_CONSUMED" => true,
            "CONSUMPTION_STATE_YET_TO_BE_CONSUMED" => false,
            _ => throw new PlayUnavailableException()
        } : throw new PlayUnavailableException();
    }

    internal static PlayPassPurchase ReadPass(JsonElement json, string uid)
    {
        if (!json.TryGetProperty("externalAccountIdentifiers", out var identity) ||
            !identity.TryGetProperty("obfuscatedExternalAccountId", out var owner) || owner.GetString() != PlayNoAdsRequest.Owner(uid))
            throw new InvalidOperationException("El pase no pertenece a esta cuenta del juego.");
        if (!json.TryGetProperty("lineItems", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 1)
            throw new InvalidOperationException("El pase no corresponde a un plan compatible.");
        var item = items[0];
        if (item.GetProperty("productId").GetString() != StoreCatalogue.PassProduct ||
            !item.TryGetProperty("prepaidPlan", out var prepaid) || prepaid.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("offerDetails", out var offer) || !offer.TryGetProperty("basePlanId", out var plan) ||
            plan.GetString() is not ("month-prepaid" or "six-months-prepaid"))
            throw new InvalidOperationException("Solo se admiten pases de prepago de uno o seis meses, sin renovación automática.");
        string basePlan = plan.GetString()!;
        string? state = json.GetProperty("subscriptionState").GetString();
        if (state is "SUBSCRIPTION_STATE_PENDING" or "SUBSCRIPTION_STATE_EXPIRED" or "SUBSCRIPTION_STATE_ON_HOLD" or
            "SUBSCRIPTION_STATE_PAUSED" or "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED") return new(false, false, basePlan, 0);
        if (state is not ("SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_CANCELED")) throw new PlayUnavailableException();
        if (!item.TryGetProperty("expiryTime", out var expiry) || !DateTimeOffset.TryParse(expiry.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var until)) throw new PlayUnavailableException();
        return new(until > DateTimeOffset.UtcNow,
            json.TryGetProperty("acknowledgementState", out var ack) && ack.GetString() == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
            basePlan, until.ToUnixTimeSeconds());
    }

    internal async Task VerifyStore(FirestoreAccountRepository database, string uid, StorePurchaseRequest request, CancellationToken token)
    {
        ValidPurchaseToken(request.PurchaseToken);
        var products = Products.Where(p => p.ProductId == request.ProductId).ToArray();
        if (products.Length == 0) throw new ArgumentException("Este producto no está habilitado en la tienda.");
        if (products.Any(p => p.Months > 0))
        { await VerifyPass(database, uid, request.PurchaseToken, true, token); return; }
        var product = products.Single();
        if (product.Gold == 0) { await Verify(database, uid, request.PurchaseToken, true, token); return; }
        string escaped = Uri.EscapeDataString(request.PurchaseToken);
        using var response = await Send(HttpMethod.Get, "productsv2/tokens/" + escaped, token);
        if (!response.IsSuccessStatusCode) throw new PlayUnavailableException();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!ReadPurchase(json.RootElement, uid, product.ProductId).Purchased)
            throw new InvalidOperationException("El oro se entregará cuando Google Play confirme el pago.");
        bool consumed = GoldConsumed(json.RootElement, product.ProductId);
        // La entrega se registra primero. Un fallo al consumir se puede reintentar
        // desde Restaurar compras sin volver a incrementar el saldo.
        await database.CreditPlayGold(uid, product, request.PurchaseToken, consumed, token);
        if (consumed) return;
        using var consume = await Send(HttpMethod.Post, "products/" + product.ProductId + "/tokens/" + escaped + ":consume", token);
        if (consume.IsSuccessStatusCode) return;
        // Dos dispositivos pueden confirmar el mismo recibo simultáneamente.
        using var checkedAgain = await Send(HttpMethod.Get, "productsv2/tokens/" + escaped, token);
        if (checkedAgain.IsSuccessStatusCode)
        {
            using var current = JsonDocument.Parse(await checkedAgain.Content.ReadAsStringAsync(token));
            if (ReadPurchase(current.RootElement, uid, product.ProductId).Purchased && GoldConsumed(current.RootElement, product.ProductId)) return;
        }
        throw new PlayUnavailableException();
    }

    private async Task VerifyPass(FirestoreAccountRepository database, string uid, string purchaseToken, bool submitted, CancellationToken token)
    {
        ValidPurchaseToken(purchaseToken);
        var previous = await database.PlayPassFor(uid, token);
        if (!submitted && previous?.Token != purchaseToken) return;
        long checkedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string escaped = Uri.EscapeDataString(purchaseToken);
        using var response = await Send(HttpMethod.Get, "subscriptionsv2/tokens/" + escaped, token);
        PlayPassPurchase purchase;
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) purchase = new(false, false, "", 0);
        else
        {
            if (!response.IsSuccessStatusCode) throw new PlayUnavailableException();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            purchase = ReadPass(json.RootElement, uid);
        }
        if (!purchase.Purchased)
        {
            if (previous?.Token == purchaseToken) await database.StorePlayPass(uid, previous, null, token);
            if (submitted) throw new InvalidOperationException("Google Play no ha confirmado un pase vigente. Restaura la compra cuando se complete el pago.");
            return;
        }
        var receipt = new PlayPassReceipt(purchaseToken, StoreCatalogue.PassProduct, purchase.BasePlan, purchase.ExpiresAt, checkedAt, purchase.Acknowledged);
        if (!await database.StorePlayPass(uid, previous, receipt, token))
        {
            if (submitted) throw new InvalidOperationException("El pase ha cambiado en otro dispositivo. Pulsa Restaurar compras.");
            return;
        }
        if (purchase.Acknowledged) return;
        using var ack = await Send(HttpMethod.Post, "subscriptions/" + StoreCatalogue.PassProduct + "/tokens/" + escaped + ":acknowledge", token);
        if (!ack.IsSuccessStatusCode) throw new PlayUnavailableException();
        await database.StorePlayPass(uid, receipt, receipt with { Acknowledged = true }, token);
    }

    private async Task RefreshPassIfDue(FirestoreAccountRepository database, string uid, CancellationToken token)
    {
        var receipt = await database.PlayPassFor(uid, token);
        if (receipt == null || receipt.Acknowledged && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - receipt.CheckedAt < TimeSpan.FromHours(12).TotalMilliseconds) return;
        try { await VerifyPass(database, uid, receipt.Token, false, token); }
        catch (PlayUnavailableException) { /* La última verificación caduca en un máximo de 24 horas. */ }
    }
}
