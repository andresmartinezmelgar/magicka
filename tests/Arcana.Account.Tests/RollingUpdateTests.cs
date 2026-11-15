using Arcana.Core;
using Arcana.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Diagnostics;

internal static class RollingUpdateTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 150 && !ready(); i++) await Task.Delay(20);
        Check(ready(), "La transición no terminó dentro del plazo.");
    }
    private static int Port()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        try { return ((IPEndPoint)socket.LocalEndpoint).Port; } finally { socket.Stop(); }
    }
    private sealed class Instance(WebApplication app, GameService game, ServerSettings settings) : IAsyncDisposable
    {
        internal GameService Game => game;
        internal ServerSettings Settings => settings;
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
    private static async Task<Instance> Server(int port, int publicPort, string generation)
    {
        var settings = new ServerSettings { Port = port, Generation = generation, InstanceId = generation,
            AdvertisedPort = publicPort, ManagementToken = new('a', 64), DiscoveryEnabled = false,
            World = new() { Mode = GameMode.Warfare, Map = ArenaMapId.Classic, MaxPlayers = 4, Tickets = 1000 } };
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls($"http://127.0.0.1:{port}"); builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(o =>
        { foreach (var converter in Protocol.Json.Converters) o.SerializerOptions.Converters.Add(converter); });
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(new AccountService(new("", "", "", "")));
        builder.Services.AddSingleton<RewardService>(); builder.Services.AddHostedService(p => p.GetRequiredService<RewardService>());
        builder.Services.AddSingleton<MatchStatisticsService>(); builder.Services.AddHostedService(p => p.GetRequiredService<MatchStatisticsService>());
        builder.Services.AddSingleton<GameService>(); builder.Services.AddHostedService(p => p.GetRequiredService<GameService>());
        var app = builder.Build(); app.UseWebSockets(); app.MapServerLifecycle();
        app.MapGet("/health", () => Results.Json(new { status = "ok", version = BuildInfo.Version }, Protocol.Json));
        app.MapGet("/info", (GameService g) => Results.Json(g.Info(), Protocol.Json));
        app.MapGet("/rooms", (GameService g) => Results.Json(g.ListRooms(), Protocol.Json));
        app.MapPost("/rooms", (CreateRoomRequest request, GameService g) =>
        {
            var result = g.CreateRoom(request);
            return result.Room != null ? (IResult)Results.Json(result.Room, Protocol.Json, statusCode: result.Status)
                : Results.Json(new ApiError(result.Error), Protocol.Json, statusCode: result.Status);
        });
        app.Map("/play", (HttpContext context, GameService g) => g.HandleConnection(context));
        await app.StartAsync(); return new(app, app.Services.GetRequiredService<GameService>(), settings);
    }
    private static void Routes(string file, string phase, Instance active, params Instance[] all)
    {
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, Protocol.Serialize(new GatewayRoutes(phase, active.Settings.InstanceId, "0.29.66",
            all.Select(s => new GatewayNode(s.Settings.InstanceId, BuildInfo.Version, s.Settings.Port)).ToArray())));
        File.Move(temporary, file, true);
    }
    private static GameRoom Room(Instance server, string id)
    {
        var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(server.Game)!;
        return rooms.Values.Single(r => r.Id == id);
    }
    private static async Task Journal(string root)
    {
        string directory = Path.Combine(root, "statistics");
        var first = new MatchStatisticsJournal(directory); var recovered = new MatchStatisticsJournal(directory);
        var world = new GameWorld(new() { Mode = GameMode.Arena }); world.AddPlayer("Prueba", 0); world.Step();
        var earlier = world.MatchStatistics(MatchRecordStatus.Disconnected);
        first.Persist(earlier); world.Step(); var newer = world.MatchStatistics(MatchRecordStatus.Disconnected);
        recovered.Persist(newer); first.Acknowledge(earlier);
        Check(recovered.Pending().Single().Revision == newer.Revision, "Una confirmación anterior borra estadísticas nuevas.");
        string file = Directory.EnumerateFiles(directory, "*.json").Single(); File.WriteAllText(file + ".tmp", earlier.ToJson());
        Check(recovered.Pending().Single().Revision == newer.Revision, "Una escritura interrumpida antigua sustituye al resultado más reciente.");
        recovered.Acknowledge(newer); Check(first.Pending().Length == 0, "La cola temporal conserva un resultado ya confirmado.");
        File.WriteAllText(Path.Combine(directory, "damaged.json.tmp"), "{");
        first.Persist(newer); Check(recovered.Pending().Single().RunId == newer.RunId && Directory.EnumerateFiles(directory, "*.invalid-*").Any(),
            "Un archivo dañado bloquea la recuperación o se borra sin conservarlo.");
        first.Persist(newer with { GameVersion = "999.999.999" });
        Check(recovered.Pending().Length == 1 && Directory.EnumerateFiles(directory, "999.999.999-*.json").Any(),
            "Un proceso antiguo toca resultados de una versión que aún no entiende.");
        using (var lease = new SharedJournalLease(directory))
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") info.ArgumentList.Add(typeof(RollingUpdateTests).Assembly.Location);
            info.ArgumentList.Add("--journal-lock-probe=" + directory);
            using var child = Process.Start(info)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string output = await child.StandardOutput.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            Check(child.ExitCode == 0 && output.Contains("LOCK_BUSY"), "Dos procesos pueden modificar a la vez el diario compartido.");
        }
        using (var released = new SharedJournalLease(directory)) { }
    }
    internal static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "arcana-rolling-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string? oldOutbox = Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX");
        string? oldService = Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_URL");
        string? oldKey = Environment.GetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY");
        Environment.SetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_URL", null); Environment.SetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY", null);
        Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", Path.Combine(root, "rewards"));
        try
        {
            await Journal(root);
            int publicPort = Port(), firstPort = Port(), secondPort = Port();
            while (firstPort == publicPort) firstPort = Port();
            while (secondPort == publicPort || secondPort == firstPort) secondPort = Port();
            await using var first = await Server(firstPort, publicPort, Guid.NewGuid().ToString("N"));
            await using var second = await Server(secondPort, publicPort, Guid.NewGuid().ToString("N"));
            second.Game.SetAdmission(new(second.Settings.InstanceId, false));
            string state = Path.Combine(root, "routes.json"); Routes(state, "ready", first, first, second);
            await using var gateway = RollingGateway.Build(state, publicPort, discovery: false, bind: "127.0.0.1");
            await gateway.StartAsync();
            string address = $"ws://127.0.0.1:{publicPort}/play";
            await using var player = new ArenaConnection();
            await player.ConnectAsync(address, "Partida anterior", 0, "");
            await Until(() => first.Game.Lifecycle().ActiveGames == 1);
            string oldRoom = player.Room!.Id, resume = player.ResumeToken; int playerId = player.PlayerId;
            Check(RoomGeneration.Of(oldRoom) == first.Settings.InstanceId, "La sala principal no identifica su generación.");

            var waiting = first.Game.CreateRoom(new() { Name = "Serie sin empezar", Mode = GameMode.Duel,
                Map = ArenaMapId.Classic, MaxPlayers = 2, Bots = 0, BestOf = 5 });
            Check(waiting.Room != null, "No creó la sala de espera anterior.");
            await using var host = new ArenaConnection(); await using var guest = new ArenaConnection();
            await host.ConnectAsync(address, "Anfitrión", 0, "", roomId: waiting.Room!.Id);
            await guest.ConnectAsync(address, "Invitado", 1, "", roomId: waiting.Room.Id);
            await Until(() => host.Ready && guest.Ready);
            first.Game.SetAdmission(new(first.Settings.InstanceId, false, "0.29.66")); Routes(state, "starting", first, first, second);
            Check(first.Game.CreateRoom(new() { Name = "No permitida" }).Status == 503, "La generación antigua permite crear salas.");
            bool startupNotice = false;
            try { await RoomBrowser.ListAsync(address); } catch (ServerStartingException e) { startupNotice = e.Message.Contains("Iniciando servidor"); }
            Check(startupNotice, "La lista de salas no explica el arranque.");
            host.Send(new() { Type = "lobby-ready" }); guest.Send(new() { Type = "lobby-ready" }); host.Send(new() { Type = "lobby-start" });
            long tick = player.Latest!.Tick; await Until(() => player.Latest!.Tick > tick + 3);
            Check(player.Connected && !Room(first, waiting.Room.Id).Started, "El vaciado corta una partida o empieza una serie pendiente.");

            second.Game.SetAdmission(new(second.Settings.InstanceId, true)); Routes(state, "ready", second, first, second);
            await using var next = new ArenaConnection(); await next.ConnectAsync(address, "Partida nueva", 0, "");
            Check(RoomGeneration.Of(next.Room!.Id) == second.Settings.InstanceId && player.Connected,
                "Las partidas nuevas no pasan al segundo servidor o se corta la anterior.");
            player.ReconnectAfterStall();
            await Until(() => first.Game.Lifecycle().ReconnectingPlayers == 1);
            Check(first.Game.Lifecycle().ActiveGames == 1 && !first.Game.Lifecycle().SafeToStop, "Se retira una partida reservada para reconexión.");
            await using var restored = new ArenaConnection();
            await restored.ConnectAsync(address, "Partida anterior", 0, "", roomId: oldRoom, resumeToken: resume);
            Check(restored.PlayerId == playerId && restored.ResumeToken == resume && restored.Room!.Id == oldRoom,
                "La reconexión cambia de personaje, sesión o servidor.");
            await Until(() => restored.Ready);
            await using var intruder = new ArenaConnection(); bool refused = false;
            try { await intruder.ConnectAsync(address, "Nueva entrada antigua", 0, "", roomId: oldRoom); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "Se admiten entradas nuevas en una generación retirada.");

            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            Check((await http.GetAsync($"http://127.0.0.1:{firstPort}/internal/lifecycle")).StatusCode == HttpStatusCode.NotFound,
                "El control interno no exige autorización.");
            http.DefaultRequestHeaders.Add("X-Arcana-Management", new string('a', 64));
            Check((await http.GetAsync($"http://127.0.0.1:{publicPort}/internal/lifecycle")).StatusCode == HttpStatusCode.NotFound,
                "El proxy permite acceder al control interno.");
            Check((await http.GetAsync($"http://127.0.0.1:{firstPort}/internal/lifecycle")).IsSuccessStatusCode,
                "La administración local autorizada no responde.");

            var activeRoom = Room(first, oldRoom);
            var simulationGate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(first.Game)!;
            lock (simulationGate)
            {
            activeRoom.BestOf = 5; activeRoom.SeriesFinished = false;
            typeof(GameWorld).GetProperty(nameof(GameWorld.MatchEnded))!.SetValue(activeRoom.World, true);
            typeof(GameWorld).GetProperty(nameof(GameWorld.Winner))!.SetValue(activeRoom.World, 0);
            Check(!GameService.Completed(activeRoom), "Una ronda terminada se confunde con la serie completa.");
            activeRoom.SeriesFinished = true;
            Check(GameService.Completed(activeRoom), "La serie completa no permite retirar la generación.");
            }
            await Until(() => first.Game.Lifecycle().ActiveGames == 0);
            Check(!first.Game.Lifecycle().SafeToStop, "Cierra sin entregar el resultado final.");
            typeof(GameService).GetField("_emptySince", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(first.Game, Environment.TickCount64 - 11000);
            Check(first.Game.Lifecycle().SafeToStop && next.Connected, "No libera la versión terminada o afecta a la nueva.");
            await gateway.StopAsync();
            Console.WriteLine("ROLLING_UPDATE_OK: conexiones fijadas, arranque, admisión, series, reconexión y control privado.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", oldOutbox);
            Environment.SetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_URL", oldService); Environment.SetEnvironmentVariable("ARCANA_ACCOUNT_SERVICE_KEY", oldKey);
            Directory.Delete(root, true);
        }
    }
}
