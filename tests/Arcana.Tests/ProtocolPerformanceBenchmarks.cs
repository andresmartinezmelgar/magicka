using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class ProtocolPerformanceBenchmarks
{
    internal static void Run(string output)
    {
        var session = new PracticeSession("Medición ñ");
        for (int i = 0; i < 120; i++) session.Step();
        var message = new ServerMessage { Type = "snapshot", Snapshot = session.World.Snapshot() };
        var bytes = Protocol.SerializeUtf8(message);
        if (!bytes.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(Protocol.Serialize(message))))
            throw new InvalidDataException("Las rutas de serialización difieren.");
        object Measure(string name, Action action, int? payloadBytes = null)
        {
            for (int i = 0; i < 100; i++) action();
            var samples = new double[1000];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                long start = Stopwatch.GetTimestamp(); action();
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            long total = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Array.Sort(samples);
            var result = new { name, iterations = samples.Length, messageBytes = payloadBytes ?? bytes.Length,
                meanMs = samples.Average(), p95Ms = samples[949], allocatedBytesPerMessage = total / samples.Length };
            Console.WriteLine(JsonSerializer.Serialize(result)); return result;
        }
        var networkWorld = new GameWorld(new WorldOptions { MaxPlayers = 8 });
        for (int i = 0; i < 8; i++) networkWorld.AddPlayer("Red " + i, i % 2);
        var baseline = new ServerMessage { Type = "snapshot", Snapshot = networkWorld.Snapshot() };
        foreach (var player in networkWorld.Players.Values)
        {
            player.Position += new V2(0.123456f, -0.345678f);
            player.Health -= 1.25f;
        }
        networkWorld.Step();
        var changed = new ServerMessage { Type = "snapshot", Snapshot = networkWorld.Snapshot() };
        byte[] compressed = Protocol.SerializeSnapshot(changed);
        byte[] delta = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(changed, baseline));
        var receiver = new SnapshotReceiver(baseline);
        bool binaryDelta = delta[0] == 0xA1;
        if (!binaryDelta) throw new InvalidDataException("La comparación de recepción requiere un delta comprimido.");
        void DecodeLegacy()
        {
            // Ruta anterior: deserialización de la envoltura, segunda descompresión y dos árboles JSON.
            if (Protocol.DeserializeFrame(delta, binaryDelta)?.Type != "delta") throw new InvalidDataException();
            byte[] raw = binaryDelta ? Protocol.DecompressSnapshotPayload(delta) : delta.ToArray();
            var node = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException();
            if (node["baseId"]?.GetValue<long>() != 1) throw new InvalidDataException();
            _ = SnapshotDelta.Decode(raw, baseline);
        }
        void DecodeCurrent()
        {
            byte[] raw = binaryDelta ? Protocol.DecompressSnapshotPayload(delta) : delta;
            if (SnapshotReceiver.ReadMessageType(raw) != "delta") throw new InvalidDataException();
            _ = receiver.Decode(raw);
        }
        var rawDelta = binaryDelta ? Protocol.DecompressSnapshotPayload(delta) : delta;
        if (Protocol.Serialize(receiver.Decode(rawDelta)?.Snapshot) != Protocol.Serialize(changed.Snapshot))
            throw new InvalidDataException("La ruta de recepción cambia el estado.");
        Console.WriteLine(JsonSerializer.Serialize(new { measurement = "snapshot_payload", fullBytes = Protocol.SerializeUtf8(changed).Length,
            compressedBytes = compressed.Length, deltaBytes = delta.Length,
            chosenBytes = Math.Min(compressed.Length, delta.Length) }));
        var reports = new[]
        {
            Measure("encode_text_then_utf8", () => _ = Encoding.UTF8.GetBytes(Protocol.Serialize(message))),
            Measure("encode_utf8", () => _ = Protocol.SerializeUtf8(message)),
            Measure("decode_utf8_through_text", () => _ = Protocol.Deserialize<ServerMessage>(Encoding.UTF8.GetString(bytes))),
            Measure("decode_utf8", () => _ = Protocol.Deserialize<ServerMessage>(bytes)),
            Measure("validate_snapshot", () => SnapshotValidation.Check(message.Snapshot!)),
            Measure("decode_delta_legacy", DecodeLegacy, delta.Length),
            Measure("decode_delta_single_decompression", DecodeCurrent, delta.Length)
        };
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { schema = 1, version = BuildInfo.Version,
            scope = "local_json_serialization", transportExcluded = true, warmupIterations = 100, reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PROTOCOL_PROFILE_OK: " + path);
    }
}
