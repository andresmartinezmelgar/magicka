using Arcana.Core;
using Arcana.Server;

var options = AccountOptions.FromEnvironment();
if (!options.Enabled)
    throw new InvalidOperationException("Configura FIREBASE_PROJECT_ID, FIREBASE_API_KEY, FIREBASE_AUTH_DOMAIN y ARCANA_PUBLIC_URL.");
int port = int.TryParse(Environment.GetEnvironmentVariable("ACCOUNT_PORT"), out int configured)
    && configured is >= 1024 and <= 65535 ? configured : 8080;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = Protocol.MaxMessageBytes);
builder.Services.ConfigureHttpJsonOptions(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = Protocol.Json.PropertyNamingPolicy;
    json.SerializerOptions.MaxDepth = Protocol.Json.MaxDepth;
    foreach (var converter in Protocol.Json.Converters) json.SerializerOptions.Converters.Add(converter);
});
builder.Services.AddSingleton(new AccountService(options));
var app = builder.Build();
app.MapAccounts();
app.MapItemShop();

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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "accounts", version = BuildInfo.Version }));
app.MapGet("/", () => Results.Text("Arcana Arena · servicio independiente de cuentas"));
await app.RunAsync();
