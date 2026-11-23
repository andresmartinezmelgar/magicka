using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    public static async Task RunNetwork(string address, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Defensa en red: un jugador, cooperación con bots, flujo y comandos válidos", async () =>
        {
            var room = await RoomBrowser.CreateAsync(address, new CreateRoomRequest { Name = "Defensa sola", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 1, DefenseFlow = DefenseFlow.Continuous, ServerPassword = password });
            Check(room.MaxPlayers == 1 && room.DefenseFlow == DefenseFlow.Continuous && !room.FriendlyFire, "Directorio de defensa incorrecto.");
            await using var solo = new ArenaConnection(); await solo.ConnectAsync(address, "Defensor", 1, password, roomId: room.Id);
            Check(solo.Latest!.Players.Single().Team == 0 && solo.Latest.Defense != null, "Conexión cooperativa rechazada.");
            solo.Send(new() { Type = "defense_supply" }); solo.Send(new() { Type = "defense_equip", ItemId = new string('a', 32) });
            await Task.Delay(300);
            Check(solo.Connected && solo.Latest!.Defense!.Defenders.Single().Items.Length == 0, "El servidor confió en una compra sin fondos.");
            solo.Send(new() { Type = "defense_shop", Slot = 1 });
            for (int i = 0; i < 40 && solo.Latest!.Defense?.Paused != true; i++) await Task.Delay(50);
            Check(solo.Latest!.Defense!.Paused, "El servidor no pausó la sala con un humano.");
            var paused = solo.Latest; await Task.Delay(400);
            Check(solo.Latest!.Tick == paused.Tick && !ReferenceEquals(solo.Latest, paused), "El servidor detuvo la conexión o avanzó la simulación pausada.");
            solo.Send(new() { Type = "defense_shop", Slot = 0 });
            for (int i = 0; i < 40 && solo.Latest!.Tick == paused.Tick; i++) await Task.Delay(50);
            Check(solo.Latest!.Tick > paused.Tick && !solo.Latest.Defense!.Paused, "El servidor no reanudó la sala.");
            var coop = await RoomBrowser.CreateAsync(address, new CreateRoomRequest { Name = "Defensa coop", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = 1, ServerPassword = password });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(address, "A", 0, password, roomId: coop.Id);
            await b.ConnectAsync(address, "B", 1, password, roomId: coop.Id);
            for (int i = 0; i < 50 && a.Latest!.Players.Length < 3; i++) await Task.Delay(50);
            Check(a.Latest!.Players.Length == 3 && a.Latest.Players.All(p => p.Team == 0), "Bots o humanos en equipo rival.");
            a.Send(new() { Type = "defense_performance", FramesPerSecond = 20 });
            b.Send(new() { Type = "defense_performance", FramesPerSecond = 60 });
            await Task.Delay(500);
            Check(a.Latest!.Defense!.PerformanceLimit > DefensePerformanceBudget.MinimumLimit &&
                b.Latest!.Defense!.PerformanceLimit > DefensePerformanceBudget.MinimumLimit,
                "Un cliente que declara pocos FPS alteró la dificultad online.");
            a.RequestMatchStatistics();
            for (int i = 0; i < 50 && a.LatestStatistics?.Humans != 2; i++) await Task.Delay(50);
            Check(a.LatestStatistics is { Network: true, Humans: 2, Bots: 1 }, "El cliente no recibió el informe cooperativo.");
            string report = (a.LatestStatistics! with { Status = MatchRecordStatus.Left }).ToJson();
            using var http = new HttpClient();
            using var accepted = await http.PostAsync(RoomBrowser.Endpoint(address, "analytics/matches"), new StringContent(report, System.Text.Encoding.UTF8, "application/json"));
            Check(accepted.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.ServiceUnavailable, "La ingesta aceptó un informe online suministrado por el cliente.");
            using var rejected = await http.PostAsync(RoomBrowser.Endpoint(address, "analytics/matches"), new StringContent(report[..^1] + ",\"email\":\"private@example.com\"}", System.Text.Encoding.UTF8, "application/json"));
            Check(rejected.StatusCode == System.Net.HttpStatusCode.BadRequest, "El relé aceptó identidad fuera del contrato.");
            SnapshotValidation.Check(a.Latest);
        });
    }
}
