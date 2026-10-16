using System.Collections.Concurrent;
using Arcana.Core;

namespace Arcana.Server;

/// <summary>Guarda cada hito antes de confirmarlo a la simulación. Firestore confirma la entrega mediante un recibo idempotente.</summary>
public sealed class RewardService(AccountService accounts, ILogger<RewardService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, RewardMatch> _unwritten = new(StringComparer.Ordinal);
    private readonly object _journalGate = new();
    private readonly string _directory = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArcanaArena", "rewards")
        : Path.GetFullPath(Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX")!);
    public event Action<RewardMatch>? Delivered;

    /// <returns>True cuando el hito ya está guardado en disco, aunque el servicio de cuentas esté caído.</returns>
    public bool Enqueue(RewardMatch match)
    {
        if (RewardRules.Eligible(match).Length == 0) return false;
        _unwritten.TryAdd(match.Id, match);
        return Persist(match);
    }
    private void PrepareDirectory()
    {
        Directory.CreateDirectory(_directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private bool Persist(RewardMatch match)
    {
        lock (_journalGate)
        {
            try
            {
                PrepareDirectory();
                string file = Path.Combine(_directory, match.Id + ".json");
                if (!File.Exists(file))
                {
                    string temporary = file + ".tmp";
                    using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    {
                        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                        stream.Write(Protocol.SerializeUtf8(match)); stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, file);
                }
                _unwritten.TryRemove(match.Id, out _);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { logger.LogError(error, "Hito {MatchId} pendiente de guardar en disco; se reintentará.", match.Id); return false; }
        }
    }
    private void RecoverInterruptedWrites()
    {
        lock (_journalGate)
        foreach (string temporary in Directory.EnumerateFiles(_directory, "*.json.tmp"))
        {
            try
            {
                if (new FileInfo(temporary).Length > 64 * 1024) throw new InvalidDataException("Recibo demasiado grande.");
                var match = Protocol.Deserialize<RewardMatch>(File.ReadAllText(temporary));
                if (match == null || RewardRules.Eligible(match).Length == 0 || Path.GetFileName(temporary) != match.Id + ".json.tmp")
                    throw new InvalidDataException("Escritura de recibo incompleta.");
                string file = temporary[..^4];
                if (File.Exists(file)) File.Delete(temporary); else File.Move(temporary, file);
            }
            catch (Exception error) when (error is InvalidDataException || error is not IOException and not UnauthorizedAccessException)
            {
                logger.LogError(error, "Recibo interrumpido conservado para revisión: {File}", Path.GetFileName(temporary));
                File.Move(temporary, temporary + ".incomplete", overwrite: true);
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retry = new Dictionary<string, DateTimeOffset>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    PrepareDirectory();
                    RecoverInterruptedWrites();
                    foreach (var match in _unwritten.Values) Persist(match);
                    // Sin conexión o sin configuración de cuentas, el diario sigue conservando todos los premios.
                    if (accounts.GameEnabled)
                    foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
                    {
                        if (retry.TryGetValue(path, out var next) && next > DateTimeOffset.UtcNow) continue;
                        try
                        {
                            var match = Protocol.Deserialize<RewardMatch>(await File.ReadAllTextAsync(path, stoppingToken))
                                ?? throw new InvalidDataException("Resultado vacío.");
                            if (RewardRules.Eligible(match).Length == 0 || Path.GetFileNameWithoutExtension(path) != match.Id)
                                throw new InvalidDataException("Recibo de recompensa inválido.");
                            await accounts.AwardGame(match, stoppingToken);
                            // Si hay una caída entre Award y Delete, Firestore reconoce el mismo recibo al reintentar.
                            lock (_journalGate) File.Delete(path);
                            retry.Remove(path);
                            try { Delivered?.Invoke(match); }
                            catch (Exception error) { logger.LogWarning(error, "Premio guardado; la notificación se recuperará al actualizar el perfil."); }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        { logger.LogWarning(error, "Recompensa {MatchId} pendiente; se reintentará en 15 s.", Path.GetFileNameWithoutExtension(path)); retry[path] = DateTimeOffset.UtcNow.AddSeconds(15); }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { logger.LogError(error, "Diario de recompensas temporalmente inaccesible; se reintentará."); }
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var match in _unwritten.Values) Persist(match);
        return base.StopAsync(cancellationToken);
    }
}
