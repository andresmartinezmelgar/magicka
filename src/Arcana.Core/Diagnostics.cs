using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Arcana.Core;

public static class BuildInfo
{
    public static string Version => typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "development";
    public static long UpdateBuildNumber => long.TryParse(typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ArcanaUpdateBuild")?.Value, out long build) && build > 0 ? build : 0;
}
public enum DiagnosticLevel { Trace, Debug, Information, Warning, Error, Critical }
public interface IDiagnosticLog
{
    void Write(DiagnosticLevel level, string code, string message, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? context = null);
    void ProtectSecret(string? secret);
}
public static class Diagnostics
{
    private sealed class SilentLog : IDiagnosticLog
    {
        public void Write(DiagnosticLevel level, string code, string message, Exception? exception = null, IReadOnlyDictionary<string, object?>? context = null) { }
        public void ProtectSecret(string? secret) { }
    }
    private static IDiagnosticLog _log = new SilentLog();
    public static IDiagnosticLog Log { get => Volatile.Read(ref _log); set => Volatile.Write(ref _log, value); }
    public static void Info(string code, string message, IReadOnlyDictionary<string, object?>? context = null) => Log.Write(DiagnosticLevel.Information, code, message, context: context);
    public static void Warn(string code, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? context = null) => Log.Write(DiagnosticLevel.Warning, code, message, error, context);
    public static void Error(string code, string message, Exception? error = null, IReadOnlyDictionary<string, object?>? context = null) => Log.Write(DiagnosticLevel.Error, code, message, error, context);
}

/// <summary>JSONL, cola acotada y escritura fuera de los hilos de juego. Una avería del disco no derriba la partida.</summary>
public sealed class DiagnosticLog : IDiagnosticLog, IDisposable, IAsyncDisposable
{
    private sealed record Pending(string? Line, TaskCompletionSource? Barrier);
    private readonly Channel<Pending> _queue;
    private readonly Task _writer;
    private readonly object _secretGate = new(), _disposeGate = new();
    private readonly Queue<string> _secrets = new();
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
    public void ProtectSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return;
        lock (_secretGate)
        {
            if (_secrets.Contains(secret)) return;
            _secrets.Enqueue(secret); while (_secrets.Count > 256) _secrets.Dequeue();
        }
    }
    private string Clean(string? value, int max = 4000)
    {
        string text = value ?? "";
        lock (_secretGate) foreach (string secret in _secrets)
        {
            text = text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            string escaped = JsonSerializer.Serialize(secret)[1..^1];
            if (escaped != secret) text = text.Replace(escaped, "[REDACTED]", StringComparison.Ordinal);
        }
        text = InlineSecret.Replace(text, "$1=[REDACTED]");
        return text.Length <= max ? text : text[..max] + "…";
    }
    private object? Value(string key, object? value)
    {
        if (SensitiveKey.IsMatch(key)) return "[REDACTED]";
        return value switch
        {
            null => null, string text => Clean(text, 2000), Enum e => e.ToString(), Guid g => g.ToString("N"),
            Uri uri => Clean(new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.GetLeftPart(UriPartial.Path)),
            bool or byte or short or int or long or uint or ulong or decimal => value,
            float f => float.IsFinite(f) ? f : null, double d => double.IsFinite(d) ? d : null,
            _ => value.GetType().Name // No serializa objetos arbitrarios que puedan contener credenciales.
        };
    }
    private string Line(DiagnosticLevel level, string code, string message, Exception? exception, IReadOnlyDictionary<string, object?>? context)
    {
        var safe = new Dictionary<string, object?>();
        if (context != null) foreach (var item in context.Take(24)) safe[Clean(item.Key, 100)] = Value(item.Key, item.Value);
        return JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.UtcNow, level = level.ToString(), version = BuildInfo.Version,
            component = _component, session = SessionId, process = Environment.ProcessId,
            code = Clean(code, 120), message = Clean(message), context = safe,
            exception = exception == null ? null : new { type = exception.GetType().FullName,
                message = Clean(exception.Message), stack = Clean(exception.StackTrace, 8000),
                inner = exception.InnerException == null ? null : Clean(exception.InnerException.ToString(), 4000) }
        });
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

    public async Task<string> ExportReportAsync(string reportsDirectory, IReadOnlyDictionary<string, object?>? gameContext = null, CancellationToken token = default)
    {
        await FlushAsync(token).ConfigureAwait(false);
        Directory.CreateDirectory(reportsDirectory);
        string target = Path.Combine(reportsDirectory, $"ArcanaArena-diagnostic-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (string path in Directory.Exists(DirectoryPath) ? Directory.GetFiles(DirectoryPath, "*.jsonl") : [])
        {
            // Solo logs. No copia launcher.cfg, variables de entorno ni archivos del usuario.
            FileStream input;
            try
            {
                input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (input.Length > 64 * 1024 * 1024) { input.Dispose(); continue; }
            }
            catch (IOException) { continue; } // La rotación puede retirar un archivo mientras se exporta.
            using var reading = input;
            using var output = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Fastest).Open();
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        }
        using var metadata = new StreamWriter(archive.CreateEntry("report.json").Open(), new UTF8Encoding(false));
        await metadata.WriteAsync(Line(DiagnosticLevel.Information, "diagnostic.report", "Informe generado por el usuario.", null, gameContext)).ConfigureAwait(false);
        return target;
    }
}

/// <summary>Registra excepciones no controladas sin cambiar la política de terminación del runtime.</summary>
public sealed class ProcessDiagnostics : IDisposable
{
    private readonly DiagnosticLog _log;
    public ProcessDiagnostics(DiagnosticLog log)
    { _log = log; AppDomain.CurrentDomain.UnhandledException += Unhandled; TaskScheduler.UnobservedTaskException += Unobserved; }
    private void Unhandled(object sender, UnhandledExceptionEventArgs e)
    {
        _log.Write(DiagnosticLevel.Critical, "process.unhandled", "Excepción no controlada.", e.ExceptionObject as Exception,
            new Dictionary<string, object?> { ["terminating"] = e.IsTerminating });
        using var timeout = new CancellationTokenSource(2000);
        try { _log.FlushAsync(timeout.Token).GetAwaiter().GetResult(); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }
    private void Unobserved(object? sender, UnobservedTaskExceptionEventArgs e) => _log.Write(DiagnosticLevel.Error, "task.unobserved", "Tarea con excepción no observada.", e.Exception);
    public void Dispose()
    { AppDomain.CurrentDomain.UnhandledException -= Unhandled; TaskScheduler.UnobservedTaskException -= Unobserved; }
}
