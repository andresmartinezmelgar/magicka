using System.Diagnostics;

namespace Arcana.Server;

// Un servidor dedicado no tiene esta variable. El alojado por el launcher sale si este se cierra.
public sealed class ParentLifetimeService(IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("ARCANA_PARENT_PID"), out int parentId) || parentId <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { using var parent = Process.GetProcessById(parentId); if (!parent.HasExited) continue; }
                catch (ArgumentException) { }
                lifetime.StopApplication(); return;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
