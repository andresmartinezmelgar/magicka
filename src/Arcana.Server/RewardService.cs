using System.Threading.Channels;
using Arcana.Core;

namespace Arcana.Server;

/// <summary>La simulación solo encola. El diario sobrevive al reinicio y los recibos de Firestore hacen seguros los reintentos.</summary>
public sealed class RewardService(AccountService accounts, ILogger<RewardService> logger) : BackgroundService
{
    private readonly Channel<RewardMatch> _pending = Channel.CreateUnbounded<RewardMatch>(new UnboundedChannelOptions { SingleReader = true });
    private readonly string _directory = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcanaArena", "rewards")
        : Path.GetFullPath(Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX")!);
    private Task _persistTask = Task.CompletedTask;
    public event Action<RewardMatch>? Delivered;
    public void Enqueue(RewardMatch match)
    {
        if (accounts.Enabled && RewardRules.Eligible(match).Length > 0 && !_pending.Writer.TryWrite(match))
            logger.LogError("No se pudo encolar el resultado {MatchId}", match.Id);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!accounts.Enabled) return;
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        _persistTask = Persist(stoppingToken);
        await Task.WhenAll(_persistTask, Deliver(stoppingToken));
    }
    private async Task Persist(CancellationToken token)
    {
        try
        {
            await foreach (var match in _pending.Reader.ReadAllAsync(token))
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        string file = Path.Combine(_directory, match.Id + ".json"), temporary = file + ".tmp";
                        await File.WriteAllTextAsync(temporary, Protocol.Serialize(match), token);
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        File.Move(temporary, file, true); break;
                    }
                    catch (IOException error)
                    { logger.LogError(error, "No se pudo guardar el resultado {MatchId}; se reintentará.", match.Id); await Task.Delay(1000, token); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task Deliver(CancellationToken token)
    {
        var retry = new Dictionary<string, DateTimeOffset>();
        try
        {
            while (!token.IsCancellationRequested)
            {
                foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
                {
                    if (retry.TryGetValue(path, out var next) && next > DateTimeOffset.UtcNow) continue;
                    try
                    {
                        var match = Protocol.Deserialize<RewardMatch>(await File.ReadAllTextAsync(path, token)) ?? throw new InvalidDataException("Resultado vacío.");
                        await accounts.Repository!.Award(match, token);
                        File.Delete(path); retry.Remove(path);
                        try { Delivered?.Invoke(match); }
                        catch (Exception error) { logger.LogWarning(error, "La recompensa se guardó pero no se pudo notificar en la partida."); }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    { logger.LogWarning(error, "Resultado pendiente de entregar: {MatchId}", Path.GetFileNameWithoutExtension(path)); retry[path] = DateTimeOffset.UtcNow.AddSeconds(30); }
                }
                await Task.Delay(1000, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pending.Writer.TryComplete();
        await _persistTask.WaitAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
