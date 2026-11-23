namespace Arcana.Core;

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
