using Arcana.Core;

namespace Arcana.Server;

public sealed class MatchStatisticsService(AccountService accounts, ILogger<MatchStatisticsService> logger) : BackgroundService
{
    private readonly MatchStatisticsOutbox _outbox = new();
    public void Record(MatchStatisticsReport report) => _outbox.Enqueue(report);
    public void Accept(MatchStatisticsReport report) => _outbox.Enqueue(report);
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
}
