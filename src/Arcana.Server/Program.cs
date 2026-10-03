using Arcana.Server;

var settings = ServerSettings.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>());
builder.Services.AddHostedService<DiscoveryService>();
builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.MapGet("/health", (GameService game) => Results.Ok(new { status = "ok", version = "0.1.0", tick = game.CurrentTick }));
app.MapGet("/info", (GameService game) => Results.Json(game.Info(), Arcana.Core.Protocol.Json));
app.Map("/play", async (HttpContext context, GameService game) => await game.HandleConnection(context));
app.MapGet("/", () => "Arcana Arena v0.1.0. Cliente de escritorio: /play. Estado: /health.");
await app.RunAsync();
