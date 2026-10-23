namespace Arcana.Core;

/// <summary>Reintentos en memoria: no crea archivos ni conserva un historial local.</summary>
public sealed class MatchStatisticsOutbox
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sending = new(1, 1);
    private readonly Dictionary<string, MatchStatisticsReport> _pending = new(StringComparer.Ordinal);
    public int PendingCount { get { lock (_gate) return _pending.Count; } }

    public bool Enqueue(MatchStatisticsReport report)
    {
        if (report.Status == MatchRecordStatus.Playing || report.Revision == 0) return false;
        // Aísla las matrices del mundo y valida el contrato antes del envío asíncrono.
        var copy = MatchStatisticsReport.Parse(report.ToJson());
        lock (_gate)
        {
            if (_pending.TryGetValue(copy.RunId, out var previous) && !MatchStatisticsReport.IsNewer(copy, previous)) return false;
            _pending[copy.RunId] = copy;
            return true;
        }
    }

    public async Task Flush(Func<MatchStatisticsReport, CancellationToken, Task> send, CancellationToken token = default)
    {
        await _sending.WaitAsync(token).ConfigureAwait(false);
        try
        {
            MatchStatisticsReport[] pending;
            lock (_gate) pending = _pending.Values.ToArray();
            foreach (var report in pending)
            {
                token.ThrowIfCancellationRequested();
                await send(report, token).ConfigureAwait(false);
                lock (_gate)
                    if (_pending.TryGetValue(report.RunId, out var current) && ReferenceEquals(report, current)) _pending.Remove(report.RunId);
            }
        }
        finally { _sending.Release(); }
    }
}
