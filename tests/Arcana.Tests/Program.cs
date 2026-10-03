using Arcana.Core;

int passed = 0, failed = 0;
void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
void Test(string name, Action action)
{
    if (args.Contains("--only-network")) return;
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
async Task AsyncTest(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
(GameWorld World, Wizard A, Wizard B) Setup(GameMode mode = GameMode.Arena, bool friendly = true)
{
    var world = new GameWorld(new() { Mode = mode, FriendlyFire = friendly });
    var a = world.AddPlayer("Azul", 0)!; var b = world.AddPlayer("Rojo", 1)!;
    a.Position = new(-2, 0); b.Position = new(2, 0); a.Invulnerable = b.Invulnerable = 0;
    return (world, a, b);
}
void Send(GameWorld w, Wizard p, string type, Element element = Element.Water, V2? aim = null, int slot = 0)
{
    var at = aim ?? new V2(3, 0);
    w.ApplyMessage(p.Id, new() { Type = type, Sequence = p.LastSequence + 1, Element = element, Slot = slot, AimX = at.X, AimY = at.Y });
}
void Run(GameWorld w, Wizard p, int ticks)
{
    for (int i = 0; i < ticks; i++) { Send(w, p, "input"); w.Step(); }
}
void Select(GameWorld w, Wizard p, params Element[] elements)
{ foreach (var element in elements) Send(w, p, "element", element); }

Test("Límite de tres elementos y cancelación de incompatibles", () =>
{
    var buffer = new ElementBuffer();
    buffer.Add(Element.Fire); buffer.Add(Element.Fire); buffer.Add(Element.Fire);
    Check(!buffer.Add(Element.Earth) && buffer.Elements.Count == 3, "Se admitió un cuarto elemento.");
    buffer.Add(Element.Cold);
    Check(buffer.Elements.Count == 2 && buffer.Elements.All(e => e == Element.Fire), "Frío no anuló un fuego.");
    buffer.Clear(); buffer.Add(Element.Water); buffer.Add(Element.Lightning);
    Check(buffer.Elements.Count == 0, "Agua y rayo no se anularon.");
    buffer.Add(Element.Shield); buffer.Add(Element.Shield);
    Check(buffer.Elements.Count == 0, "Dos escudos no se anularon.");
});
Test("Vapor, hielo y prioridad de forma del hechizo", () =>
{
    Check(new SpellRecipe([Element.Water, Element.Fire]).Steam, "No se resolvió vapor.");
    Check(new SpellRecipe([Element.Water, Element.Cold]).Shape == SpellShape.Shards, "No se resolvió hielo.");
    Check(new SpellRecipe([Element.Earth, Element.Fire, Element.Arcane]).Shape == SpellShape.Rock, "Tierra perdió prioridad.");
    Check(new SpellRecipe([Element.Shield, Element.Fire]).Shape == SpellShape.Barrier, "Escudo perdió prioridad.");
});
Test("Toda selección de hasta tres teclas resuelve y serializa", () =>
{
    foreach (var a in Enum.GetValues<Element>())
    foreach (var b in Enum.GetValues<Element>())
    foreach (var c in Enum.GetValues<Element>())
    {
        var buffer = new ElementBuffer(); buffer.Add(a); buffer.Add(b); buffer.Add(c);
        var recipe = new SpellRecipe(buffer.Elements);
        Check(Enum.IsDefined(recipe.Shape) && recipe.Elements.Length <= 3, "Combinación sin resolver.");
    }
});
Test("Snapshot JSON sin recursión y copia independiente del estado", () =>
{
    var (w, a, _) = Setup();
    var snapshot = w.Snapshot();
    var json = Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot });
    var copy = Protocol.Deserialize<ServerMessage>(json)!.Snapshot!;
    a.Health = 400; a.Position = new(10, 10);
    Check(copy.Players[0].Health == 1000 && snapshot.Players[0].Position.X == -2, "La copia se modificó con el mundo.");
    Check(!json.Contains("normalized"), "Se serializaron propiedades calculadas del vector.");
});
Test("El servidor rechaza NaN y secuencias repetidas", () =>
{
    var (w, a, _) = Setup();
    Check(!w.ApplyMessage(a.Id, new() { Type = "input", Sequence = 1, AimX = float.NaN }), "Se aceptó una coordenada no finita.");
    Check(w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "No se aceptó una intención válida.");
    Check(!w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "Se repitió una intención.");
});
Test("El rayo arcano aplica daño; al soltar deja de aplicarlo", () =>
{
    var (w, a, b) = Setup();
    Send(w, a, "input"); w.Step(); Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane);
    Send(w, a, "cast"); Run(w, a, 30);
    Check(b.Health < 850 && b.Health > 700, "Daño continuo inesperado.");
    Send(w, a, "release"); float health = b.Health; Run(w, a, 20);
    Check(Math.Abs(health - b.Health) < 0.01, "El hechizo siguió tras soltarlo.");
});
Test("Agua y frío congelan; fuego propio seca al mago mojado", () =>
{
    var (w, a, b) = Setup();
    Select(w, a, Element.Water); Send(w, a, "cast"); Run(w, a, 3); Send(w, a, "release");
    Check(b.Wet > 0, "El agua no mojó al objetivo.");
    Select(w, a, Element.Cold, Element.Cold, Element.Cold); Send(w, a, "cast"); Run(w, a, 45);
    Check(b.Frozen > 0, "El objetivo mojado no se congeló.");
    Send(w, a, "release"); a.Wet = 5; a.Burning = 0; float hp = a.Health;
    Select(w, a, Element.Fire); Send(w, a, "self");
    Check(a.Wet == 0 && a.Health == hp, "Secarse provocó daño o no eliminó mojado.");
});
Test("Curación propia, escudo y guarda elemental", () =>
{
    var (w, a, _) = Setup(); a.Health = 500;
    Select(w, a, Element.Life, Element.Life); Send(w, a, "self");
    Check(a.Health > 700, "No se curó al mago."); Run(w, a, 30);
    Select(w, a, Element.Shield, Element.Fire); Send(w, a, "self");
    Check(a.Shield > 0 && a.Ward.Contains(Element.Fire), "No se creó la guarda.");
});
Test("La roca cargada se lanza al soltar y colisiona con el enemigo", () =>
{
    var (w, a, b) = Setup(); Send(w, a, "input"); w.Step();
    Select(w, a, Element.Earth, Element.Earth, Element.Earth); Send(w, a, "cast"); Run(w, a, 35);
    Check(w.Snapshot().Projectiles.Length == 0 && a.ChargeTime > 0.5f, "Se lanzó antes de soltar.");
    Send(w, a, "release"); Run(w, a, 20);
    Check(b.Health < 650, "La roca atravesó al objetivo sin aplicar daño.");
});
Test("Obstáculos bloquean movimiento y rayos", () =>
{
    var (w, a, b) = Setup(); a.Position = new(-16, 0); b.Position = new(-10, 0);
    Send(w, a, "input", aim: b.Position); w.Step();
    Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 60; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Check(b.Health == 1000, "El rayo atravesó una columna.");
    Check(!w.Walkable(new(-13, 0)), "Se puede caminar dentro de la columna.");
});
Test("Se aplica la configuración de fuego amigo", () =>
{
    var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = false });
    var a = world.AddPlayer("A", 0)!; world.AddPlayer("Enemigo", 1); var ally = world.AddPlayer("Aliado", 0)!;
    a.Position = new(-2, 0); ally.Position = new(2, 0); a.Invulnerable = ally.Invulnerable = 0;
    Send(world, a, "input"); world.Step(); Select(world, a, Element.Arcane, Element.Arcane, Element.Arcane);
    Send(world, a, "cast"); Run(world, a, 30);
    Check(ally.Health == 1000, "Se dañó al aliado con fuego amigo desactivado.");
});
Test("Captura, desgaste de tickets, victoria y nueva partida", () =>
{
    var w = new GameWorld(new() { Mode = GameMode.Warfare, Tickets = 5 }); var a = w.AddPlayer("A", 0)!;
    a.Position = V2.Zero;
    for (int i = 0; i < 1000 && !w.MatchEnded; i++) { Send(w, a, "input"); w.Step(); }
    Check(w.MatchEnded && w.Winner == 0 && w.Scores[1] == 0, "La captura no terminó la partida.");
    for (int i = 0; i < 550; i++) w.Step();
    Check(!w.MatchEnded && w.Scores[0] == 5 && w.Scores[1] == 5, "No se reinició la partida.");
});
Test("Timeout de entrada interrumpe un canal abandonado", () =>
{
    var (w, a, _) = Setup(); Send(w, a, "input"); Select(w, a, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 70; i++) w.Step();
    Check(a.Channel == null, "El jugador abandonado siguió lanzando.");
});
Test("Partida con ocho bots se mantiene válida durante un minuto simulado", () =>
{
    var world = new GameWorld(new() { Bots = 8 });
    for (int i = 0; i < 3600; i++) world.Step();
    var snapshot = world.Snapshot();
    Check(snapshot.Players.All(p => p.Position.IsFinite && p.Health >= 0 && p.Health <= 1000 && ArenaMap.Inside(p.Position)), "Estado inválido tras combate.");
    Check(snapshot.Events.Any() && snapshot.Players.Any(p => p.Kills > 0 || p.Health < 1000), "Los bots no combatieron.");
});
Test("Direcciones IP, dominio, IPv6 y WSS", () =>
{
    Check(ArenaConnection.Address("192.168.1.10").Port == 27015, "Puerto por defecto incorrecto.");
    Check(ArenaConnection.Address("127.0.0.1:29015").AbsolutePath == "/play", "Ruta por defecto incorrecta.");
    Check(ArenaConnection.Address("::1").Port == 27015, "IPv6 inválida.");
    Check(ArenaConnection.Address("wss://juego.example/play").Scheme == "wss", "No se acepta WSS.");
});

string? server = args.FirstOrDefault(a => a.StartsWith("--server="))?[9..];
int discoveryPort = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--discovery-port="))?[17..], out int port) ? port : Protocol.DiscoveryPort;
async Task WaitFor(Func<bool> ready, string reason)
{
    for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); }
    throw new Exception(reason);
}
if (server != null)
{
    string password = Environment.GetEnvironmentVariable("TEST_PASSWORD") ?? "";
    await AsyncTest("Dos clientes reales, selección sincronizada y movimiento autoritativo", async () =>
    {
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Prueba A", 0, password); await b.ConnectAsync(server, "Prueba B", 1, password);
        Check(a.PlayerId != b.PlayerId, "Los clientes comparten ID.");
        await WaitFor(() => a.Latest?.Players.Length >= 2, "No se replicó el segundo jugador.");
        a.Send(new() { Type = "element", Element = Element.Fire }); a.Send(new() { Type = "element", Element = Element.Fire }); a.Send(new() { Type = "element", Element = Element.Fire });
        await WaitFor(() => b.Latest?.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.Selected.Length == 3, "El rival no recibió los elementos.");
        var start = a.Latest!.Players.First(p => p.Id == a.PlayerId).Position;
        a.Send(new() { Type = "input", MoveX = 999, AimX = 0, AimY = 0 });
        await Task.Delay(220);
        var end = a.Latest!.Players.First(p => p.Id == a.PlayerId).Position;
        Check(V2.Distance(start, end) > 0.2 && V2.Distance(start, end) < 2.3, "El cliente impuso una velocidad inválida.");
        a.Send(new() { Type = "input", AimX = 0, AimY = 0 });
        a.Send(new() { Type = "cast" });
        await WaitFor(() => b.Latest!.Players.First(p => p.Id == a.PlayerId).Casting, "No se replicó el lanzamiento.");
        a.Send(new() { Type = "release" });
        await WaitFor(() => !b.Latest!.Players.First(p => p.Id == a.PlayerId).Casting, "No se replicó la interrupción.");
    });
    // La red puente de Docker sirve para IP directa. LAN por broadcast se comprueba con red host.
    if (!args.Contains("--skip-discovery")) await AsyncTest("Descubrimiento UDP LAN devuelve el servidor real", async () =>
    {
        var found = await LanDiscovery.FindAsync(discoveryPort);
        Check(found.Length > 0 && found.Any(s => s.Info.Protocol == Protocol.Version), "No se encontró el servidor por UDP.");
    });
    await AsyncTest("Saludo con versión incompatible es rechazado", async () =>
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket(); using var timeout = new CancellationTokenSource(3000);
        await socket.ConnectAsync(ArenaConnection.Address(server), timeout.Token);
        var bytes = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(new ClientMessage { Type = "hello", Protocol = 999, Password = password }));
        await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096]; var response = await socket.ReceiveAsync(buffer, timeout.Token);
        var result = Protocol.Deserialize<ServerMessage>(System.Text.Encoding.UTF8.GetString(buffer, 0, response.Count));
        Check(result?.Type == "error", "Se admitió una versión incompatible.");
    });
    if (password.Length > 0) await AsyncTest("Contraseña incorrecta es rechazada", async () =>
    {
        await using var connection = new ArenaConnection(); bool rejected = false;
        try { await connection.ConnectAsync(server, "No autorizado", -1, "contraseña incorrecta"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Se admitió una contraseña incorrecta.");
    });
}
Console.WriteLine($"RESULTADO: {passed} correctas, {failed} fallidas.");
return failed == 0 ? 0 : 1;
