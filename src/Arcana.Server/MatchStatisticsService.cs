using Arcana.Core;
using System.Collections.Concurrent;

namespace Arcana.Server;

public sealed class MatchStatisticsService(AccountService accounts, ILogger<MatchStatisticsService> logger) : BackgroundService
{
    private readonly MatchStatisticsOutbox _outbox = new();
    private readonly ConcurrentDictionary<string, MatchStatisticsReport> _unwritten = new(StringComparer.Ordinal);
    private int _recording;
    private readonly MatchStatisticsJournal? _journal = Environment.GetEnvironmentVariable("ARCANA_MATCH_STATISTICS_DIR") is { Length: > 0 } path
        ? new(Path.GetFullPath(path)) : null;
    public int PendingCount => _outbox.PendingCount;
    public int UnwrittenCount => (_journal == null ? _outbox.PendingCount : _unwritten.Count) + Volatile.Read(ref _recording);
    public void Record(MatchStatisticsReport report)
    {
        if (!accounts.GameEnabled) return;
        Interlocked.Increment(ref _recording);
        try
        {
            if (!_outbox.Enqueue(report) || _journal == null) return;
            var copy = MatchStatisticsReport.Parse(report.ToJson());
            _unwritten.AddOrUpdate(copy.RunId, copy, (_, previous) => MatchStatisticsReport.IsNewer(copy, previous) ? copy : previous);
            Persist(copy);
        }
        finally { Interlocked.Decrement(ref _recording); }
    }
    public void Accept(MatchStatisticsReport report) => Record(report);
    private void Persist(MatchStatisticsReport report)
    {
        try
        {
            _journal!.Persist(report);
            ((ICollection<KeyValuePair<string, MatchStatisticsReport>>)_unwritten).Remove(new(report.RunId, report));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.LogWarning("Resultado pendiente de guardar en disco ({Type}).", error.GetType().Name); }
    }
    private async Task Deliver(MatchStatisticsReport report, CancellationToken token)
    {
        await accounts.SaveMatchStatisticsGame(report, token);
        _journal?.Acknowledge(report);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            int wait = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_journal != null && accounts.GameEnabled)
                {
                    foreach (var report in _unwritten.Values) Persist(report);
                    try { foreach (var report in _journal.Pending()) _outbox.Enqueue(report); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                    { logger.LogWarning("Cola de estadísticas temporalmente inaccesible ({Type}).", error.GetType().Name); }
                }
                if (wait-- <= 0 && accounts.GameEnabled && _outbox.PendingCount > 0)
                {
                    try { await _outbox.Flush(Deliver, stoppingToken); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    { logger.LogWarning("Estadísticas pendientes en memoria ({Type}).", e.GetType().Name); wait = 30; }
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
