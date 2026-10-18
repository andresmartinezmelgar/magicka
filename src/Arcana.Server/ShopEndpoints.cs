using Arcana.Core;
using FirebaseAdmin.Auth;

namespace Arcana.Server;

public static class ShopEndpoints
{
    public static void MapItemShop(this WebApplication app)
    {
        app.MapGet("/account/shop", (AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
            new ShopResult(await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), "Ofertas cargadas.")));
        app.MapPost("/account/shop/buy", (ShopPurchaseRequest request, AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            bool bought = await accounts.Repository!.Buy(user.Uid, request, context.RequestAborted);
            return new ShopResult(await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted),
                await accounts.Repository.Shop(user.Uid, user.Name, context.RequestAborted), bought ? "Objeto comprado y guardado en tu inventario." : "Esta compra ya estaba registrada.");
        }));
        app.MapPost("/account/shop/ad/start", AdsUnavailable);
        app.MapPost("/account/shop/ad/complete", AdsUnavailable);
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
