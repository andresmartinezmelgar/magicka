using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Arcana.Core;

internal static class SurvivalMapGenerationBenchmark
{
    private sealed record Timing(int Samples, double MedianMs, double P95Ms, double MaximumMs);
    private static Timing Summary(List<double> samples)
    {
        var sorted = samples.Order().ToArray();
        return new(sorted.Length, Math.Round((sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2, 3),
            Math.Round(sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], 3), Math.Round(sorted[^1], 3));
    }
    internal static void Run(string output)
    {
        // Semillas diferentes evitan medir la caché. Calentamiento fuera de las muestras.
        for (int i = 0; i < 4; i++) _ = new GameWorld(new() { Mode = GameMode.Defense,
            Map = ArenaMapId.Bastion, MaxPlayers = 4, SurvivalMap = new(700_000 + i) });
        var geometry = new List<double>(); var ready = new List<double>();
        var structures = new List<SurvivalMapStructure>();
        var clock = new Stopwatch();
        for (int i = 0; i < 64; i++)
        {
            clock.Restart(); var map = ArenaMap.Get(ArenaMapId.Bastion, new(710_000 + i)); clock.Stop();
            geometry.Add(clock.Elapsed.TotalMilliseconds);
            structures.Add(map.SurvivalStructure ?? throw new InvalidDataException("Falta el diagnóstico del mapa."));
            if (!map.Walkable(V2.Zero)) throw new InvalidDataException("Mapa sin aparición central.");
            clock.Restart();
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                MaxPlayers = 4, SurvivalMap = new(720_000 + i) });
            clock.Stop(); ready.Add(clock.Elapsed.TotalMilliseconds);
            if (!world.Map.Walkable(V2.Zero)) throw new InvalidDataException("Mundo sin aparición central.");
        }
        var report = new { Version = BuildInfo.Version, GeneratorVersion = SurvivalMapSpec.CurrentVersion, Width = 160, Height = 120,
            BudgetMilliseconds = SurvivalMapSpec.GenerationBudgetMilliseconds,
            Rooms = new { Minimum = structures.Min(s => s.Rooms), Maximum = structures.Max(s => s.Rooms),
                DistinctCounts = structures.Select(s => s.Rooms).Distinct().Order().ToArray() },
            MaximumRoomShapeKinds = structures.Max(s => s.RoomShapeKinds),
            MaximumCorridorShapeKinds = structures.Max(s => s.CorridorShapeKinds),
            Runtime = RuntimeInformation.FrameworkDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            GeometryAndNavigation = Summary(geometry), WorldIncludingGeometryAndNavigation = Summary(ready),
            Scope = "CPU local; 64 semillas nuevas por medida; sin caché ni carga de recursos Godot. No mide Android físico." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("SURVIVAL_MAP_GENERATION: " + JsonSerializer.Serialize(report));
    }
}
