using Arcana.Core;
using FirebaseAdmin.Auth;

namespace Arcana.Server;

public static class AccountEndpoints
{
    public static void MapAccounts(this WebApplication app)
    {
        app.MapGet("/account/config", (AccountService accounts) => Results.Json(new AccountConfiguration(accounts.Enabled,
            accounts.Enabled ? "Cuenta persistente y recompensas PvP disponibles." : "Las cuentas y recompensas necesitan configurar Firebase en el servidor central."), Protocol.Json));
        app.MapPost("/account/login/start", (AccountService accounts) => Try(() => Task.FromResult<object>(accounts.StartLogin())));
        app.MapPost("/account/login/poll", (LoginPollRequest request, AccountService accounts, HttpContext context) =>
            Try(async () => await accounts.Poll(request, context.RequestAborted)));
        app.MapGet("/account/login/{id}", (string id, string key, AccountService accounts, HttpContext context) =>
        {
            if (!accounts.BrowserAllowed(id, key)) return Results.Text("El acceso ha caducado. Vuelve a iniciarlo desde Arcana Arena.", statusCode: 401);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Content(AccountLoginPage.Render(accounts.Options, id, key), "text/html; charset=utf-8");
        });
        app.MapPost("/account/login/{id}/complete", (string id, LoginCompleteRequest request, AccountService accounts, HttpContext context) =>
            Try(async () => { await accounts.CompleteLogin(id, request, context.RequestAborted); return new ApiError(""); }));
        app.MapGet("/account/profile", (AccountService accounts, HttpContext context) => Auth(accounts, context,
            async user => await accounts.Repository!.Profile(user.Uid, user.Name, context.RequestAborted)));
        app.MapPost("/account/equip", (InventoryRequest request, AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            await accounts.Repository!.Equip(user.Uid, request, context.RequestAborted);
            return new InventoryResult(await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted), "Equipamiento guardado.");
        }));
        app.MapPost("/account/sell", (InventoryRequest request, AccountService accounts, HttpContext context) => Auth(accounts, context, async user =>
        {
            int gold = await accounts.Repository!.Sell(user.Uid, request.ItemId, context.RequestAborted);
            return new InventoryResult(await accounts.Repository.Profile(user.Uid, user.Name, context.RequestAborted),
                gold > 0 ? $"Objeto eliminado: +{gold} de oro." : "Esta venta ya estaba registrada.");
        }));
        app.MapPost("/account/logout", (AccountService accounts, HttpContext context) => Auth(accounts, context, user =>
        { accounts.Logout(Bearer(context)); return Task.FromResult<object>(new ApiError("")); }));
    }
    private static string Bearer(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : "";
    }
    private static Task<IResult> Auth(AccountService accounts, HttpContext context, Func<AccountIdentity, Task<object>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        var identity = accounts.Authenticate(Bearer(context));
        return identity == null ? Task.FromResult<IResult>(Results.Json(new ApiError("Inicia sesión con Google para acceder a tu cuenta."), Protocol.Json, statusCode: 401))
            : Try(() => action(identity));
    }
    private static async Task<IResult> Try(Func<Task<object>> action)
    {
        try { return Results.Json(await action(), Protocol.Json); }
        catch (Exception e) when (e is UnauthorizedAccessException or FirebaseAuthException)
        { return Results.Json(new ApiError("El acceso no es válido o ha caducado. Inicia sesión de nuevo."), Protocol.Json, statusCode: 401); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { return Results.Json(new ApiError(e.Message), Protocol.Json, statusCode: 409); }
    }
}
