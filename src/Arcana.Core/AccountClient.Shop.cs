namespace Arcana.Core;

public sealed partial class AccountClient
{
    public Task<ShopResult> Shop(CancellationToken token = default) => ShopRequest(HttpMethod.Get, "account/shop", null, token);
    public Task<ShopResult> BuyShopItem(ShopPurchaseRequest request, CancellationToken token = default) =>
        ShopRequest(HttpMethod.Post, "account/shop/buy", request, token);
    public Task<ShopResult> CompleteShopAd(ShopAdCompleteRequest request, CancellationToken token = default) =>
        ShopRequest(HttpMethod.Post, "account/shop/ad/complete", request, token);

    private async Task<ShopResult> ShopRequest(HttpMethod method, string path, object? body, CancellationToken token)
    {
        var identity = CaptureIdentity();
        var result = await Request<ShopResult>(method, path, body, token, identity).ConfigureAwait(false);
        if (!ShopRules.Valid(result.Shop) || result.Profile == null || result.Profile.Gold < 0)
            throw new InvalidDataException("Respuesta de tienda inválida.");
        PublishProfile(result.Profile, identity.Generation);
        return result;
    }

    public async Task<ShopAdTicket> StartShopAd(string shopId, CancellationToken token = default)
    {
        var identity = CaptureIdentity();
        var ticket = await Request<ShopAdTicket>(HttpMethod.Post, "account/shop/ad/start", new ShopAdRequest(shopId), token, identity).ConfigureAwait(false);
        if (!Guid.TryParseExact(ticket.Id, "N", out _) || ticket.ShopId != shopId || ticket.ReadyAt >= ticket.ExpiresAt ||
            ticket.ExpiresAt <= DateTimeOffset.UtcNow || ticket.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6))
            throw new InvalidDataException("Anuncio simulado inválido.");
        return ticket;
    }
}
