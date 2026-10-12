using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
        object Measure(string name, Action action)
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
            var result = new { name, iterations = samples.Length, messageBytes = bytes.Length,
                meanMs = samples.Average(), p95Ms = samples[949], allocatedBytesPerMessage = total / samples.Length };
            Console.WriteLine(JsonSerializer.Serialize(result)); return result;
        }
        var baseline = new ServerMessage { Type = "snapshot", Snapshot = session.World.Snapshot() };
        session.Step();
        var changed = new ServerMessage { Type = "snapshot", Snapshot = session.World.Snapshot() };
        byte[] compressed = Protocol.SerializeSnapshot(changed);
        byte[] delta = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(changed, baseline));
        Console.WriteLine(JsonSerializer.Serialize(new { measurement = "snapshot_payload", fullBytes = Protocol.SerializeUtf8(changed).Length,
            compressedBytes = compressed.Length, deltaBytes = delta.Length,
            chosenBytes = Math.Min(compressed.Length, delta.Length) }));
        var reports = new[]
        {
            Measure("encode_text_then_utf8", () => _ = Encoding.UTF8.GetBytes(Protocol.Serialize(message))),
            Measure("encode_utf8", () => _ = Protocol.SerializeUtf8(message)),
            Measure("decode_utf8_through_text", () => _ = Protocol.Deserialize<ServerMessage>(Encoding.UTF8.GetString(bytes))),
            Measure("decode_utf8", () => _ = Protocol.Deserialize<ServerMessage>(bytes)),
            Measure("validate_snapshot", () => SnapshotValidation.Check(message.Snapshot!))
        };
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { schema = 1, version = BuildInfo.Version,
            scope = "local_json_serialization", transportExcluded = true, warmupIterations = 100, reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PROTOCOL_PROFILE_OK: " + path);
    }
}
