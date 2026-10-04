using Arcana.Core;

namespace Arcana.Server;

/// <summary>Conserva los eventos de ASP.NET y del servidor en el mismo registro JSONL.</summary>
public sealed class DiagnosticLoggerProvider(IDiagnosticLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(log, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(IDiagnosticLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var context = new Dictionary<string, object?> { ["category"] = category, ["eventId"] = eventId.Id };
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var value in values) if (value.Key != "{OriginalFormat}") context[value.Key] = value.Value;
            log.Write((DiagnosticLevel)(int)level, eventId.Name ?? category, formatter(state, exception), exception, context);
        }
    }
}
