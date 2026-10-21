namespace Arcana.Core;

/// <summary>Historial JSON y cola duradera. Una confirmación antigua nunca borra una revisión nueva.</summary>
public sealed class MatchStatisticsArchive(string directory)
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private string History => Path.Combine(directory, "matches");
    private string Pending => Path.Combine(directory, "pending");
    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(System.Text.Encoding.UTF8.GetBytes(json)); stream.Flush(true); }
        File.Move(path + ".tmp", path, true);
    }
    private static MatchStatisticsReport? Read(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > MatchStatisticsReport.MaxBytes) throw new InvalidDataException();
            var value = MatchStatisticsReport.Parse(File.ReadAllText(path));
            if (Path.GetFileName(path) != value.RunId + ".json") throw new InvalidDataException();
            return value;
        }
        catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException)
        { File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N"), true); return null; }
    }
    public void Save(MatchStatisticsReport report, bool upload = true)
    {
        string json = report.ToJson();
        if (report.Revision == 0) return;
        lock (_gate)
        {
            string path = Path.Combine(History, report.RunId + ".json");
            var previous = Read(path);
            if (previous != null && !MatchStatisticsReport.IsNewer(report, previous))
            { if (report != previous && report.ToJson() != previous.ToJson()) return; }
            if (upload)
            {
                string pending = Path.Combine(Pending, report.RunId + ".json");
                var queued = Read(pending);
                if (queued != null && MatchStatisticsReport.IsNewer(queued, report)) return;
                if (queued == null || MatchStatisticsReport.IsNewer(report, queued)) Write(pending, json);
            }
            if (previous == null || MatchStatisticsReport.IsNewer(report, previous)) Write(path, json);
        }
    }
    public MatchStatisticsReport? Find(string runId)
    {
        if (!Guid.TryParseExact(runId, "N", out _)) return null;
        lock (_gate) return Read(Path.Combine(History, runId + ".json"));
    }
    public MatchStatisticsReport[] LocalReports(int limit = 200)
    {
        lock (_gate) return !Directory.Exists(History) ? [] : Directory.EnumerateFiles(History, "*.json")
            .Select(Read).OfType<MatchStatisticsReport>().OrderByDescending(p => p.Day).ThenByDescending(p => p.Revision).Take(Math.Clamp(limit, 1, 2000)).ToArray();
    }
    public void RecoverInterrupted()
    {
        lock (_gate)
        {
            // La cola se escribe primero: recuperar el historial si hubo un corte entre ambos renombrados.
            if (Directory.Exists(Pending)) foreach (string path in Directory.GetFiles(Pending, "*.json"))
                if (Read(path) is {} report) Save(report);
            foreach (var report in LocalReports(2000))
                if (report.Status == MatchRecordStatus.Playing) Save(report with { Status = MatchRecordStatus.Interrupted });
        }
    }
    public async Task Flush(Func<MatchStatisticsReport, CancellationToken, Task> send, CancellationToken token = default)
    {
        await _sending.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string[] paths;
            lock (_gate) paths = Directory.Exists(Pending) ? Directory.GetFiles(Pending, "*.json") : [];
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                MatchStatisticsReport? report;
                lock (_gate) report = Read(path);
                if (report == null) continue;
                await send(report, token).ConfigureAwait(false);
                lock (_gate)
                {
                    var current = Read(path);
                    if (current != null && current.ToJson() == report.ToJson()) File.Delete(path);
                }
            }
        }
        finally { _sending.Release(); }
    }
}
