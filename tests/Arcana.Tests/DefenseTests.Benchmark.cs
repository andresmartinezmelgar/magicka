using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    public static void Benchmark(string path)
    {
        var world = World();
        var baseline = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
        for (int i = 0; i < 4; i++)
        {
            var player = world.AddPlayer("Defensor " + i)!;
            for (int item = 0; item < DefenseRules.InventoryLimit; item++)
                Invoke(world, "GrantDefenseItem", player, EquipmentCatalogue.Generate(new Random(i * 100 + item),
                    tier: ItemTier.Mythic, slot: (EquipmentSlot)(item % 4)) with
                    { Id = $"{i * 100 + item + 1:x32}", CreatedAt = DateTimeOffset.UnixEpoch });
        }
        Set(world, "_defenseElapsed", 810.0); Set(world, "_defenseDifficulty", 10); Set(world, "<Tick>k__BackingField", 48600L);
        for (int i = 0; i < DefenseRules.MaxMinions; i++) Enemy(world);
        // Mantiene constante el número de actores: sus explosiones se verifican en las regresiones de combate.
        foreach (var minion in ((IDictionary)Field(world, "_minions")).Values)
            if ((MinionKind)minion!.GetType().GetField("Kind")!.GetValue(minion)! == MinionKind.Bomber)
                minion.GetType().GetField("Attack")!.SetValue(minion, 60f);
        long simulationAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int tick = 0; tick < 1200; tick++)
        {
            foreach (var p in world.Players.Values) p.Invulnerable = 1.2f;
            world.Step();
        }
        double simulationMs = watch.Elapsed.TotalMilliseconds / 1200;
        double allocatedBytesPerStep = (GC.GetAllocatedBytesForCurrentThread() - simulationAllocationStart) / 1200.0;
        var message = new ServerMessage { Type = "snapshot", Snapshot = DefenseVisibility.ForPlayer(world.Snapshot(), world.Players.Keys.First()) };
        SnapshotValidation.Check(message.Snapshot);
        Check(message.Snapshot.Defense!.Minions.Length == DefenseRules.MaxMinions && world.Players.Count == 4, "La carga no mantuvo la horda máxima y cuatro jugadores.");
        int raw = Protocol.SerializeUtf8(message).Length;
        Check(raw < Protocol.MaxSnapshotBytes, "El peor inventario superó el límite del protocolo.");
        watch.Restart(); int compressed = 0;
        long encodingAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) compressed = Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(message, baseline)).Length;
        double encodingMs = watch.Elapsed.TotalMilliseconds / 60;
        double allocatedBytesPerRecipientFrame = (GC.GetAllocatedBytesForCurrentThread() - encodingAllocationStart) / 60.0;
        var state = world.Snapshot();
        var normalized = state with { Defense = state.Defense! with { Defenders = state.Defense.Defenders.Select(p => p with
            { Items = p.Items.Select((item, index) => item with { Item = item.Item with { Id = $"item-{p.PlayerId}-{index}" } }).ToArray() }).ToArray() } };
        string stateFingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Protocol.SerializeUtf8(normalized)));
        var result = new { defenders = 4, minions = DefenseRules.MaxMinions, temporaryItems = 96, simulatedTicks = 1200,
            millisecondsPerStep = simulationMs, millisecondsPerRecipientFrame = encodingMs,
            allocatedBytesPerStep, allocatedBytesPerRecipientFrame, stateFingerprint,
            rawBytes = raw, wireBytesPerFrame = compressed, kilobytesPerSecondAt20Hz = compressed * 20.0 / 1024,
            note = "Medición del entorno de pruebas; detonaciones aplazadas para sostener la carga de actores. Requiere medición adicional en Raspberry Pi y Android reales." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("DEFENSE_BENCHMARK_OK: " + Protocol.Serialize(result));
    }
}
