using Arcana.AccountServer;
using Arcana.Core;
using Arcana.Server;

var options = AccountOptions.FromEnvironment();
if (!options.Enabled)
    throw new InvalidOperationException("Configura FIREBASE_PROJECT_ID, FIREBASE_API_KEY, FIREBASE_AUTH_DOMAIN y ARCANA_PUBLIC_URL.");
int port = int.TryParse(Environment.GetEnvironmentVariable("ACCOUNT_PORT"), out int configured)
    && configured is >= 1024 and <= 65535 ? configured : 8080;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.ConfigureArcanaHttp();
builder.Services.AddArcanaAccounts(options);
builder.Services.AddArcanaProtection();
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/internal/statistics" && context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        limit.MaxRequestBodySize = MatchStatisticsReport.MaxBytes;
    await next(context);
});
app.UseArcanaProtection();
app.MapAccounts();
app.MapItemShop();
app.MapAccountDeletion();
app.MapPublicDashboard();

app.MapInternalAccounts();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "accounts", version = BuildInfo.Version }));
app.MapGet("/", () => Results.Text("Arcana Arena · servicio independiente de cuentas"));
await app.RunAsync();
