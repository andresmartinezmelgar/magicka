using Arcana.Core;
using FirebaseAdmin.Auth;

namespace Arcana.Server;

public static class ShopEndpoints
{
    public static void MapItemShop(this WebApplication app)
    {
        var verifier = new AdMobRewardVerifier();
        var billing = new PlayBilling(app.Configuration);
        async Task<ShopResult> Result(IAccountRepository repository, AccountProfile profile, ItemShop shop, string message, CancellationToken token)
        {
            var advertising = await repository.Advertising(token);
            return new(profile, shop, message, DateTimeOffset.UtcNow)
            {
                AdsEnabled = advertising.Enabled,
                BillingProducts = billing.Products,
                RewardedAds = advertising.RequiresAd(profile.Access, DateTimeOffset.UtcNow)
                    ? AdMobRewardVerifier.Configuration(app.Configuration) : null,
                NoAdsProductId = billing.Product
            };
        }
        app.MapGet("/account/advertising", (AccountService accounts, HttpContext context) => Auth(accounts, context,
            async _ => await accounts.Repository!.Advertising(context.RequestAborted)));
        app.MapPost("/account/advertising", (AdvertisingUpdate update, AccountService accounts, HttpContext context) => Auth(accounts, context,
            async user => await accounts.Repository!.UpdateAdvertising(user.Uid, update, context.RequestAborted)));
        app.MapGet("/account/shop", (AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            await billing.RefreshIfDue(accounts.Repository, user.Uid, context.RequestAborted);
            return await Result(accounts.Repository!, await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), "Ofertas cargadas.", context.RequestAborted);
        }));
        app.MapPost("/account/shop/play/verify", (PlayNoAdsRequest request, AccountService accounts, HttpContext context) =>
            Auth(accounts, context, async user =>
            {
                if (accounts.Repository is not FirestoreAccountRepository repository || billing.Product == null)
                    throw new InvalidOperationException("Las compras de Google Play aún no están disponibles.");
                await billing.Verify(repository, user.Uid, request.PurchaseToken, true, context.RequestAborted);
                return await Result(repository, await repository.Profile(user.Uid, user.Name, context.RequestAborted),
                    await repository.Shop(user.Uid, user.Name, context.RequestAborted), "Compra sin anuncios confirmada.", context.RequestAborted);
            }));
        app.MapPost("/account/shop/play/purchase", (StorePurchaseRequest request, AccountService accounts, HttpContext context) =>
            Auth(accounts, context, async user =>
            {
                if (accounts.Repository is not FirestoreAccountRepository repository)
                    throw new InvalidOperationException("Las compras necesitan un servidor conectado a Firebase.");
                await repository.Profile(user.Uid, user.Name, context.RequestAborted);
                await billing.VerifyStore(repository, user.Uid, request, context.RequestAborted);
                return await Result(repository, await repository.Profile(user.Uid, user.Name, context.RequestAborted),
                    await repository.Shop(user.Uid, user.Name, context.RequestAborted), "Compra confirmada y guardada en tu cuenta.", context.RequestAborted);
            }));
        app.MapPost("/account/shop/buy", (ShopPurchaseRequest request, AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            bool bought = await accounts.Repository!.Buy(user.Uid, request, context.RequestAborted);
            return await Result(accounts.Repository, await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), bought ? "Objeto comprado y guardado en tu inventario." : "Esta compra ya estaba registrada.", context.RequestAborted);
        }));
        app.MapPost("/account/shop/ad/start", (ShopAdRequest request, AccountService accounts, HttpContext context) =>
            AdMobRewardVerifier.Configuration(app.Configuration) is not { } config ? Task.FromResult(AdsUnavailable()) :
            Auth(accounts, context, async user => (await accounts.Repository!.StartVerifiedShopAd(user.Uid, request, context.RequestAborted))
                with { AdUnitId = config.UnitId }));
        app.MapPost("/account/shop/refresh", (ShopAdRequest request, AccountService accounts, HttpContext context) =>
            Auth(accounts, context, async user =>
            {
                await billing.RefreshIfDue(accounts.Repository, user.Uid, context.RequestAborted);
                var shop = await accounts.Repository!.RefreshShopWithoutAd(user.Uid, request, context.RequestAborted);
                return await Result(accounts.Repository, await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted),
                    shop, "Ofertas renovadas sin anuncios. Disponible de nuevo mañana.", context.RequestAborted);
            }));
        app.MapPost("/account/shop/ad/complete", (ShopAdCompleteRequest request, AccountService accounts, HttpContext context) =>
            AdMobRewardVerifier.Configuration(app.Configuration) == null ? Task.FromResult(AdsUnavailable()) :
            Auth(accounts, context, async user => await Result(accounts.Repository!, await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.CompleteVerifiedShopAd(user.Uid, request, context.RequestAborted), "Ofertas renovadas. Disponible de nuevo mañana.", context.RequestAborted)));
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
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
            { context.Response.Headers.RetryAfter = "300"; return Results.StatusCode(503); }
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
            var identity = await accounts.AuthenticateHttp(context);
            return identity == null ? Results.Unauthorized() : Results.Json(await operation(identity), Protocol.Json);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or FirebaseAuthException)
        { return Results.Json(new ApiError("La sesión ha caducado. Inicia sesión con Google."), Protocol.Json, statusCode: 401); }
        catch (System.Security.SecurityException e)
        { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 403); }
        catch (PlayUnavailableException e) { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 503); }
        catch (ArgumentException e) { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 400); }
        catch (InvalidOperationException e) { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 409); }
    }
}
