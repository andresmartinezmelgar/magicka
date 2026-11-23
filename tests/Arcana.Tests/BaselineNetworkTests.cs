using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineNetworkTests
{
    public static async Task Run(string server, int discoveryPort, bool skipDiscovery, Func<string, Func<Task>, Task> AsyncTest)
    {
        string password = Environment.GetEnvironmentVariable("TEST_PASSWORD") ?? "";
        await NetworkEndpointTests.RunNetwork(server, password, AsyncTest);
        await FrontlineTests.RunNetwork(server, password, AsyncTest);
        await DefenseTests.RunNetwork(server, password, AsyncTest);
        await UpgradeTests.RunNetwork(server, password, AsyncTest);
        await LoadingTests.RunNetwork(server, password, AsyncTest);
        await AsyncTest("Crear salas con opciones, enumerarlas y aislar estado aunque los IDs coincidan", async () =>
        {
            var roomA = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba aislamiento A", ServerPassword = password,
                Mode = GameMode.Arena, MaxPlayers = 4, FriendlyFire = false, ScoreLimit = 9, Tickets = 45 });
            var roomB = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba aislamiento B", ServerPassword = password,
                Mode = GameMode.Warfare, Map = ArenaMapId.Citadel, MaxPlayers = 3, Bots = 0 });
            var directory = await RoomBrowser.ListAsync(server);
            Check(directory.Rooms.Any(r => r.Id == roomA.Id && r.ScoreLimit == 9 && !r.FriendlyFire) &&
                directory.Rooms.Any(r => r.Id == roomB.Id && r.Bots == 0 && r.Map == ArenaMapId.Citadel), "No se publicaron las opciones de sala.");
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Solo A", -1, password, roomId: roomA.Id);
            await b.ConnectAsync(server, "Solo B", -1, password, roomId: roomB.Id);
            Check(a.PlayerId == b.PlayerId, "La prueba debe cubrir IDs locales iguales en dos salas.");
            a.Send(new() { Type = "element", Element = Element.Fire });
            await WaitFor(() => a.Latest!.Players.First(p => p.Id == a.PlayerId).Selected.Length == 1, "No llegó la intención a su sala.");
            Check(a.Latest!.Mode == GameMode.Arena && a.Latest.Players.Length == 1 && b.Latest!.Mode == GameMode.Warfare &&
                b.Latest.Players.All(p => p.Name != "Solo A" && p.Selected.Length == 0), "Se filtró estado entre salas.");
            Check(a.Room?.Id == roomA.Id && b.Room?.Id == roomB.Id, "El saludo no identifica la sala.");
            Check(a.Latest.Map == ArenaMapId.Classic && b.Latest!.Map == ArenaMapId.Citadel && b.Room!.Map == ArenaMapId.Citadel &&
                b.Latest.Points.Select(p => p.Position).SequenceEqual(ArenaMap.Get(ArenaMapId.Citadel).ControlPoints) &&
                b.Latest.HoldingTeam == -1 && b.Latest.HoldSeconds == 0, "La red mezcló los mapas o perdió sus reglas de control.");
        });
        await AsyncTest("Ascensión: sala, saludo y snapshots replican el tercer mapa", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Ascensión en red", ServerPassword = password,
                Mode = GameMode.Duel, Map = ArenaMapId.Ascension, MaxPlayers = 2, ScoreLimit = 10 });
            var directory = await RoomBrowser.ListAsync(server);
            Check(directory.Rooms.Any(r => r.Id == room.Id && r.Map == ArenaMapId.Ascension), "El directorio perdió el mapa.");
            await using var connection = new ArenaConnection();
            await connection.ConnectAsync(server, "Ascendente", 0, password, roomId: room.Id);
            Check(connection.Room?.Map == ArenaMapId.Ascension && connection.Latest!.Map == ArenaMapId.Ascension &&
                connection.Latest.Players.Single().MaxHealth == 1000 && connection.Latest.HoldingTeam == -1 &&
                connection.Latest.Points.Select(p => p.Position).SequenceEqual(ArenaMap.Get(ArenaMapId.Ascension).ControlPoints),
                "El saludo o la geometría no coinciden con la sala.");
            await WaitFor(() => connection.Latest!.Players.Single().InvulnerableSeconds <= 0, "No terminó la protección inicial.");
            for (int i = 0; i < 3; i++)
            {
                float health = connection.Latest!.Players.Single().Health;
                for (int element = 0; element < 3; element++) connection.Send(new() { Type = "element", Element = Element.Arcane });
                connection.Send(new() { Type = "self" });
                await WaitFor(() => connection.Latest!.Players.Single().Health < health, "No se replicó el autodaño.");
                if (!connection.Latest!.Players.Single().Alive) break;
                double readyAt = connection.Latest.Time + 0.5;
                await WaitFor(() => connection.Latest!.Time >= readyAt, "La simulación no completó el enfriamiento.");
            }
            await WaitFor(() => connection.Latest!.Players.Single().Deaths == 1, "No se replicó la muerte.");
            var dead = connection.Latest!.Players.Single();
            Check(!dead.Alive && dead.MaxHealth == 1000 && dead.Respawn > 2 && dead.Respawn <= 3, "El servidor no aplicó la primera espera de 3 s.");
            SnapshotValidation.Check(connection.Latest);
        });
        await AsyncTest("Coliseo en red: dos clientes comparten las posiciones aleatorias y la recogida", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Objetos del Coliseo", ServerPassword = password,
                Mode = GameMode.Duel, Map = ArenaMapId.Ascension, MaxPlayers = 2, ScoreLimit = 10 });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Recolector del Coliseo", 0, password, roomId: room.Id);
            await b.ConnectAsync(server, "Observador del Coliseo", 1, password, roomId: room.Id);
            Check(a.Latest!.Pickups is { Length: 14 } && a.Latest.Pickups.SequenceEqual(b.Latest!.Pickups!), "Los clientes sortearon posiciones diferentes.");
            var start = a.Latest.Players.Single(p => p.Id == a.PlayerId).Position;
            var item = a.Latest.Pickups!.OrderBy(i => V2.Distance(i.Position, start)).First();
            for (int i = 0; i < 1200 && !a.Latest.Players.Single(p => p.Id == a.PlayerId).Buffs!.Any(); i++)
            {
                a.Send(new() { Type = "input", MoveTo = true, TargetX = item.Position.X, TargetY = item.Position.Y });
                await Task.Delay(20);
            }
            a.Send(new() { Type = "input" });
            await WaitFor(() => b.Latest!.Players.Single(p => p.Id == a.PlayerId).Buffs!.Any(buff => buff.Kind == item.Kind), "El rival no recibió la mejora recogida.");
            Check(!b.Latest!.Pickups!.Single(i => i.Id == item.Id).Active && b.Latest.Pickups!.Single(i => i.Id == item.Id).Position == item.Position,
                "La recogida no se replicó en la posición del servidor."); SnapshotValidation.Check(a.Latest); SnapshotValidation.Check(b.Latest);
        });
        await AsyncTest("Reliquias en red: objetos, recogida autoritativa, acumulaciones y limpieza al morir", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Reliquias en red", ServerPassword = password,
                Mode = GameMode.Duel, Map = ArenaMapId.Relics, MaxPlayers = 2, ScoreLimit = 10 });
            await using var connection = new ArenaConnection();
            await connection.ConnectAsync(server, "Recolector en red", 0, password, roomId: room.Id);
            Check(connection.Room?.Map == ArenaMapId.Relics && connection.Latest!.Pickups is { Length: 14 }, "El saludo perdió el cuarto mapa u objetos.");
            var item = connection.Latest!.Pickups!.First(i => i.Kind == BuffKind.Power);
            await WaitFor(() =>
            {
                connection.Send(new() { Type = "input", MoveTo = true, TargetX = item.Position.X, TargetY = item.Position.Y,
                    AimX = 0, AimY = 0 });
                return connection.Latest!.Players.Single().Buffs!.Any(b => b.Kind == BuffKind.Power && b.Stacks == 1);
            }, "El servidor no recogió el objeto al llegar.");
            connection.Send(new() { Type = "input" });
            Check(!connection.Latest!.Pickups!.Single(i => i.Id == item.Id).Active, "La red dejó disponible el objeto recogido.");
            for (int i = 0; i < 3; i++)
            {
                for (int e = 0; e < 3; e++) connection.Send(new() { Type = "element", Element = Element.Arcane });
                connection.Send(new() { Type = "self" }); await Task.Delay(550);
            }
            await WaitFor(() => !connection.Latest!.Players.Single().Alive, "No se replicó la muerte del recolector.");
            Check(connection.Latest!.Players.Single().Buffs!.Length == 0 && connection.Latest.Players.Single().MaxHealth == 1000,
                "La red conservó las mejoras tras morir.");
            SnapshotValidation.Check(connection.Latest);
        });
        await AsyncTest("Contraseña de sala, aforo y sala inexistente se validan", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Duelo privado", ServerPassword = password,
                Password = "sala-clave-é", Mode = GameMode.Duel, MaxPlayers = 2, Bots = 1 });
            await using var wrong = new ArenaConnection(); bool rejected = false;
            try { await wrong.ConnectAsync(server, "Sin clave", -1, password, roomId: room.Id, roomPassword: "incorrecta"); }
            catch (InvalidOperationException e) { rejected = e.Message.Contains("sala"); }
            Check(rejected, "Se admitió una contraseña de sala incorrecta.");
            if (password.Length > 0)
            {
                // La clave del SERVIDOR se comprueba de forma independiente de la SALA.
                await using var wrongServer = new ArenaConnection();
                bool rejectedServer = false;
                try { await wrongServer.ConnectAsync(server, "Clave servidor errónea", -1,
                    "servidor-incorrecto", roomId: room.Id, roomPassword: "sala-clave-é"); }
                catch (InvalidOperationException error)
                {
                    rejectedServer = error.Message.Contains("Contraseña del servidor incorrecta.", StringComparison.Ordinal);
                }
                Check(rejectedServer, "El servidor confundió las dos contraseñas.");
            }
            await using var a = new ArenaConnection(); await a.ConnectAsync(server, "Con clave", -1, password, roomId: room.Id, roomPassword: "sala-clave-é");
            Check(a.Latest!.Players.Length == 2, "No se añadió el bot del duelo.");
            await using var full = new ArenaConnection(); rejected = false;
            try { await full.ConnectAsync(server, "Tercero", -1, password, roomId: room.Id, roomPassword: "sala-clave-é"); }
            catch (InvalidOperationException e) { rejected = e.Message.Contains("llena"); }
            Check(rejected, "Se excedió el aforo.");
            await using var missing = new ArenaConnection(); rejected = false;
            try { await missing.ConnectAsync(server, "Perdido", -1, password, roomId: "no-existe"); }
            catch (InvalidOperationException e) { rejected = e.Message.Contains("existe"); }
            Check(rejected, "Se admitió una sala inexistente.");
        });
        await AsyncTest("Crear sala rechaza opciones inválidas y no expone contraseñas", async () =>
        {
            bool rejected = false;
            try { await RoomBrowser.CreateAsync(server, new() { Name = "Inválida", Mode = GameMode.Duel, MaxPlayers = 8, ServerPassword = password }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Se admitió un duelo de ocho jugadores.");
            rejected = false;
            try { await RoomBrowser.CreateAsync(server, new() { Name = "Mapa inválido", Map = (ArenaMapId)99, ServerPassword = password }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Se admitió una sala con un mapa inexistente.");
            using var http = new HttpClient();
            string directory = await http.GetStringAsync(RoomBrowser.Endpoint(server, "rooms"));
            Check(!directory.Contains("sala-clave") && (password.Length == 0 || !directory.Contains(password)), "Se expuso una contraseña en el directorio.");
            if (password.Length > 0)
            {
                rejected = false;
                try { await RoomBrowser.CreateAsync(server, new() { Name = "No autorizada", ServerPassword = "incorrecta" }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Se creó una sala sin la contraseña del servidor.");
            }
        });
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
            Check(b.Latest!.Players.First(p => p.Id == a.PlayerId).DisplayElements.SequenceEqual(new[] { Element.Fire, Element.Fire, Element.Fire }),
                "El rival perdió los elementos al empezar el canal.");
            a.Send(new() { Type = "release" });
            await WaitFor(() => !b.Latest!.Players.First(p => p.Id == a.PlayerId).Casting, "No se replicó la interrupción.");
            Check(b.Latest!.Players.First(p => p.Id == a.PlayerId).DisplayElements.Length == 0, "El rival conservó casillas de un canal terminado.");
        });


        await AsyncTest("La red replica capacidad y duración reales del escudo a ambos jugadores", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba de escudo", ServerPassword = password, Mode = GameMode.Arena, MaxPlayers = 2 });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Escudo A", -1, password, roomId: room.Id); await b.ConnectAsync(server, "Escudo B", -1, password, roomId: room.Id);
            a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "self" });
            await WaitFor(() => b.Latest!.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.ShieldMax == 420, "El rival no recibió el escudo normal.");
            var shield = b.Latest!.Players.First(p => p.Id == a.PlayerId);
            Check(shield.Shield == 420 && shield.ShieldSeconds is > 0 and <= 12, "El escudo normal no se transmitió lleno o perdió su duración.");
            await Task.Delay(550); a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Earth }); a.Send(new() { Type = "self" });
            await WaitFor(() => a.Latest!.Players.First(p => p.Id == a.PlayerId).ShieldMax == 650 && b.Latest!.Players.First(p => p.Id == a.PlayerId).ShieldMax == 650, "No se replicó el cambio de capacidad a tierra.");
            shield = b.Latest!.Players.First(p => p.Id == a.PlayerId);
            Check(shield.Shield == 650 && shield.Ward.Contains(Element.Earth) && shield.ShieldSeconds > 0, "La guarda de tierra se transmitió con datos incorrectos.");
        });
        await AsyncTest("Reutilizar una intención conserva dos comandos; cierre concurrente es idempotente", async () =>
        {
            var connection = new ArenaConnection();
            await connection.ConnectAsync(server, "Cierre concurrente", -1, password);
            var message = new ClientMessage { Type = "element", Element = Element.Fire };
            connection.Send(message); connection.Send(message); message.Element = Element.Life;
            await WaitFor(() => connection.Latest!.Players.First(p => p.Id == connection.PlayerId).Selected.SequenceEqual(new[] { Element.Fire, Element.Fire }), "Se conservaron referencias de mensajes mutables.");
            var sender = Task.Run(async () => { for (int i = 0; i < 20; i++) { connection.Send(new() { Type = "input" }); await Task.Delay(2); } });
            await Task.WhenAll(sender, connection.DisposeAsync().AsTask(), connection.DisposeAsync().AsTask());
            Check(!connection.Connected && !connection.Send(new() { Type = "release" }), "Una conexión cerrada aceptó mensajes.");
        });
        await AsyncTest("La red replica tipos y objetivos y aplica la debilidad en el servidor", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Afinidades", ServerPassword = password, Mode = GameMode.Arena,
                MaxPlayers = 2, ScoreLimit = 7, Tickets = 33 });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Piromante", 0, password, roomId: room.Id, wizardType: WizardType.Pyromancer);
            await b.ConnectAsync(server, "Tempestario", 1, password, roomId: room.Id, wizardType: WizardType.Stormcaller);
            await WaitFor(() => b.Latest!.Players.Any(p => p.Id == a.PlayerId && p.WizardType == WizardType.Pyromancer), "El rival no recibió el tipo.");
            Check(a.Latest!.ScoreLimit == 7 && a.Latest.StartingTickets == 33 &&
                b.Latest!.Players.First(p => p.Id == b.PlayerId).WizardType == WizardType.Stormcaller, "Se perdieron los objetivos o el tipo.");
            await Task.Delay(1300);
            a.Send(new() { Type = "element", Element = Element.Cold }); a.Send(new() { Type = "self" });
            await WaitFor(() => b.Latest!.Players.First(p => p.Id == a.PlayerId).Health < 1000, "No se aplicó el daño.");
            Check(Math.Abs(b.Latest!.Players.First(p => p.Id == a.PlayerId).Health - 970) < 0.01f, "El servidor no aplicó el +25 % de debilidad al frío.");
        });
        await AsyncTest("La red replica escudo regenerativo y minas de vida armadas", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Efectos", ServerPassword = password, Mode = GameMode.Warfare,
                MaxPlayers = 2, FriendlyFire = false });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync(server, "Vida", 0, password, roomId: room.Id); await b.ConnectAsync(server, "Observador", 1, password, roomId: room.Id);
            a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Life }); a.Send(new() { Type = "self" });
            await WaitFor(() => b.Latest!.Players.Any(p => p.Id == a.PlayerId && p.RegenerationRate == 38), "No se recibió la regeneración.");
            await Task.Delay(500);
            var player = a.Latest!.Players.First(p => p.Id == a.PlayerId);
            a.Send(new() { Type = "input", AimX = player.Position.X + 5, AimY = player.Position.Y });
            await Task.Delay(80);
            a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Life }); a.Send(new() { Type = "cast" });
            await WaitFor(() => b.Latest!.Fields.Any(f => f.Kind == "healing_mine" && f.Armed), "No se recibió la mina armada.");
            var mine = b.Latest!.Fields.Single(f => f.Kind == "healing_mine");
            Check(mine.TriggerRadius == 0.85f && mine.Radius == 2.4f, "Se perdió el radio de activación o de curación.");
            SnapshotValidation.Check(b.Latest!);
        });
        await AsyncTest("El servidor rechaza un tipo de mago desconocido en el saludo", async () =>
        {
            using var socket = new System.Net.WebSockets.ClientWebSocket(); using var timeout = new CancellationTokenSource(5000);
            await socket.ConnectAsync(ArenaConnection.Address(server), timeout.Token);
            byte[] hello = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(new ClientMessage { Type = "hello", Name = "Inválido", Password = password, WizardType = (WizardType)999 }));
            await socket.SendAsync(hello.AsMemory(), System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
            var buffer = new byte[4096]; var reply = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            var message = Protocol.Deserialize<ServerMessage>(System.Text.Encoding.UTF8.GetString(buffer, 0, reply.Count));
            Check(message is { Type: "error" } && message.Error.Contains("mago"), "El servidor aceptó un tipo de mago desconocido.");
        });
        await AsyncTest("Cerrar durante el saludo cancela el arranque de la conexión", async () =>
        {
            var connection = new ArenaConnection();
            var connect = connection.ConnectAsync(server, "Arranque cancelado", -1, password);
            await connection.DisposeAsync();
            try { await connect; } catch (Exception e) when (e is OperationCanceledException or System.Net.WebSockets.WebSocketException or IOException or ObjectDisposedException) { }
            Check(!connection.Connected, "Se activó una conexión después de cerrarla.");
        });
        await AsyncTest("Salas vacías con bots se pausan; el primer jugador inicia la simulación", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Reposo con bots", MaxPlayers = 8, Bots = 7, ServerPassword = password });
            await Task.Delay(500);
            await using var connection = new ArenaConnection(); await connection.ConnectAsync(server, "Despertar", -1, password, roomId: room.Id);
            Check(connection.Latest!.Tick < 15, "Los bots simularon una sala vacía.");
            long first = connection.Latest.Tick;
            await WaitFor(() => connection.Latest!.Tick > first + 5, "La simulación no se reanudó al entrar.");
        });
        await AsyncTest("API de salas rechaza formato no JSON y JSON malformado sin error 500", async () =>
        {
            using var http = new HttpClient();
            using var text = new StringContent("no es json");
            using var wrongType = await http.PostAsync(RoomBrowser.Endpoint(server, "rooms"), text);
            Check((int)wrongType.StatusCode == 415, "El Content-Type inválido no recibió 415.");
            using var malformed = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
            using var invalidJson = await http.PostAsync(RoomBrowser.Endpoint(server, "rooms"), malformed);
            Check((int)invalidJson.StatusCode == 400, "El JSON malformado no recibió 400.");
            Check((await http.GetAsync(RoomBrowser.Endpoint(server, "health"))).IsSuccessStatusCode, "El error de un cliente derribó el servidor.");
        });
        await AsyncTest("Más de 32 jugadores pueden entrar en salas con plazas y volver a conectar", async () =>
        {
            var rooms = new List<RoomInfo>();
            for (int i = 0; i < 3; i++) rooms.Add(await RoomBrowser.CreateAsync(server,
                new() { Name = $"Aforo compartido {i}", ServerPassword = password, MaxPlayers = 16, Bots = 0 }));
            var connections = new List<ArenaConnection>();
            try
            {
                for (int i = 0; i < 33; i++)
                {
                    var connection = new ArenaConnection(); connections.Add(connection);
                    await connection.ConnectAsync(server, $"Jugador {i}", -1, password, roomId: rooms[i / 16].Id);
                }
                var directory = await RoomBrowser.ListAsync(server);
                Check(connections.All(c => c.Connected) && directory.Rooms.Where(r => rooms.Any(room => room.Id == r.Id)).Sum(r => r.Players) == 33,
                    "El límite de saludos se aplicó a jugadores ya conectados.");
                foreach (var connection in connections) SnapshotValidation.Check(connection.Latest!);
            }
            finally { await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask())); }
            await WaitFor(() => connections.All(c => !c.Connected), "No se cerraron las conexiones de aforo.");
            await using var again = new ArenaConnection();
            await again.ConnectAsync(server, "Después del cierre", -1, password, roomId: rooms[2].Id);
            Check(again.Connected, "Cerrar los jugadores impidió un nuevo saludo.");
        });
        // La red puente de Docker sirve para IP directa. LAN por broadcast se comprueba con red host.
        if (!skipDiscovery) await AsyncTest("Descubrimiento UDP LAN devuelve el servidor real", async () =>
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
}
