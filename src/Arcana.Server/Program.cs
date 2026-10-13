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
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = Protocol.Json.PropertyNamingPolicy;
        options.SerializerOptions.MaxDepth = Protocol.Json.MaxDepth;
        foreach (var converter in Protocol.Json.Converters) options.SerializerOptions.Converters.Add(converter);
    });
    // Firebase Admin verifica tokens y realiza las escrituras sin servidor HTTP intermedio.
    string firebaseProject = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID")?.Trim() ?? "";
    builder.Services.AddSingleton(new AccountService(firebaseProject.Length == 0
        ? new AccountOptions("", "", "", "")
        : new AccountOptions(firebaseProject, "", "", "")));
    builder.Services.AddSingleton<RewardService>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<RewardService>());
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
    // Mutaciones de cuenta: exclusivamente a través del servidor autoritativo.
    // El cliente presenta un Firebase ID token; nunca se acepta un UID enviado por el cliente.
    async Task<IResult> AccountMutation(HttpContext context, AccountService accounts,
        Func<AccountIdentity, CancellationToken, Task<InventoryResult>> operation)
    {
        if (!accounts.Enabled)
            return Results.Json(new ApiError("Firebase no está configurado en este servidor."), Protocol.Json, statusCode: 503);
        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            return Results.Unauthorized();
        var identity = await accounts.AuthenticateGame(authorization[7..], context.RequestAborted);
        if (identity == null) return Results.Unauthorized();
        try { return Results.Json(await operation(identity, context.RequestAborted), Protocol.Json); }
        catch (ArgumentException error) { return Results.Json(new ApiError(error.Message), Protocol.Json, statusCode: 400); }
        catch (InvalidOperationException error) { return Results.Json(new ApiError(error.Message), Protocol.Json, statusCode: 409); }
    }
    app.MapPost("/account/equip", (InventoryRequest request, HttpContext context, AccountService accounts) =>
        AccountMutation(context, accounts, async (identity, token) =>
        {
            await accounts.Repository!.Equip(identity.Uid, request, token);
            return new InventoryResult(await accounts.Repository.Profile(identity.Uid, identity.Name, token), "Equipamiento guardado.");
        }));
    app.MapPost("/account/sell", (InventoryRequest request, HttpContext context, AccountService accounts) =>
        AccountMutation(context, accounts, async (identity, token) =>
        {
            int gold = await accounts.Repository!.Sell(identity.Uid, request.ItemId, token);
            return new InventoryResult(await accounts.Repository.Profile(identity.Uid, identity.Name, token),
                gold > 0 ? $"Objeto vendido: +{gold} de oro." : "Esta venta ya estaba registrada.");
        }));
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
