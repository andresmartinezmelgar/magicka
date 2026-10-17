using Arcana.Core;
using System.Diagnostics;

internal static class StreamingPerformanceBenchmarks
{
    public static void Run()
    {
        var session = new SurvivalPerformanceSession();
        var first = session.Frame(0);
        var baseline = new ServerMessage { Type = "welcome", Snapshot = first with { Defense = first.Defense! with {
            Minions = [], Drops = [], Defenders = first.Defense.Defenders.Select(p => p with { Items = [] }).ToArray() } },
            ReadyPlayerIds = first.Players.Select(p => p.Id).ToArray() };
        var frames = Enumerable.Range(0, 180).Select(_ => new ServerMessage { Type = "snapshot",
            Snapshot = DefenseVisibility.ForPlayer(session.Frame(0.05), first.Players.First(p => !p.Bot).Id), ReadyPlayerIds = baseline.ReadyPlayerIds }).ToArray();
        SnapshotValidation.Check(frames[^1].Snapshot!);
        long beforeBytes = 0, afterBytes = 0;
        var watch = Stopwatch.StartNew(); long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        foreach (var frame in frames)
        {
            var full = Protocol.SerializeSnapshot(frame);
            var delta = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(frame, baseline));
            beforeBytes += Math.Min(full.Length, delta.Length);
        }
        double beforeMs = watch.Elapsed.TotalMilliseconds;
        beforeAllocation = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        var sender = new SnapshotSender(baseline); var receiver = new SnapshotReceiver(baseline);
        watch.Restart(); long afterAllocation = GC.GetAllocatedBytesForCurrentThread();
        foreach (var frame in frames)
        {
            var live = sender.Encode(frame, out var keyframe); afterBytes += live.Length;
            if (keyframe != null)
            {
                afterBytes += keyframe.Length;
                var message = Protocol.DeserializeFrame(keyframe, keyframe[0] == 0xA1)!;
                receiver.Accept(message); sender.Acknowledge(message.BaselineId);
            }
        }
        double afterMs = watch.Elapsed.TotalMilliseconds;
        afterAllocation = GC.GetAllocatedBytesForCurrentThread() - afterAllocation;
        Console.WriteLine("STREAM_BENCHMARK_OK: " + Protocol.Serialize(new { frames = frames.Length, beforeBytes, afterBytes,
            beforeMs, afterMs, beforeAllocation, afterAllocation,
            note = "Mismos estados reales para base inicial fija y bases confirmadas. Incluye keyframes; no mide un dispositivo físico." }));
    }
}
