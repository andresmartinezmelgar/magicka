using Arcana.Server;

var settings = ServerSettings.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Arcana.Core.Protocol.MaxMessageBytes);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>());
builder.Services.AddHostedService<DiscoveryService>();
builder.Services.AddHostedService<ParentLifetimeService>();
builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.MapGet("/health", (GameService game) => Results.Ok(new { status = "ok", version = "0.2.0", tick = game.CurrentTick }));
app.MapGet("/info", (GameService game) => Results.Json(game.Info(), Arcana.Core.Protocol.Json));
app.MapGet("/rooms", (GameService game) => Results.Json(game.ListRooms(), Arcana.Core.Protocol.Json));
app.MapPost("/rooms", async (HttpContext context, GameService game) =>
{
    try
    {
        var request = await context.Request.ReadFromJsonAsync<Arcana.Core.CreateRoomRequest>(Arcana.Core.Protocol.Json, context.RequestAborted);
        var result = game.CreateRoom(request);
        return result.Room != null ? Results.Json(result.Room, Arcana.Core.Protocol.Json, statusCode: result.Status)
            : Results.Json(new Arcana.Core.ApiError(result.Error), Arcana.Core.Protocol.Json, statusCode: result.Status);
    }
    catch (Exception e) when (e is System.Text.Json.JsonException or BadHttpRequestException)
    { return Results.Json(new Arcana.Core.ApiError("Solicitud de sala inválida o demasiado grande."), Arcana.Core.Protocol.Json, statusCode: 400); }
});
app.Map("/play", async (HttpContext context, GameService game) => await game.HandleConnection(context));
app.MapGet("/", () => "Arcana Arena v0.2.0. Cliente: /play. Salas: /rooms. Estado: /health.");
await app.RunAsync();
