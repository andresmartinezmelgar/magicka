using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

public sealed class MatchStatisticsService(AccountService accounts, ILogger<MatchStatisticsService> logger) : BackgroundService
{
    private readonly MatchStatisticsArchive _archive = new(StatisticsDirectory());
    private static string StatisticsDirectory()
    {
        string? configured = Environment.GetEnvironmentVariable("ARCANA_MATCH_STATISTICS_DIR");
        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ServerSettings.LogDirectory))!, "match-statistics") : Path.GetFullPath(configured);
    }
    private readonly Channel<MatchStatisticsReport> _reports = Channel.CreateUnbounded<MatchStatisticsReport>(new() { SingleReader = true });
    public void Record(MatchStatisticsReport report) => _reports.Writer.TryWrite(report);
    public void Accept(MatchStatisticsReport report) => _archive.Save(report);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            bool recovered = false;
            int wait = 0;
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!recovered)
                {
                    try { _archive.RecoverInterrupted(); recovered = true; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    { logger.LogWarning("No se pudo recuperar la cola de estadísticas; se reintentará."); }
                }
                Drain();
                if (wait-- <= 0 && accounts.GameEnabled)
                {
                    try { await _archive.Flush(accounts.SaveMatchStatisticsGame, stoppingToken); wait = 10; }
                    catch (Exception e) when (e is not OperationCanceledException)
                    { logger.LogWarning("Estadísticas pendientes de sincronizar ({Type}).", e.GetType().Name); wait = 60; }
                }
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { Drain(); }
    }
    private void Drain()
    {
        while (_reports.Reader.TryRead(out var report))
        {
            try { _archive.Save(report); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _reports.Writer.TryWrite(report); logger.LogError("No se pudo guardar un informe de partida; se reintentará."); break; }
        }
    }
}
