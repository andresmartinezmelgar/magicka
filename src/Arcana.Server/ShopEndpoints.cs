using Arcana.Core;
using FirebaseAdmin.Auth;

namespace Arcana.Server;

public static class ShopEndpoints
{
    public static void MapItemShop(this WebApplication app)
    {
        var verifier = new AdMobRewardVerifier();
        ShopResult Result(AccountProfile profile, ItemShop shop, string message) => new(profile, shop, message, DateTimeOffset.UtcNow)
            { RewardedAds = AdMobRewardVerifier.Configuration(app.Configuration) };
        app.MapGet("/account/shop", (AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
            Result(await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), "Ofertas cargadas.")));
        app.MapPost("/account/shop/buy", (ShopPurchaseRequest request, AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            bool bought = await accounts.Repository!.Buy(user.Uid, request, context.RequestAborted);
            return Result(await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), bought ? "Objeto comprado y guardado en tu inventario." : "Esta compra ya estaba registrada.");
        }));
        app.MapPost("/account/shop/ad/start", (ShopAdRequest request, AccountService accounts, HttpContext context) =>
            AdMobRewardVerifier.Configuration(app.Configuration) is not { } config ? Task.FromResult(AdsUnavailable()) :
            Auth(accounts, context, async user => (await accounts.Repository!.StartVerifiedShopAd(user.Uid, request, context.RequestAborted))
                with { AdUnitId = config.UnitId }));
        app.MapPost("/account/shop/ad/complete", (ShopAdCompleteRequest request, AccountService accounts, HttpContext context) =>
            AdMobRewardVerifier.Configuration(app.Configuration) == null ? Task.FromResult(AdsUnavailable()) :
            Auth(accounts, context, async user => Result(await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.CompleteVerifiedShopAd(user.Uid, request, context.RequestAborted), "Ofertas renovadas. Disponible de nuevo mañana.")));
        app.MapGet("/account/shop/ad/ssv", async (AccountService accounts, HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            if (!accounts.Enabled || AdMobRewardVerifier.Configuration(app.Configuration) is not { } config) return AdsUnavailable();
            try
            {
                var reward = await verifier.Verify(context.Request.QueryString.Value ?? "", config.UnitId, context.RequestAborted);
                // La consola de AdMob puede comprobar la URL sin crear una recompensa real.
                // También esta comprobación exige firma válida y la unidad configurada.
                if (reward.IsSetupProbe) return Results.Ok();
                await accounts.Repository!.ConfirmVerifiedShopAd(reward, context.RequestAborted);
                return Results.Ok();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException)
            { return Results.BadRequest(new ApiError("Confirmación publicitaria inválida.")); }
        });
    }
    private static IResult AdsUnavailable() => Results.Json(new ApiError("La renovación por anuncios todavía no está disponible."),
        Protocol.Json, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static async Task<IResult> Auth(AccountService accounts, HttpContext context, Func<AccountIdentity, Task<object>> operation)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!accounts.Enabled) return Results.Json(new ApiError("La tienda necesita un servidor conectado a Firebase."), Protocol.Json, statusCode: 503);
        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return Results.Unauthorized();
        try
        {
            var identity = await accounts.AuthenticateGame(authorization[7..], context.RequestAborted);
            return identity == null ? Results.Unauthorized() : Results.Json(await operation(identity), Protocol.Json);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or FirebaseAuthException)
        { return Results.Json(new ApiError("La sesión ha caducado. Inicia sesión con Google."), Protocol.Json, statusCode: 401); }
        catch (ArgumentException e) { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 400); }
        catch (InvalidOperationException e) { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 409); }
    }
}
