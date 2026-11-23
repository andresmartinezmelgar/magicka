using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Arcana.Core;

/// <summary>JSONL, cola acotada y escritura fuera de los hilos de juego. Una avería del disco no derriba la partida.</summary>
public sealed partial class DiagnosticLog : IDiagnosticLog, IDisposable, IAsyncDisposable
{
    private sealed record Pending(string? Line, TaskCompletionSource? Barrier);
    private readonly Channel<Pending> _queue;
    private readonly Task _writer;
    private readonly object _secretGate = new(), _disposeGate = new();
    private readonly Queue<(string Value, string Escaped)> _secrets = new();
    private readonly string _component;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private readonly DiagnosticLevel _minimum;
    private int _closed, _part;
    private long _dropped;
    private Task? _disposal;
    private string? _lastError;
    public string DirectoryPath { get; }
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string CurrentFilePath => Path.Combine(DirectoryPath, $"{_component}-{SessionId[..12]}-{Volatile.Read(ref _part):D3}.jsonl");
    public string? LastError => Volatile.Read(ref _lastError);
    private static readonly Regex SensitiveKey = new("password|contraseña|secret|token|authorization|cookie|credential", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex InlineSecret = new("(password|serverPassword|roomPassword|token|authorization|secret)[\"']?\\s*[:=]\\s*(\"[^\"]*\"|'[^']*'|[^\\s,;}]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public DiagnosticLog(string directory, string component, DiagnosticLevel minimum = DiagnosticLevel.Information,
        long maxFileBytes = 2 * 1024 * 1024, int maxFiles = 5, int queueCapacity = 2048)
    {
        if (!Regex.IsMatch(component, "^[a-zA-Z0-9_-]{1,24}$")) throw new ArgumentException("Componente de log inválido.", nameof(component));
        DirectoryPath = Path.GetFullPath(directory); _component = component; _minimum = minimum;
        _maxBytes = Math.Clamp(maxFileBytes, 1024, 64 * 1024 * 1024); _maxFiles = Math.Clamp(maxFiles, 2, 20);
        _queue = Channel.CreateBounded<Pending>(new BoundedChannelOptions(Math.Clamp(queueCapacity, 16, 8192))
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _writer = Task.Run(Consume);
    }
    public void Write(DiagnosticLevel level, string code, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null)
    {
        if (level < _minimum || Volatile.Read(ref _closed) != 0) return;
        try
        {
            if (!_queue.Writer.TryWrite(new(Line(level, code, message, exception, context), null))) Interlocked.Increment(ref _dropped);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        { Volatile.Write(ref _lastError, e.GetType().Name); }
    }
    private async Task Consume()
    {
        FileStream? file = null; StreamWriter? output = null; long length = 0, retryAt = 0;
        void Close() { try { output?.Dispose(); file?.Dispose(); } catch (IOException) { } output = null; file = null; }
        async Task Append(string line)
        {
            if (Environment.TickCount64 < retryAt) return;
            try
            {
                int bytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (file == null || length > 0 && length + bytes > _maxBytes)
                {
                    if (file != null) { Close(); Interlocked.Increment(ref _part); }
                    Directory.CreateDirectory(DirectoryPath);
                    file = new FileStream(CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 16 * 1024, FileOptions.Asynchronous);
                    output = new StreamWriter(file, new UTF8Encoding(false)) { NewLine = "\n" }; length = file.Length;
                    Prune();
                }
                await output!.WriteLineAsync(line).ConfigureAwait(false); length += bytes;
                Volatile.Write(ref _lastError, null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Volatile.Write(ref _lastError, e.GetType().Name + ": " + e.Message); Close(); retryAt = Environment.TickCount64 + 1000; }
        }
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                long dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped > 0) await Append(Line(DiagnosticLevel.Warning, "logs.dropped", "Cola de logs saturada; se agruparon mensajes.", null,
                    new Dictionary<string, object?> { ["count"] = dropped })).ConfigureAwait(false);
                var barriers = new List<TaskCompletionSource>();
                for (int n = 0; n < 128 && _queue.Reader.TryRead(out var entry); n++)
                {
                    if (entry.Line != null) await Append(entry.Line).ConfigureAwait(false);
                    if (entry.Barrier != null) barriers.Add(entry.Barrier);
                }
                try { if (output != null) await output.FlushAsync().ConfigureAwait(false); }
                catch (IOException e) { Volatile.Write(ref _lastError, e.Message); Close(); }
                foreach (var barrier in barriers) barrier.TrySetResult();
            }
        }
        finally { Close(); }
    }
    private void Prune()
    {
        try
        {
            var files = Directory.GetFiles(DirectoryPath, _component + "-*.jsonl").Select(p => new FileInfo(p))
                .OrderByDescending(p => p.LastWriteTimeUtc).ThenByDescending(p => p.Name).ToArray();
            foreach (var file in files.Skip(_maxFiles))
                if (file.FullName != CurrentFilePath) try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Volatile.Write(ref _lastError, e.GetType().Name); }
    }
    public async Task FlushAsync(CancellationToken token = default)
    {
        if (Volatile.Read(ref _closed) != 0) { await _writer.WaitAsync(token).ConfigureAwait(false); return; }
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await _queue.Writer.WriteAsync(new(null, barrier), token).ConfigureAwait(false); }
        catch (ChannelClosedException) { await _writer.WaitAsync(token).ConfigureAwait(false); return; }
        await barrier.Task.WaitAsync(token).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposal == null) { Interlocked.Exchange(ref _closed, 1); _queue.Writer.TryComplete(); _disposal = _writer; }
            return new(_disposal);
        }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

}
