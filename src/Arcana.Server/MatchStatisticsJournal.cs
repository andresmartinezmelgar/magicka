using System.Security.Cryptography;
using System.Text;
using Arcana.Core;

namespace Arcana.Server;

/// <summary>Cola temporal compartida: el siguiente proceso continúa las entregas pendientes.</summary>
internal sealed class MatchStatisticsJournal(string directory) : ISharedMatchJournal
{
    private string FileFor(MatchStatisticsReport report) => Path.Combine(directory,
        report.GameVersion + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(report.RunId))).ToLowerInvariant() + ".json");
    private static bool Supported(string path)
    {
        string name = Path.GetFileName(path); int dash = name.IndexOf('-');
        return dash < 0 || !Version.TryParse(name[..dash], out var version) || version <= Version.Parse(BuildInfo.Version);
    }
    private static MatchStatisticsReport Read(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Informe demasiado grande.");
        return MatchStatisticsReport.Parse(File.ReadAllText(path));
    }
    private MatchStatisticsReport? ReadSafely(string path)
    {
        try
        {
            var report = Read(path);
            string legacy = Path.Combine(directory, report.RunId + ".json");
            if (path != FileFor(report) && path != FileFor(report) + ".tmp" && path != legacy && path != legacy + ".tmp") throw new InvalidDataException("Nombre de informe inválido.");
            return report;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidDataException or ArgumentException or FormatException)
        {
            File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
            Diagnostics.Warn("statistics.invalid_journal", "Informe dañado conservado para revisión.", error);
            return null;
        }
    }
    private static bool Supersedes(MatchStatisticsReport incoming, MatchStatisticsReport saved) =>
        incoming.Network != saved.Network ? incoming.Network : MatchStatisticsReport.IsNewer(incoming, saved);
    public void Persist(MatchStatisticsReport report)
    {
        using var lease = new SharedJournalLease(directory);
        string file = FileFor(report);
        if (File.Exists(file) && ReadSafely(file) is { } previous && !Supersedes(report, previous)) return;
        string temporary = file + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { stream.Write(Encoding.UTF8.GetBytes(report.ToJson())); stream.Flush(true); }
        File.Move(temporary, file, true);
    }
    public MatchStatisticsReport[] Pending()
    {
        using var lease = new SharedJournalLease(directory);
        // Al instalar la primera generación se heredan también los diarios de la versión anterior.
        var incomplete = Directory.EnumerateFiles(directory, "*.json.tmp").Where(Supported);
        var legacy = Directory.EnumerateFiles(directory, "*.json")
            .Where(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _));
        foreach (string temporary in incomplete.Concat(legacy).Take(64).ToArray())
        {
            var report = ReadSafely(temporary); if (report == null) continue;
            string file = FileFor(report);
            if (!File.Exists(file) || ReadSafely(file) is not { } previous || Supersedes(report, previous)) File.Move(temporary, file, true);
            else File.Delete(temporary);
        }
        return Directory.EnumerateFiles(directory, "*.json").Where(Supported).Take(64).Select(ReadSafely).Where(r => r != null).Cast<MatchStatisticsReport>().ToArray();
    }
    public void Acknowledge(MatchStatisticsReport sent)
    {
        using var lease = new SharedJournalLease(directory);
        string file = FileFor(sent);
        if (File.Exists(file) && ReadSafely(file) is { } current && !Supersedes(current, sent)) File.Delete(file);
    }
}
