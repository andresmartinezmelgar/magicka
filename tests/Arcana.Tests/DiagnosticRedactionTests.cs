using Arcana.Core;
using System.Text.Json;

internal static class DiagnosticRedactionTests
{
    public static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("Logs: secretos y escapes JSON se ocultan al registrarlos desde varios hilos", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-redaction-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var log = new DiagnosticLog(directory, "test");
                await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
                {
                    string secret = $"privado-{index:D3}-á界\"\\\n";
                    log.ProtectSecret(secret);
                    log.ProtectSecret(secret);
                    string escaped = JsonSerializer.Serialize(secret)[1..^1];
                    log.Write(DiagnosticLevel.Information, "redaction", secret + "|" + escaped);
                })));
                await log.FlushAsync();
                var messages = ReadMessages(directory);
                Check(messages.Length == 24 && messages.All(message => message == "[REDACTED]|[REDACTED]"),
                    "Un secreto concurrente o su representación JSON llegó al log.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });

        await test("Logs: duplicar un secreto conserva la retención acotada de los últimos 256", async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-redaction-bound-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using var log = new DiagnosticLog(directory, "test");
                const string first = "fixture-private-first";
                log.ProtectSecret(first);
                for (int index = 0; index < 255; index++) log.ProtectSecret($"fixture-entry-{index:D3}");
                log.ProtectSecret(first);
                log.Write(DiagnosticLevel.Information, "retained", first);
                log.ProtectSecret("fixture-private-new");
                log.Write(DiagnosticLevel.Information, "evicted", first);
                await log.FlushAsync();
                Check(ReadMessages(directory).SequenceEqual(new[] { "[REDACTED]", first }),
                    "Una entrada duplicada cambió el orden o el límite de retención.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    private static string[] ReadMessages(string directory) => Directory.GetFiles(directory, "*.jsonl")
        .Order(StringComparer.Ordinal).SelectMany(File.ReadAllLines).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.GetProperty("message").GetString()!;
        }).ToArray();

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
