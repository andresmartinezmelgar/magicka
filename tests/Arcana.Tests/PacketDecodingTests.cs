using Arcana.Core;
using System.Text;
using System.Text.Json;

internal static class PacketDecodingTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Discriminante de red ignora tipos anidados y conserva semántica JSON", () =>
        {
            foreach (var (json, expected) in new (string, string?)[]
            {
                ("{\"type\":\"delta\",\"changes\":{\"type\":\"snapshot\"}}", "delta"),
                ("{\"changes\":{\"type\":\"snapshot\"},\"type\":\"delta\"}", "delta"),
                ("{\"type\":\"snapshot\",\"type\":\"delta\"}", "delta"),
                ("{\"type\":null}", null),
                ("{\"other\":[{\"type\":\"delta\"}]}", null),
                ("{\"ty\\u0070e\":\"pong\"}", "pong")
            })
                if (SnapshotReceiver.ReadMessageType(Encoding.UTF8.GetBytes(json)) != expected)
                    throw new Exception("Se clasificó incorrectamente el mensaje.");
        });
        test("Lectura del tipo rechaza JSON incompleto, tipos inválidos y profundidad excesiva", () =>
        {
            foreach (string json in new[] { "", "[]", "null", "{", "{\"type\":1}",
                "{\"type\":\"delta\",\"changes\":[}", "{\"type\":\"delta\"} {}",
                "{\"type\":\"delta\",\"x\":" + new string('[', 33) + "0" + new string(']', 33) + "}" })
            {
                bool rejected = false;
                try { SnapshotReceiver.ReadMessageType(Encoding.UTF8.GetBytes(json)); }
                catch (JsonException) { rejected = true; }
                if (!rejected) throw new Exception("Se aceptó un mensaje de red inválido.");
            }
        });
        test("Recepción conserva deltas independientes, reintento y confirmación de bases", () =>
        {
            var world = new GameWorld(new WorldOptions());
            var player = world.AddPlayer("Red", 0)!;
            var welcome = new ServerMessage { Type = "welcome", Snapshot = world.Snapshot(), ReadyPlayerIds = [player.Id] };
            var sender = new SnapshotSender(welcome);
            var receiver = new SnapshotReceiver(welcome);
            long confirmed = 0;
            for (int frame = 1; frame <= 85; frame++)
            {
                player.Position = new V2(frame * 0.01f, frame * -0.02f);
                world.Step();
                var current = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot(), ReadyPlayerIds = [player.Id] };
                var payload = sender.Encode(current, out var keyframe);
                // Se pierde la primera base y algunas tramas; la siguiente no depende de ellas.
                if (keyframe != null && frame > 40)
                {
                    var message = Protocol.DeserializeFrame(keyframe, keyframe[0] == 0xA1)!;
                    receiver.Accept(message); sender.Acknowledge(message.BaselineId); confirmed = message.BaselineId;
                }
                if (frame % 7 == 0) continue;
                var raw = payload[0] == 0xA1 ? Protocol.DecompressSnapshotPayload(payload) : payload;
                if (SnapshotReceiver.ReadMessageType(raw) != "delta") throw new Exception("Tipo de delta incorrecto.");
                var decoded = receiver.Decode(raw);
                if (Protocol.Serialize(decoded?.Snapshot) != Protocol.Serialize(current.Snapshot) ||
                    !decoded!.ReadyPlayerIds!.SequenceEqual(current.ReadyPlayerIds))
                    throw new Exception("El delta pierde estado, precisión o preparación tras un descarte.");
            }
            if (confirmed < 2) throw new Exception("No se ejercitó el cambio de base.");
            var unavailable = Encoding.UTF8.GetBytes("{\"type\":\"delta\",\"baseId\":999,\"baseTick\":0,\"changes\":{}}");
            if (receiver.Decode(unavailable) != null) throw new Exception("Se aceptó una base desconocida.");
        });
        test("Descompresión de recepción conserva límite de memoria", () =>
        {
            var oversized = Protocol.CompressSnapshotPayload(new byte[Protocol.MaxSnapshotBytes + 1]);
            bool rejected = false;
            try { Protocol.DecompressSnapshotPayload(oversized); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected) throw new Exception("Se superó el límite de descompresión.");
        });
    }
}
