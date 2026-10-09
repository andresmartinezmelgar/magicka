using Arcana.Core;

internal static class LoadingTests
{
    private static void Check(bool condition, string reason)
    { if (!condition) throw new Exception(reason); }

    public static void Run(Action<string, Action> test)
    {
        test("Equipos: elección explícita y equilibrio solo en Automático", () =>
        {
            var world = new GameWorld(new() { Bots = 1 });
            Check(world.AddPlayer("Rojo", 1)!.Team == 1, "No se respetó el equipo rojo.");
            Check(world.AddPlayer("Otro rojo", 1)!.Team == 1, "Se cambió una elección explícita para equilibrar.");
            Check(world.AddPlayer("Automático")!.Team == 0, "Automático no eligió el equipo menos poblado.");
            Check(world.AddPlayer("Otro azul", 0)!.Team == 0, "No se respetó el equipo azul.");
        });
        test("Duelo: elegir el lado mueve al bot y mantiene dos equipos opuestos", () =>
        {
            foreach (int team in new[] { 0, 1 })
            {
                var world = new GameWorld(new() { Mode = GameMode.Duel, MaxPlayers = 2, Bots = 1 });
                var player = world.AddPlayer("Creador", team)!;
                var bot = world.Players.Values.Single(p => p.Bot);
                Check(player.Team == team && bot.Team == 1 - team && world.Walkable(bot.Position), "El duelo no respetó el lado elegido.");
                Check(Math.Sign(bot.Facing.X) == (bot.Team == 0 ? 1 : -1), "El bot conservó la orientación del equipo anterior.");
            }
            var humans = new GameWorld(new() { Mode = GameMode.Duel, MaxPlayers = 2 });
            humans.AddPlayer("Rojo", 1);
            Check(humans.AddPlayer("Otro rojo", 1) == null && humans.AddPlayer("Azul", 0)?.Team == 0,
                "Un duelo permitió dos humanos en el mismo equipo o rechazó al rival.");
        });
    }

    private static async Task WaitFor(Func<bool> ready, string reason)
    {
        for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); }
        throw new Exception(reason);
    }

    public static async Task RunNetwork(string address, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Carga en red: equipos elegidos, espera conjunta, cancelación y entrada tardía protegida", async () =>
        {
            var room = await RoomBrowser.CreateAsync(address, new() { Name = "Carga de recursos", Mode = GameMode.Warfare,
                MaxPlayers = 6, Bots = 1, ServerPassword = password });
            await using var first = new ArenaConnection(); await using var second = new ArenaConnection();
            await using var canceled = new ArenaConnection();
            await first.ConnectAsync(address, "Creador rojo", 1, password, roomId: room.Id, deferReady: true);
            await second.ConnectAsync(address, "Segundo rojo", 1, password, roomId: room.Id, deferReady: true);
            await canceled.ConnectAsync(address, "Carga cancelada", 0, password, roomId: room.Id, deferReady: true);
            await WaitFor(() => first.PreparingPlayers == 3, "No se publicaron todos los clientes que cargan.");
            Check(!first.Ready && !second.Ready && first.Latest!.Tick == 0 &&
                first.Latest.Players.Single(p => p.Id == first.PlayerId).Team == 1 &&
                second.Latest!.Players.Single(p => p.Id == second.PlayerId).Team == 1, "Se arrancó antes de cargar o se cambió el equipo.");
            var initial = first.Latest!.Players.Select(p => p.Position).ToArray();
            first.Send(new() { Type = "element", Element = Element.Fire });
            first.Send(new() { Type = "input", MoveX = 1, AimX = 1 });
            await Task.Delay(180);
            Check(first.Latest.Tick == 0 && first.Latest.Players.Select(p => p.Position).SequenceEqual(initial) &&
                first.Latest.Players.Single(p => p.Id == first.PlayerId).Selected.Length == 0, "La carga permitió mover bots o ejecutar acciones.");
            Check(first.CompleteLoading() && first.CompleteLoading(), "Confirmar la carga no es idempotente.");
            await WaitFor(() => first.Ready && first.PreparingPlayers == 2, "No llegó la confirmación del primer cliente.");
            Check(second.CompleteLoading(), "El segundo cliente no pudo confirmar.");
            await WaitFor(() => second.Ready && first.PreparingPlayers == 1, "No se mantuvo la espera por el tercer cliente.");
            await Task.Delay(180); Check(first.Latest.Tick == 0, "La sala empezó con un cliente pendiente.");
            await canceled.DisposeAsync();
            await WaitFor(() => first.PreparingPlayers == 0 && first.Latest.Tick > 0, "Cancelar la carga bloqueó la sala.");
            await using var late = new ArenaConnection();
            await late.ConnectAsync(address, "Entrada tardía", 0, password, roomId: room.Id, deferReady: true);
            long tick = late.Latest!.Tick; var position = late.Latest.Players.Single(p => p.Id == late.PlayerId).Position;
            late.Send(new() { Type = "element", Element = Element.Fire });
            late.Send(new() { Type = "input", MoveX = 1, AimX = 1 });
            await WaitFor(() => late.Latest!.Tick >= tick + 12, "Una entrada tardía pausó la partida iniciada.");
            var player = late.Latest!.Players.Single(p => p.Id == late.PlayerId);
            Check(!late.Ready && late.PreparingPlayers == 0 && player.Position == position &&
                player.Selected.Length == 0 && player.InvulnerableSeconds > 1 && player.Health == player.MaxHealth,
                "El jugador que carga recibió daño o pudo actuar.");
            Check(late.CompleteLoading(), "No se confirmó la entrada tardía.");
            await WaitFor(() => late.Ready, "Se perdió la confirmación de preparación.");
            late.Send(new() { Type = "element", Element = Element.Fire });
            await WaitFor(() => late.Latest!.Players.Single(p => p.Id == late.PlayerId).Selected.Contains(Element.Fire),
                "El jugador siguió bloqueado después de cargar.");
        });
    }
}
