using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcana.Core;

internal static class MapPerformanceBenchmarks
{
    // Simulación local reproducible; las cifras no incluyen GPU, audio ni red.
    internal static void Run(string output)
    {
        var reports = new List<object>();
        foreach (var id in Enum.GetValues<ArenaMapId>())
        {
            long loading = Stopwatch.GetTimestamp();
            var session = id == ArenaMapId.Practice ? new PracticeSession("Medición") : null;
            var world = session?.World ?? new GameWorld(new() { Map = id, Bots = 7, MaxPlayers = 8,
                Mode = GameMode.Warfare, Tickets = 1000, Seed = 73, SpawnSeed = 271, PickupSeed = 271, EnvironmentSeed = 271 });
            if (session == null) world.AddPlayer("Medición", 0);
            double loadingMs = Stopwatch.GetElapsedTime(loading).TotalMilliseconds;
            var samples = new double[3600];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                long began = Stopwatch.GetTimestamp();
                if (session != null) session.Step(); else world.Step();
                var snapshot = world.Snapshot();
                samples[i] = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                if (i % 120 == 0) SnapshotValidation.Check(snapshot);
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var sorted = samples.Skip(120).Order().ToArray();
            var result = new { map = id.ToString(), bots = session == null ? 7 : 0, ticks = samples.Length,
                loadingMs, warmupTicks = 120, coldMaximumMs = samples.Take(120).Max(), meanMs = sorted.Average(),
                p95Ms = sorted[(int)Math.Ceiling((sorted.Length - 1) * 0.95)], maximumMs = sorted[^1],
                overBudgetTicks = sorted.Count(t => t > 1000.0 / 60), allocatedBytes,
                finalStateSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(world.Snapshot())))) };
            reports.Add(result); Console.WriteLine(JsonSerializer.Serialize(result));
        }
        string path = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { schema = 1, version = BuildInfo.Version,
            scope = "deterministic_local_simulation", seed = 73, spawnSeed = 271, pickupSeed = 271, environmentSeed = 271,
            includesSnapshotCopy = true, graphicsAndNetwork = false, maps = reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("MAP_PROFILE_OK: " + path);
    }
}
