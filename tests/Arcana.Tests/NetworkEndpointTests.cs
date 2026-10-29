using Arcana.Core;

static class NetworkEndpointTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Red: ping compacto correlacionado sin datos de cuenta ni cambios del protocolo binario", () =>
        {
            const long id = long.MaxValue - 1;
            var bytes = Protocol.SerializeBinaryAction(new() { Type = "ping", Sequence = 7, PingId = id,
                AccountToken = "no-debe-viajar", Password = "no-debe-viajar" });
            var request = Protocol.Deserialize<ClientMessage>(bytes)!;
            if (request.Type != "ping" || request.PingId != id || request.Sequence != 7 ||
                System.Text.Encoding.UTF8.GetString(bytes).Contains("no-debe-viajar"))
                throw new Exception("El ping perdió su correlación o transportó credenciales.");
            var reply = Protocol.DeserializeFrame(Protocol.SerializeUtf8(new ServerMessage { Type = "pong", PingId = id }), false);
            if (reply?.Type != "pong" || reply.PingId != id)
                throw new Exception("La respuesta de ping no se puede identificar.");
            var input = Protocol.SerializeBinaryAction(new() { Type = "input", Sequence = 8 });
            if (input[0] != 0xB1 || input[1] != 0 || Protocol.DeserializeBinaryAction(input).Type != "input")
                throw new Exception("Añadir el ping desplazó las acciones binarias existentes.");
        });
        test("Red: normaliza conexiones LAN sin exponer claves en URL", () =>
        {
            foreach (var (input, expected) in new[]
            {
                ("127.0.0.1", "ws://127.0.0.1:27015/play"),
                (" 192.168.1.20:28000 ", "ws://192.168.1.20:28000/play"),
                ("wss://example.org", "wss://example.org/play"),
                ("ws://localhost:27015/play", "ws://localhost:27015/play")
            })
                if (ArenaConnection.Address(input).AbsoluteUri != expected)
                    throw new Exception("Dirección transformada erróneamente: " + input);
        });
        test("Red: sesión Google heredada aislada por servidor y Firebase sin filtración LAN", () =>
        {
            using var client = new AccountClient();
            // Una cuenta no autenticada nunca emite tokens.
            if (client.TokenFor("ws://127.0.0.1:27015/play").Length != 0 ||
                client.TokenForVerifiedServer("wss://example.org/play", "wss://example.org/play").Length != 0)
                throw new Exception("Se admitió un token de cuenta sin sesión válida.");
        });
        test("Red: una sala LAN solo usa WSS de la misma instancia", () =>
        {
            var official = new ServerInfo("Arena", 27014, 27016, GameMode.Duel, 0, 8,
                Protocol.Version, false, "instancia-1", BuildInfo.Version, true);
            var same = official with { Name = "Otro nombre" };
            var different = official with { InstanceId = "instancia-2" };
            if (!PublicAccountServer.SameInstance(official, same) ||
                PublicAccountServer.SameInstance(official, different) ||
                PublicAccountServer.SameInstance(official with { InstanceId = null }, same) ||
                PublicAccountServer.SameInstance(official, official with { Protocol = Protocol.Version + 1 }))
                throw new Exception("La comprobación de identidad del servidor WSS es insegura.");
        });
        test("Red: rechaza credenciales y parámetros de URL", () =>
        {
            foreach (string address in new[]
            {
                "", "https://example.org", "ws://user:pass@example.org/play",
                "ws://example.org/play?token=secret", "wss://example.org/play#secret",
                "ws://", "not a host"
            })
            {
                bool refused = false;
                try { ArenaConnection.Address(address); } catch (ArgumentException) { refused = true; }
                if (!refused) throw new Exception("Dirección insegura admitida: " + address);
            }
        });
    }

    public static async Task RunNetwork(string server, string password, Func<string, Func<Task>, Task> test)
    {
        await test("Red: ping real antes de cargar, límite de frecuencia y descarte al desconectar", async () =>
        {
            var room = await RoomBrowser.CreateAsync(server, new() { Name = "Medición de ping", ServerPassword = password,
                Mode = GameMode.Duel, MaxPlayers = 2, Bots = 0, BestOf = 3 });
            await using var client = new ArenaConnection();
            if (client.PingMilliseconds != null || client.RequestPing())
                throw new Exception("Se inventó latencia sin conexión.");
            await client.ConnectAsync(server, "Medidor", 0, password, roomId: room.Id, deferReady: true);
            if (client.PingMilliseconds != null || !client.RequestPing() || client.RequestPing())
                throw new Exception("El ping no comenzó o admitió consultas sin intervalo.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (client.PingMilliseconds == null) await Task.Delay(10, timeout.Token);
            if (client.PingMilliseconds is not (>= 0 and < 5000) || client.Ready || client.Latest!.Players.Single().Selected.Length != 0)
                throw new Exception("El ping no es válido o modifica la carga o el combate.");
            await client.DisposeAsync();
            if (client.PingMilliseconds != null || client.RequestPing())
                throw new Exception("Se conserva el ping de una conexión cerrada.");
        });
    }
}
