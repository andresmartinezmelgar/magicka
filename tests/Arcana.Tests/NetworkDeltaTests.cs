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
            if (Protocol.Serialize(rebuilt.Snapshot) != Protocol.Serialize(latest.Snapshot))
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
    }
}
