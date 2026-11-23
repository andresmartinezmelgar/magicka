using System.Text.Json;

namespace Arcana.Core;

public sealed partial class DiagnosticLog
{
    public void ProtectSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return;
        lock (_secretGate)
        {
            if (_secrets.Any(entry => entry.Value == secret)) return;
            _secrets.Enqueue((secret, JsonSerializer.Serialize(secret)[1..^1]));
            while (_secrets.Count > 256) _secrets.Dequeue();
        }
    }

    private string Clean(string? value, int max = 4000)
    {
        string text = value ?? "";
        lock (_secretGate) foreach (var secret in _secrets)
        {
            text = text.Replace(secret.Value, "[REDACTED]", StringComparison.Ordinal);
            if (secret.Escaped != secret.Value) text = text.Replace(secret.Escaped, "[REDACTED]", StringComparison.Ordinal);
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
}
