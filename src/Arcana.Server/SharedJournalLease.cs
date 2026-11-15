using System.Collections.Concurrent;

namespace Arcana.Server;

/// <summary>Exclusión breve sobre el volumen compartido; nunca se mantiene durante una llamada a Firebase.</summary>
internal sealed class SharedJournalLease : IDisposable
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Local = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate;
    private FileStream? _stream;
    internal SharedJournalLease(string directory)
    {
        string file = Path.Combine(Path.GetFullPath(directory), ".journal.lock");
        _gate = Local.GetOrAdd(file, _ => new(1, 1));
        if (!_gate.Wait(0)) throw new IOException("Diario ocupado; se reintentará.");
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _stream = new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            _stream.Lock(0, 1);
        }
        catch { _stream?.Dispose(); _stream = null; _gate.Release(); throw; }
    }
    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        if (stream == null) return;
        try { stream.Dispose(); } finally { _gate.Release(); }
    }
}
