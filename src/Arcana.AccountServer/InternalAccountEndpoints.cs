using Arcana.Core;
using Arcana.Server;

namespace Arcana.AccountServer;

/// <summary>Private routes used by the authoritative game server through its shared service key.</summary>
internal static class InternalAccountEndpoints
{
    public static void MapInternalAccounts(this WebApplication app)
    {
        // Solo el servidor de juego, mediante una clave compartida, puede consultar sesiones,
        // perfiles de jugadores o entregar recompensas. No exponer esta clave al cliente.
        bool Authorized(HttpContext context)
        {
            string configured = Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY") ?? "";
            string supplied = context.Request.Headers["X-Arcana-Service-Key"].ToString();
            return configured.Length >= 32 && supplied.Length == configured.Length &&
                System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(configured));
        }
        app.MapPost("/internal/session", async (HttpContext context, AccountService accounts) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            string auth = context.Request.Headers.Authorization.ToString();
            var identity = auth.StartsWith("Bearer ", StringComparison.Ordinal)
                ? await accounts.AuthenticateGame(auth[7..], context.RequestAborted) : null;
            return identity == null ? Results.Unauthorized() : Results.Json(identity, Protocol.Json);
        });
        app.MapPost("/internal/profile", async (HttpContext context, AccountService accounts, AccountIdentity identity) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            return Results.Json(await accounts.Repository!.Profile(identity.Uid, identity.Name, context.RequestAborted), Protocol.Json);
        });
        app.MapPost("/internal/award", async (HttpContext context, AccountService accounts, RewardMatch match) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            await accounts.Repository!.Award(match, context.RequestAborted);
            return Results.Json(new ApiError(""), Protocol.Json);
        });

        app.MapPost("/internal/bestiary", async (HttpContext context, AccountService accounts, BestiaryProgress progress) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            if (!BestiaryCatalogue.Valid(progress)) return Results.BadRequest();
            await accounts.Repository!.SaveBestiary(progress, context.RequestAborted);
            return Results.Json(new ApiError(""), Protocol.Json);
        });

        app.MapPost("/internal/statistics", async (HttpContext context, AccountService accounts) =>
        {
            if (!Authorized(context)) return Results.Unauthorized();
            try
            {
                string json = await RoomBrowser.ReadBoundedAsync(new StreamContent(context.Request.Body), MatchStatisticsReport.MaxBytes, context.RequestAborted);
                await accounts.Repository!.SaveMatchStatistics(MatchStatisticsReport.Parse(json), context.RequestAborted);
                return Results.Json(new ApiError(""), Protocol.Json);
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException) { return Results.BadRequest(); }
        });
    }
}
