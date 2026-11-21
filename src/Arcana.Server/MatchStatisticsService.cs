using Arcana.Core;

namespace Arcana.Server;

public sealed class MatchStatisticsService(AccountService accounts, ILogger<MatchStatisticsService> logger, string? journalDirectory = null) : BackgroundService
{
    private static string DirectoryFor(string? configured) => configured ?? Environment.GetEnvironmentVariable("ARCANA_MATCH_STATISTICS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcanaArena", "statistics-outbox");
    private static bool Shared => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARCANA_SERVER_GENERATION"));
    private readonly MatchStatisticsJournal? _journal = Shared ? new(DirectoryFor(journalDirectory)) : null;
    private readonly MatchStatisticsOutbox _outbox = Shared ? new(shared: new MatchStatisticsJournal(DirectoryFor(journalDirectory))) : new(DirectoryFor(journalDirectory));
    public int UnwrittenCount => _outbox.UnwrittenCount;
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
                if (_journal != null && accounts.GameEnabled)
                {
                    try { foreach (var stored in _journal.Pending()) _outbox.Enqueue(stored); }
                    catch (IOException) { /* El otro contenedor está escribiendo. Se reintenta en el siguiente ciclo. */ }
                }
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
