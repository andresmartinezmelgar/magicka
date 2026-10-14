using Arcana.Core;

static class NetworkEndpointTests
{
    public static void Run(Action<string, Action> test)
    {
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
}
