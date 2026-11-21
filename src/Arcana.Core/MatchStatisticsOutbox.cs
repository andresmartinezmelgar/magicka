namespace Arcana.Core;

public interface ISharedMatchJournal
{
    void Persist(MatchStatisticsReport report);
    void Acknowledge(MatchStatisticsReport report);
    MatchStatisticsReport[] Pending();
}

/// <summary>Cola acotada de informes pendientes. El diario opcional se escribe fuera del hilo de juego.</summary>
public sealed class MatchStatisticsOutbox : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Dictionary<string, MatchStatisticsReport> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MatchStatisticsReport> _written = new(StringComparer.Ordinal);
    private readonly string? _directory;
    private readonly int _capacity;
    private readonly ISharedMatchJournal? _shared;
    private readonly SemaphoreSlim _disk = new(1, 1);
    private readonly System.Threading.Channels.Channel<bool> _writes = System.Threading.Channels.Channel.CreateBounded<bool>(
        new System.Threading.Channels.BoundedChannelOptions(1) { SingleReader = true, FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    private readonly Task _writer;
    public MatchStatisticsOutbox(string? directory = null, int capacity = 128, ISharedMatchJournal? shared = null)
    {
        _shared = shared;
        _capacity = Math.Clamp(capacity, 1, 512); _directory = directory == null ? null : Path.GetFullPath(directory);
        if (_directory != null)
        {
            Directory.CreateDirectory(_directory);
            foreach (string file in Directory.EnumerateFiles(_directory, "*.json").OrderBy(p => p, StringComparer.Ordinal).Take(_capacity))
                try
                {
                    if (new FileInfo(file).Length > MatchStatisticsReport.MaxBytes) continue;
                    var report = MatchStatisticsReport.Parse(File.ReadAllText(file));
                    if (Path.GetFileNameWithoutExtension(file) == report.RunId && report.Status != MatchRecordStatus.Playing)
                    { _pending[report.RunId] = report; _written[report.RunId] = report; }
                }
                catch (Exception e) when (e is IOException or System.Text.Json.JsonException or ArgumentException)
                { Diagnostics.Warn("statistics.journal_invalid", "Informe pendiente ilegible."); }
        }
        _writer = _directory == null && _shared == null ? Task.CompletedTask : Task.Run(WriteLoop);
    }
    public int PendingCount { get { lock (_gate) return _pending.Count; } }
    public int UnwrittenCount
    {
        get
        {
            if (!_disk.Wait(0)) return 1;
            try { lock (_gate) return _pending.Values.Count(r => !_written.TryGetValue(r.RunId, out var saved) || !ReferenceEquals(r, saved)); }
            finally { _disk.Release(); }
        }
    }
    public bool Contains(string runId) { lock (_gate) return _pending.ContainsKey(runId); }

    public bool Enqueue(MatchStatisticsReport report)
    {
        if (report.Status == MatchRecordStatus.Playing || report.Revision == 0) return false;
        // Aísla las matrices del mundo y valida el contrato antes del envío asíncrono.
        var copy = MatchStatisticsReport.Parse(report.ToJson());
        lock (_gate)
        {
            if (_pending.TryGetValue(copy.RunId, out var previous) &&
                (previous.Network && !copy.Network || previous.Network == copy.Network && !MatchStatisticsReport.IsNewer(copy, previous))) return false;
            if (!_pending.ContainsKey(copy.RunId) && _pending.Count >= _capacity)
            { Diagnostics.Warn("statistics.queue_full", "La cola de estadísticas está llena; no se aceptan más informes."); return false; }
            _pending[copy.RunId] = copy; _writes.Writer.TryWrite(true);
            return true;
        }
    }

    public async Task Flush(Func<MatchStatisticsReport, CancellationToken, Task> send, CancellationToken token = default)
    {
        await _sending.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await FlushPersistence(token).ConfigureAwait(false);
            MatchStatisticsReport[] pending;
            lock (_gate) pending = _pending.Values.ToArray();
            Exception? failure = null;
            foreach (var report in pending)
            {
                token.ThrowIfCancellationRequested();
                try { await send(report, token).ConfigureAwait(false); }
                catch (StatisticsRejectedException)
                { Diagnostics.Warn("statistics.expired", "Informe descartado: incompatible o fuera del periodo de retención."); }
                catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
                { failure ??= error; continue; } // Un informe fallido no bloquea las demás partidas.
                lock (_gate)
                    if (_pending.TryGetValue(report.RunId, out var current) && ReferenceEquals(report, current)) { _pending.Remove(report.RunId); _writes.Writer.TryWrite(true); }
            }
            await FlushPersistence(token).ConfigureAwait(false);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { _sending.Release(); }
    }
    public async Task FlushPersistence(CancellationToken token = default)
    {
        if (_directory == null && _shared == null) return;
        await _disk.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // fsync y serialización nunca ejecutan sobre el hilo de Godot,
            // incluso cuando WaitAsync o una escritura pequeña terminan sin espera.
            await Task.Run(() =>
            {
                MatchStatisticsReport[] reports;
                lock (_gate) reports = _pending.Values.ToArray();
                var retained = reports.Select(r => r.RunId).ToHashSet(StringComparer.Ordinal);
                foreach (var report in reports)
                {
                    token.ThrowIfCancellationRequested();
                    if (_written.TryGetValue(report.RunId, out var saved) && ReferenceEquals(saved, report)) continue;
                    if (_shared != null)
                    {
                        _shared.Persist(report);
                        _written[report.RunId] = report;
                        continue;
                    }
                    string file = Path.Combine(_directory!, report.RunId + ".json"), temporary = file + ".tmp";
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(report.ToJson());
                    using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    { output.Write(bytes); output.Flush(true); }
                    File.Move(temporary, file, true); _written[report.RunId] = report;
                }
                foreach (string id in _written.Keys.Where(id => !retained.Contains(id)).ToArray())
                {
                    if (_shared != null) _shared.Acknowledge(_written[id]);
                    else File.Delete(Path.Combine(_directory!, id + ".json"));
                    _written.Remove(id);
                }
            }, token).ConfigureAwait(false);
        }
        finally { _disk.Release(); }
    }
    private async Task WriteLoop()
    {
        await foreach (var _ in _writes.Reader.ReadAllAsync())
            try { await FlushPersistence().ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Diagnostics.Warn("statistics.journal_failed", "No se pudo guardar la cola de estadísticas; se reintentará."); }
    }
    public async ValueTask DisposeAsync()
    {
        _writes.Writer.TryComplete(); await _writer.ConfigureAwait(false); await FlushPersistence().ConfigureAwait(false);
    }
}
