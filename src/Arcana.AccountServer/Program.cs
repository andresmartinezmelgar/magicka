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
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "accounts", version = BuildInfo.Version }));
app.MapGet("/", () => Results.Text("Arcana Arena · servicio independiente de cuentas"));
await app.RunAsync();
