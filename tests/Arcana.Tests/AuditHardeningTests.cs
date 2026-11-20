using Arcana.Core;

internal static class AuditHardeningTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Estadísticas: diario recuperable, cuota y confirmación duradera", () => Persistence().GetAwaiter().GetResult());
        test("Estadísticas: un informe local no sustituye al del servidor en la cola", () =>
        {
            var world = new GameWorld(); world.AddPlayer("Mago"); world.Step();
            var online = world.MatchStatistics(MatchRecordStatus.Completed) with { Network = true };
            var queue = new MatchStatisticsOutbox();
            if (!queue.Enqueue(online) || queue.Enqueue(online with { Network = false, Revision = online.Revision + 1 }))
                throw new Exception("La cola permite reemplazar estadísticas autoritativas.");
        });
        test("Estadísticas: un informe fallido no bloquea los siguientes", () =>
        {
            var world = new GameWorld(); world.AddPlayer("Mago"); world.Step();
            var first = world.MatchStatistics(MatchRecordStatus.Completed);
            var second = first with { RunId = Guid.NewGuid().ToString("N") };
            var queue = new MatchStatisticsOutbox(); queue.Enqueue(first); queue.Enqueue(second);
            int delivered = 0;
            try
            {
                queue.Flush((report, _) => report.RunId == first.RunId
                    ? Task.FromException(new IOException("Fallo individual")) : Delivered()).GetAwaiter().GetResult();
                throw new Exception("Se ocultó el fallo pendiente.");
            }
            catch (IOException) { }
            Task Delivered() { delivered++; return Task.CompletedTask; }
            if (delivered != 1 || queue.PendingCount != 1 || !queue.Contains(first.RunId)) throw new Exception("Un informe bloquea la cola o desaparece sin confirmar.");
        });
        test("Deltas: reconstruir y reordenar no modifica la base compartida", () =>
        {
            var world = new GameWorld(new WorldOptions());
            var first = world.AddPlayer("A", 0)!; world.AddPlayer("B", 1);
            var baseline = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            string original = Protocol.Serialize(baseline);
            world.RemovePlayer(first.Id); world.AddPlayer("C", 0); world.Step();
            var next = new ServerMessage { Type = "snapshot", Snapshot = world.Snapshot() };
            var bytes = SnapshotDelta.Encode(next, baseline);
            for (int i = 0; i < 3; i++)
            {
                var rebuilt = SnapshotDelta.Decode(bytes, baseline);
                if (Protocol.Serialize(rebuilt.Snapshot) != Protocol.Serialize(next.Snapshot) || Protocol.Serialize(baseline) != original)
                    throw new Exception("La fusión altera la base o pierde entidades.");
            }
        });
    }
    private static async Task Persistence()
    {
        string folder = Path.Combine(Path.GetTempPath(), "arcana-statistics-" + Guid.NewGuid().ToString("N"));
        try
        {
            var world = new GameWorld(new WorldOptions()); world.AddPlayer("Mago"); world.Step();
            var report = world.MatchStatistics(MatchRecordStatus.Completed);
            await using (var queue = new MatchStatisticsOutbox(folder, 1))
            {
                if (!queue.Enqueue(report) || queue.Enqueue(report with { RunId = Guid.NewGuid().ToString("N") }))
                    throw new Exception("No respeta el límite de la cola.");
                await queue.FlushPersistence();
            }
            await using var recovered = new MatchStatisticsOutbox(folder, 1);
            if (recovered.PendingCount != 1) throw new Exception("No recupera un informe no confirmado.");
            await recovered.Flush((_, _) => Task.CompletedTask);
            if (recovered.PendingCount != 0 || Directory.EnumerateFiles(folder, "*.json").Any()) throw new Exception("El diario conserva un envío ya confirmado.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
