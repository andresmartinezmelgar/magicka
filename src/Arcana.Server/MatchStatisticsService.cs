using Arcana.Core;

namespace Arcana.Server;

public sealed class MatchStatisticsService(AccountService accounts, ILogger<MatchStatisticsService> logger, string? journalDirectory = null) : BackgroundService
{
    private readonly MatchStatisticsOutbox _outbox = new(journalDirectory ?? Environment.GetEnvironmentVariable("ARCANA_MATCH_STATISTICS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcanaArena", "statistics-outbox"));
    public int PendingCount => _outbox.PendingCount;
    public void Record(MatchStatisticsReport report) { if (accounts.GameEnabled) _outbox.Enqueue(report); }
    public bool Accept(MatchStatisticsReport report) => _outbox.Enqueue(report) || _outbox.Contains(report.RunId);
    public Task Persist(CancellationToken token) => _outbox.FlushPersistence(token);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            int wait = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (wait-- <= 0 && accounts.GameEnabled && _outbox.PendingCount > 0)
                {
                    try { await _outbox.Flush(accounts.SaveMatchStatisticsGame, stoppingToken); }
                    catch (Exception e) when (e is not OperationCanceledException)
                    { logger.LogWarning("Estadísticas pendientes en memoria ({Type}).", e.GetType().Name); wait = 30; }
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await _outbox.FlushPersistence(cancellationToken);
        await _outbox.DisposeAsync();
    }
}
