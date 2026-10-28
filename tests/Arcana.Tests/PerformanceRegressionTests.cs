using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Arcana.Core;

internal static class PerformanceRegressionTests
{
    private static void Check(bool valid, string reason) { if (!valid) throw new Exception(reason); }
    internal static void Run(Action<string, Action> test)
    {
        test("JSON UTF-8 conserva exactamente el protocolo de todos los mapas", () =>
        {
            foreach (var id in Enum.GetValues<ArenaMapId>())
            {
                var world = id == ArenaMapId.Practice ? new PracticeSession("Mago ñ 🧙").World
                    : new GameWorld(new() { Map = id, Mode = id == ArenaMapId.Bastion ? GameMode.Defense : GameMode.Warfare, Bots = id == ArenaMapId.Bastion ? 3 : 7, MaxPlayers = id == ArenaMapId.Bastion ? 4 : 8,
                        SpawnSeed = 271, PickupSeed = 271, EnvironmentSeed = 271 });
                if (id != ArenaMapId.Practice) world.AddPlayer("Mago ñ 🧙");
                for (int i = 0; i < 120; i++) world.Step();
                var message = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
                var bytes = Protocol.SerializeUtf8(message);
                Check(bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(Protocol.Serialize(message))), "Cambió el JSON de " + id);
                var copy = Protocol.Deserialize<ServerMessage>(bytes)!;
                SnapshotValidation.Check(copy.Snapshot!);
                Check(Protocol.Serialize(copy) == Protocol.Serialize(message), "Cambió el estado al leer UTF-8 de " + id);
            }
        });
        test("Las copias de snapshot aíslan elementos, eventos, enfriamientos y mejoras", () =>
        {
            var session = new PracticeSession(); var world = session.World;
            session.Step();
            var first = world.Snapshot(); var second = world.Snapshot();
            string expected = Protocol.Serialize(second);
            foreach (var p in first.Players)
            {
                Array.Fill(p.Cooldowns, 999); Array.Fill(p.Ward, Element.Lightning);
                Array.Fill(p.Selected, Element.Earth); Array.Fill(p.CastingElements ?? [], Element.Water);
                if (p.Buffs is { Length: > 0 }) p.Buffs[0] = new(BuffKind.Power, 999);
            }
            foreach (var p in first.Projectiles) Array.Fill(p.Elements, Element.Life);
            foreach (var w in first.Walls) Array.Fill(w.Elements, Element.Life);
            foreach (var f in first.Fields) Array.Fill(f.Elements, Element.Life);
            foreach (var b in first.Beams) Array.Fill(b.Elements, Element.Life);
            foreach (var e in first.Events) Array.Fill(e.Elements, Element.Life);
            Array.Fill(first.Scores, 999);
            Check(Protocol.Serialize(second) == expected && Protocol.Serialize(world.Snapshot()) == expected,
                "Una matriz mutable se compartió entre snapshots o con el mundo.");
        });
        test("El filtro de colisiones conserva contactos, tangencias y segmentos cortos", () =>
        {
            var random = new Random(927);
            float Number(float radius) => ((float)random.NextDouble() * 2 - 1) * radius;
            for (int i = 0; i < 20000; i++)
            {
                var from = new V2(Number(240), Number(50));
                var to = from + new V2(Number(i % 3 == 0 ? 200 : 2), Number(4));
                var a = new V2(Number(240), Number(50)); var b = a + new V2(Number(6), Number(6));
                float radius = i % 5 == 0 ? 0 : (float)random.NextDouble() * 2;
                Check(Geometry.SegmentCircle(from, to, a, radius) == Circle(from, to, a, radius), "Cambió una colisión circular.");
                Check(Geometry.SegmentCapsule(from, to, a, b, radius) == Capsule(from, to, a, b, radius), "Cambió una colisión de pared.");
            }
            foreach (float radius in new[] { 0f, 0.48f, 1f })
            {
                Check(Geometry.SegmentCircle(new(-2, radius), new(2, radius), V2.Zero, radius) == Circle(new(-2, radius), new(2, radius), V2.Zero, radius), "Se descartó una tangencia.");
                Check(Geometry.SegmentCapsule(V2.Zero, V2.Zero, V2.Zero, V2.Zero, radius) == 0, "Se descartó un contacto inicial.");
            }
        });
    }

    internal static async Task RunTransport(Func<string, Func<Task>, Task> test)
    {
        await test(
            "La recepción reutiliza buffers entre mensajes grandes, pequeños y fragmentados", async () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena, MaxPlayers = 16 });
            for (int i = 0; i < 16; i++) world.AddPlayer("Mago ñ " + i);
            var state = world.Snapshot();
            var welcome = new ServerMessage { Type = "welcome", PlayerId = 1, Snapshot = state,
                Server = new("Prueba", 27015, 27016, GameMode.Arena, 16, 16, Protocol.Version, false, GameVersion: BuildInfo.Version),
                Room = new("default", "Prueba", GameMode.Arena, 16, 16, 0, true, 15, 75, false) };
            var picker = new TcpListener(IPAddress.Loopback, 0);
            picker.Start(); int port = ((IPEndPoint)picker.LocalEndpoint).Port; picker.Stop();
            using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var smallReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Serve()
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var hello = new byte[4097];
                await socket.ReceiveAsync(hello.AsMemory(), timeout.Token);
                async Task Send(ServerMessage message, int chunk)
                {
                    // Incluye UTF-8 literal, con caracteres cortados entre fragmentos.
                    var bytes = Encoding.UTF8.GetBytes(Protocol.Serialize(message).Replace("\\u00F1", "ñ", StringComparison.Ordinal));
                    for (int at = 0; at < bytes.Length; at += chunk)
                        await socket.SendAsync(bytes.AsMemory(at, Math.Min(chunk, bytes.Length - at)),
                            WebSocketMessageType.Text, at + chunk >= bytes.Length, timeout.Token);
                }
                await Send(welcome, 83);
                var large = state with { Tick = 1, Time = 1 / 60.0, Events = Enumerable.Range(1, 160)
                    .Select(i => new GameEvent(i, "test", V2.Zero, [Element.Fire], new string('x', 128))).ToArray() };
                Check(Protocol.SerializeUtf8(large).Length > 8192, "El mensaje no obliga a ampliar el buffer.");
                await Send(new() { Type = "snapshot", Snapshot = large }, 67);
                await smallReady.Task.WaitAsync(timeout.Token);
                await Send(new() { Type = "snapshot", Snapshot = state with { Tick = 2, Time = 2 / 60.0,
                    Players = [state.Players[0]], Events = [] } }, 17);
                var closed = await socket.ReceiveAsync(hello.AsMemory(), timeout.Token);
                if (closed.MessageType == WebSocketMessageType.Close)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Salir", timeout.Token);
            }
            var serving = Serve();
            await using var client = new ArenaConnection();
            try
            {
                await client.ConnectAsync($"127.0.0.1:{port}", "Prueba", 0, "", timeout.Token);
                while (client.Latest?.Tick != 1) await Task.Delay(5, timeout.Token);
                Check(client.Latest.Events.Length == 160, "El mensaje grande llegó incompleto.");
                smallReady.TrySetResult();
                while (client.Latest?.Tick != 2) await Task.Delay(5, timeout.Token);
                Check(client.Connected && client.Latest.Players.Length == 1 && client.Latest.Events.Length == 0,
                    "El mensaje pequeño conservó bytes del anterior.");
                await client.DisposeAsync(); await serving;
            }
            finally { timeout.Cancel(); listener.Stop(); }
        });
        await test("La salida voluntaria espera el cierre confirmado y admite cierres concurrentes", async () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena, MaxPlayers = 2 });
            world.AddPlayer("Salida");
            var welcome = new ServerMessage { Type = "welcome", PlayerId = 1, Snapshot = world.Snapshot(),
                Server = new("Prueba", 27015, 27016, GameMode.Arena, 1, 2, Protocol.Version, false, GameVersion: BuildInfo.Version),
                Room = new("default", "Prueba", GameMode.Arena, 1, 2, 0, true, 15, 75, false) };
            var picker = new TcpListener(IPAddress.Loopback, 0);
            picker.Start(); int port = ((IPEndPoint)picker.LocalEndpoint).Port; picker.Stop();
            using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var closeReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var confirmClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task Serve()
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                using var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                var buffer = new byte[8192];
                await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                await socket.SendAsync(Protocol.SerializeUtf8(welcome).AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
                var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                Check(result.MessageType == WebSocketMessageType.Close && socket.CloseStatus == WebSocketCloseStatus.NormalClosure,
                    "La salida no llegó como cierre normal.");
                closeReceived.SetResult();
                await confirmClose.Task.WaitAsync(timeout.Token);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Salida confirmada", timeout.Token);
            }
            var serving = Serve();
            await using var client = new ArenaConnection(); int notifications = 0;
            client.Disconnected += _ => Interlocked.Increment(ref notifications);
            try
            {
                await client.ConnectAsync($"127.0.0.1:{port}", "Salida", 0, "", timeout.Token);
                var first = client.DisposeAsync().AsTask(); var second = client.DisposeAsync().AsTask();
                await closeReceived.Task.WaitAsync(timeout.Token);
                Check(!first.IsCompleted && !second.IsCompleted, "Se abortó el transporte antes de confirmar el cierre.");
                confirmClose.SetResult();
                await Task.WhenAll(first, second, serving).WaitAsync(timeout.Token);
                Check(notifications == 0 && !client.Connected, "La salida voluntaria notificó una pérdida de conexión.");
            }
            finally { confirmClose.TrySetResult(); timeout.Cancel(); listener.Stop(); }
        });
    }

    // Fórmulas de referencia anteriores al filtro de cajas: valida que este solo descarta candidatos lejanos.
    private static float? Circle(V2 from, V2 to, V2 center, float radius)
    {
        var d = to - from; var f = from - center;
        if (f.LengthSquared <= radius * radius) return 0;
        float a = d.LengthSquared; if (a < 0.000001f) return null;
        float b = 2 * V2.Dot(f, d), c = f.LengthSquared - radius * radius;
        float discriminant = b * b - 4 * a * c; if (discriminant < 0) return null;
        float t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
        return t >= 0 && t <= 1 ? t : null;
    }
    private static float? Capsule(V2 from, V2 to, V2 a, V2 b, float radius)
    {
        if (Geometry.DistanceToSegment(from, a, b) <= radius) return 0;
        float? best = Circle(from, to, a, radius);
        if (Circle(from, to, b, radius) is float end && (best == null || end < best)) best = end;
        var edge = b - a; if (edge.LengthSquared < 0.000001f) return best;
        var normal = edge.Normalized.Perpendicular;
        float start = V2.Dot(from - a, normal), speed = V2.Dot(to - from, normal);
        if (MathF.Abs(speed) < 0.000001f) return best;
        for (int side = -1; side <= 1; side += 2)
        {
            float t = (side * radius - start) / speed;
            if (t < 0 || t > 1 || best != null && t >= best) continue;
            float along = V2.Dot(V2.Lerp(from, to, t) - a, edge) / edge.LengthSquared;
            if (along >= 0 && along <= 1) best = t;
        }
        return best;
    }
}
