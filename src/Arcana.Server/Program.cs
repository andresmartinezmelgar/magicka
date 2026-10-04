using Arcana.Core;
using Arcana.Server;

var minimum = Enum.TryParse<DiagnosticLevel>(Environment.GetEnvironmentVariable("ARCANA_LOG_LEVEL"), true, out var level)
    && Enum.IsDefined(level) ? level : DiagnosticLevel.Information;
await using var log = new DiagnosticLog(ServerSettings.LogDirectory, "server", minimum);
Diagnostics.Log = log;
using var processDiagnostics = new ProcessDiagnostics(log);
try
{
    var settings = ServerSettings.FromEnvironment();
    log.ProtectSecret(settings.Password);
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls($"http://0.0.0.0:{settings.Port}");
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = Protocol.MaxMessageBytes);
    builder.Services.AddSingleton(settings);
    builder.Services.AddSingleton<GameService>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>());
    builder.Services.AddHostedService<DiscoveryService>();
    builder.Services.AddHostedService<ParentLifetimeService>();
    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(options => { options.SingleLine = true; options.TimestampFormat = "HH:mm:ss "; });
    builder.Logging.AddProvider(new DiagnosticLoggerProvider(log));
    builder.Logging.SetMinimumLevel((LogLevel)(int)minimum);
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception e)
        {
            Diagnostics.Error("http.failed", "Fallo al atender una petición.", e,
                new Dictionary<string, object?> { ["request"] = context.TraceIdentifier, ["path"] = context.Request.Path.Value });
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new ApiError($"Error interno. Referencia: {context.TraceIdentifier}"), Protocol.Json);
        }
    });
    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
    app.MapGet("/health", (GameService game) => Results.Ok(new { status = "ok", version = BuildInfo.Version, tick = game.CurrentTick }));
    app.MapGet("/info", (GameService game) => Results.Json(game.Info(), Protocol.Json));
    app.MapGet("/rooms", (GameService game) => Results.Json(game.ListRooms(), Protocol.Json));
    app.MapPost("/rooms", async (HttpContext context, GameService game) =>
    {
        if (!context.Request.HasJsonContentType())
            return Results.Json(new ApiError("Envía los datos de la sala como application/json."), Protocol.Json, statusCode: 415);
        try
        {
            var request = await context.Request.ReadFromJsonAsync<CreateRoomRequest>(Protocol.Json, context.RequestAborted);
            var result = game.CreateRoom(request);
            if (result.Room == null) Diagnostics.Warn("room.rejected", result.Error,
                context: new Dictionary<string, object?> { ["request"] = context.TraceIdentifier, ["status"] = result.Status });
            return result.Room != null ? Results.Json(result.Room, Protocol.Json, statusCode: result.Status)
                : Results.Json(new ApiError(result.Error), Protocol.Json, statusCode: result.Status);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or BadHttpRequestException)
        {
            Diagnostics.Warn("room.invalid_json", "Solicitud de sala inválida o demasiado grande.", e,
                new Dictionary<string, object?> { ["request"] = context.TraceIdentifier });
            return Results.Json(new ApiError("Solicitud de sala inválida o demasiado grande."), Protocol.Json, statusCode: 400);
        }
    });
    app.Map("/play", async (HttpContext context, GameService game) => await game.HandleConnection(context));
    app.MapGet("/", () => $"Arcana Arena v{BuildInfo.Version}. Cliente: /play. Salas: /rooms. Estado: /health.");
    Diagnostics.Info("server.start", "Iniciando servidor.", new Dictionary<string, object?>
        { ["instance"] = settings.InstanceId, ["port"] = settings.Port, ["discoveryPort"] = settings.DiscoveryPort, ["logDirectory"] = log.DirectoryPath });
    await app.RunAsync();
    Diagnostics.Info("server.stop", "Servidor detenido.");
}
catch (Exception e)
{
    Diagnostics.Error("server.fatal", "El servidor no pudo arrancar o continuar.", e);
    Console.Error.WriteLine($"Arcana Arena: {e.GetType().Name}. Consulta los registros en {log.DirectoryPath}.");
    Environment.ExitCode = 1;
}
