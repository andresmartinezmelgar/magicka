namespace Arcana.Core;

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
