using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineLobbyTests
{
    public static async Task Run(string server, Func<string, Func<Task>, Task> AsyncTest)
    {
        string secret = Environment.GetEnvironmentVariable("TEST_PASSWORD") ?? "";
        await AsyncTest("Serie PvP: sin sala automática, elección de equipos y comienzo manual", async () =>
        {
            var empty = await RoomBrowser.ListAsync(server);
            Check(empty.Rooms.Length == 0, "El servidor anuncia una sala que el usuario no creó.");
            var room = await RoomBrowser.CreateAsync(server, new()
            {
                Name = "Serie de prueba", ServerPassword = secret, Mode = GameMode.Warfare,
                Map = ArenaMapId.Classic, MaxPlayers = 4, Bots = 0, BestOf = 5, Tickets = 5
            });
            Check(room.BestOf == 5, "La sala no conserva su formato al mejor de cinco.");
            await using var a = new ArenaConnection();
            await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Anfitrión", -1, secret, roomId: room.Id);
            await b.ConnectAsync(server, "Invitado", -1, secret, roomId: room.Id);
            await WaitFor(() => a.Latest?.Players.Length == 2 && b.Latest?.Players.Length == 2,
                "No se replicaron los integrantes de la sala de espera.");
            Check(a.Latest!.WaitingForPlayers && b.Latest!.WaitingForPlayers && a.Latest.BestOf == 5,
                "La sala empezó la partida sin confirmar equipos.");
            long tick = a.Latest.Tick;
            await Task.Delay(450);
            Check(a.Latest!.Tick == tick, "Se está simulando una ronda antes de pulsar Comenzar.");
            a.Send(new() { Type = "lobby-start" });
            await Task.Delay(300);
            Check(a.Latest!.WaitingForPlayers, "Se inició la partida sin jugadores preparados.");
            a.Send(new() { Type = "lobby-team", Team = 1 });
            await WaitFor(() => a.Latest!.Players.Single(p => p.Id == a.PlayerId).Team == 1,
                "No se aplicó la selección de equipo en el servidor.");
            a.Send(new() { Type = "lobby-team", Team = 0 });
            await WaitFor(() => a.Latest!.Players.Single(p => p.Id == a.PlayerId).Team == 0,
                "No se pudo recuperar el equipo azul.");
            a.Send(new() { Type = "lobby-ready" });
            b.Send(new() { Type = "lobby-ready" });
            await WaitFor(() => a.Latest!.LobbyReadyPlayerIds?.Length == 2,
                "No aparecen los dos jugadores preparados.");
            Check(a.Latest!.WaitingForPlayers, "La partida comenzó antes de que el anfitrión autorizara el inicio.");
            a.Send(new() { Type = "lobby-start" });
            await WaitFor(() => a.Latest is { WaitingForPlayers: false, Tick: > 0 },
                "El anfitrión no pudo comenzar la partida.");
            Check(a.Latest!.SeriesWins is { Length: 2 } && a.Latest.SeriesWins[0] == 0 &&
                a.Latest.SeriesWins[1] == 0, "La serie empezó con rondas ganadas.");
            var activeRooms = await RoomBrowser.ListAsync(server);
            Check(activeRooms.Rooms.Single(r => r.Id == room.Id).InProgress,
                "La sala no anuncia que la serie está en curso.");
            await using var lateJoin = new ArenaConnection();
            bool refused = false;
            try { await lateJoin.ConnectAsync(server, "Tardío", -1, secret, roomId: room.Id); }
            catch (InvalidOperationException e) when (e.Message.Contains("en curso", StringComparison.OrdinalIgnoreCase))
            { refused = true; }
            Check(refused, "Se permitió entrar en una serie de cinco ya comenzada.");
            // Si el rival abandona, se descarta la serie parcial: el anfitrión
            // regresa a preparación y un sustituto puede entrar de forma normal.
            await b.DisposeAsync();
            await WaitFor(() => a.Latest is { WaitingForPlayers: true } waiting &&
                waiting.RewardStatus.Contains("canceló", StringComparison.OrdinalIgnoreCase),
                "El servidor no devolvió a la sala de preparación al desconectarse el rival.");
            Check(a.Latest!.SeriesWins is [0, 0], "Se conservaron victorias de una serie cancelada.");
            await using var substitute = new ArenaConnection();
            await substitute.ConnectAsync(server, "Sustituto", -1, secret, roomId: room.Id);
            await WaitFor(() => a.Latest!.Players.Length == 2 && substitute.Latest!.Players.Length == 2,
                "El sustituto no pudo unirse a la sala recuperada.");
            Check(a.Latest!.WaitingForPlayers, "La nueva serie empezó automáticamente al entrar un sustituto.");
            a.Send(new() { Type = "lobby-ready" });
            substitute.Send(new() { Type = "lobby-ready" });
            await WaitFor(() => a.Latest!.LobbyReadyPlayerIds?.Length == 2,
                "No se prepararon ambos jugadores tras la desconexión.");
            a.Send(new() { Type = "lobby-start" });
            await WaitFor(() => a.Latest is { WaitingForPlayers: false },
                "El anfitrión no pudo iniciar la serie recuperada.");
        });
    }
}
