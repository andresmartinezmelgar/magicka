using Arcana.Core;

internal static class NetworkDeltaTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Delta desde saludo permite pérdida de fotogramas", () =>
        {
            var world = new GameWorld(new WorldOptions());
            world.AddPlayer("Mago", 0);
            var welcome = new ServerMessage { Type = "welcome", PlayerId = 1, Snapshot = world.Snapshot(),
                RequiresReady = true, ReadyPlayerIds = [1] };
            world.Step();
            world.Step();
            var latest = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot(),
                ReadyPlayerIds = [1] };
            var rebuilt = SnapshotDelta.Decode(SnapshotDelta.Encode(latest, welcome), welcome);
            if (rebuilt.Type != "snapshot" || Protocol.Serialize(rebuilt.Snapshot) != Protocol.Serialize(latest.Snapshot))
                throw new Exception("Delta no independiente.");
        });
        test("Delta conserva estado completo tras movimiento", () =>
        {
            var world = new GameWorld(new WorldOptions());
            var player = world.AddPlayer("Prueba", 0)!;
            var baseMessage = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            player.Position = new V2(4, 5);
            world.Step();
            var current = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            var encoded = SnapshotDelta.Encode(current, baseMessage);
            var decoded = SnapshotDelta.Decode(encoded, baseMessage);
            if (Protocol.Serialize(decoded.Snapshot) != Protocol.Serialize(current.Snapshot))
                throw new Exception("La reconstrucción del snapshot no coincide.");
        });
        test("Delta admite transición a nulo y cambio de longitud", () =>
        {
            var world = new GameWorld(new WorldOptions());
            world.AddPlayer("Prueba", 0);
            var before = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot(),
                AccountNotice = "anterior", ReadyPlayerIds = [1] };
            var after = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot(),
                AccountNotice = null, ReadyPlayerIds = [1, 2] };
            var decoded = SnapshotDelta.Decode(SnapshotDelta.Encode(after, before), before);
            if (decoded.AccountNotice != null || decoded.ReadyPlayerIds is not { Length: 2 })
                throw new Exception("Transición a nulo o longitud distinta no reconstruida.");
        });
        test("Input binario sin destino no transmite coordenadas", () =>
        {
            var input = new ClientMessage { Type = "input", Sequence = 1, MoveTo = false,
                TargetX = 500, TargetY = 600, AimX = 1, AimY = 2 };
            var bytes = Protocol.SerializeBinaryAction(input);
            var decoded = Protocol.DeserializeBinaryAction(bytes);
            if (decoded.MoveTo || decoded.TargetX != 0 || decoded.TargetY != 0 ||
                decoded.AimX != 1 || decoded.AimY != 2)
                throw new Exception("La entrada sin destino no es compacta.");
        });
        test("Acciones binarias conservan campos y secuencia", () =>
        {
            var original = new ClientMessage { Type = "input", Sequence = 123, MoveX = 0.5f,
                MoveY = -0.25f, MoveTo = true, TargetX = 3, TargetY = 4, AimX = 8, AimY = 9 };
            var decoded = Protocol.DeserializeBinaryAction(Protocol.SerializeBinaryAction(original));
            if (decoded.Sequence != original.Sequence || Math.Abs(decoded.MoveX - original.MoveX) > 0.00004f ||
                decoded.TargetY != original.TargetY || decoded.AimY != original.AimY)
                throw new Exception("La entrada binaria ha perdido datos.");
        });
        test("Snapshot comprimido conserva el contenido", () =>
        {
            var world = new GameWorld(new WorldOptions());
            world.AddPlayer("Prueba", 0);
            var message = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            var encoded = Protocol.SerializeSnapshot(message);
            var decoded = Protocol.DeserializeFrame(encoded, encoded.Length > 0 && encoded[0] == 0xA1);
            if (Protocol.Serialize(decoded?.Snapshot) != Protocol.Serialize(message.Snapshot))
                throw new Exception("La compresión altera el snapshot.");
        });
        test("Compresión conserva estadísticas y evita expandir datos", () =>
        {
            var world = new GameWorld(new WorldOptions()); world.AddPlayer("Prueba", 0);
            for (int i = 0; i < 60; i++) world.Step();
            var message = new ServerMessage { Type = "statistics", Statistics = world.MatchStatistics() };
            var bytes = Protocol.SerializeSnapshot(message);
            var decoded = Protocol.DeserializeFrame(bytes, bytes[0] == 0xA1);
            if (decoded?.Type != "statistics" || decoded.Statistics?.ToJson() != message.Statistics.ToJson())
                throw new Exception("El informe comprimido cambia o pierde métricas.");
            var noise = new byte[8192]; new Random(271).NextBytes(noise);
            if (!ReferenceEquals(noise, Protocol.CompressSnapshotPayload(noise)))
                throw new Exception("Se comprime una trama aunque aumente su tamaño.");
            var repeated = Enumerable.Repeat((byte)42, 65536).ToArray();
            var compressed = Protocol.CompressSnapshotPayload(repeated);
            if (compressed[0] != 0xA1 || !Protocol.DecompressSnapshotPayload(compressed).SequenceEqual(repeated))
                throw new Exception("La nueva compresión no es compatible o pierde datos.");
        });
        test("Deltas PvP menores de 1 KiB se comprimen sin perder precisión", () =>
        {
            var world = new GameWorld(new WorldOptions());
            var a = world.AddPlayer("Azul", 0)!;
            var b = world.AddPlayer("Rojo", 1)!;
            var baseline = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            a.Position = new V2(1.234567f, -2.345678f); b.Position = new V2(-3.456789f, 4.567891f);
            a.Health -= 1.25f; b.Health -= 2.5f;
            world.Step();
            var current = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            var raw = SnapshotDelta.Encode(current, baseline);
            if (raw.Length < 256 || raw.Length >= 1024)
                throw new Exception($"El escenario ya no representa un delta pequeño: {raw.Length} bytes.");
            var encoded = Protocol.CompressSnapshotPayload(raw);
            if (encoded[0] != 0xA1 || encoded.Length >= raw.Length - 16)
                throw new Exception("El delta pequeño sigue enviándose sin comprimir.");
            var restored = SnapshotDelta.Decode(Protocol.DecompressSnapshotPayload(encoded), baseline);
            if (Protocol.Serialize(restored.Snapshot) != Protocol.Serialize(current.Snapshot))
                throw new Exception("Cambió el estado de combate al comprimir el delta pequeño.");
        });
        test("Umbral de compresión conserva mensajes mínimos y ruido", () =>
        {
            foreach (int size in new[] { 255, 256, 1023, 1024 })
            {
                var raw = Enumerable.Repeat((byte)'x', size).ToArray();
                var encoded = Protocol.CompressSnapshotPayload(raw);
                if (size < Protocol.MinSnapshotCompressionBytes)
                {
                    if (!ReferenceEquals(raw, encoded)) throw new Exception("Se comprime por debajo del umbral.");
                }
                else if (encoded[0] != 0xA1 || !Protocol.DecompressSnapshotPayload(encoded).SequenceEqual(raw))
                    throw new Exception("El límite de compresión pierde datos.");
            }
            var noise = new byte[512]; new Random(985).NextBytes(noise);
            if (!ReferenceEquals(noise, Protocol.CompressSnapshotPayload(noise)))
                throw new Exception("La compresión expande un mensaje pequeño incomprimible.");
        });
        test("Controles de sala y estadísticas omiten los campos del saludo", () =>
        {
            foreach (string type in new[] { "statistics", "lobby-ready", "lobby-start", "lobby-team", "defense_restart" })
            {
                var action = new ClientMessage { Type = type, Sequence = 123, Team = 1, AccountToken = "private-token",
                    Password = "private-password", RoomPassword = "private-room-password" };
                var bytes = Protocol.SerializeBinaryAction(action);
                var decoded = Protocol.Deserialize<ClientMessage>(bytes)!;
                if (decoded.Type != type || decoded.Sequence != 123 || type == "lobby-team" && decoded.Team != 1)
                    throw new Exception("El control compacto pierde su intención o secuencia.");
                if (decoded.AccountToken.Length != 0 || decoded.Password.Length != 0 || decoded.RoomPassword.Length != 0 || bytes.Length >= 100)
                    throw new Exception("Un control conserva campos ajenos al mensaje.");
            }
            var hello = Protocol.Deserialize<ClientMessage>(Protocol.SerializeAction(new ClientMessage
                { Type = "hello", AccountToken = "identity", RoomPassword = "room", Name = "Mago" }))!;
            if (hello.AccountToken != "identity" || hello.RoomPassword != "room" || hello.Name != "Mago")
                throw new Exception("La compactación altera el saludo.");
        });
        test("Pong compacto conserva el identificador de latencia", () =>
        {
            const long id = 9007199254740993;
            var raw = Protocol.SerializePong(id);
            var message = Protocol.DeserializeFrame(raw, false);
            if (message?.Type != "pong" || message.PingId != id || raw.Length >= 64)
                throw new Exception("La respuesta de ping no es compacta o pierde precisión.");
        });
    }
}
