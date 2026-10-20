namespace Arcana.Core;

/// <summary>Último acumulado por cuenta y partida; confirma solo la versión realmente enviada.</summary>
public sealed class BestiaryOutbox(string directory)
{
    private readonly object _gate = new();
    private static string Key(BestiaryProgress p) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(p.Uid + ":" + p.RunId))).ToLowerInvariant();
    private string FileName(BestiaryProgress p) => Path.Combine(directory, Key(p) + ".json");
    public void Enqueue(BestiaryProgress progress)
    {
        if (!BestiaryCatalogue.Valid(progress)) throw new ArgumentException("Registro de bestiario inválido.");
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            string path = FileName(progress);
            if (File.Exists(path))
            {
                var previous = Read(path);
                if (previous != null)
                {
                    var merged = new Dictionary<string, long>(previous.Kills, StringComparer.Ordinal);
                    foreach (var pair in progress.Kills) merged[pair.Key] = Math.Max(merged.GetValueOrDefault(pair.Key), pair.Value);
                    progress = progress with { Kills = merged, ActiveSeconds = Math.Max(progress.ActiveSeconds, previous.ActiveSeconds) };
                }
            }
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(Protocol.SerializeUtf8(progress)); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
    }
    private static BestiaryProgress? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 16384) return null;
            var progress = Protocol.Deserialize<BestiaryProgress>(File.ReadAllText(path));
            return BestiaryCatalogue.Valid(progress) ? progress : null;
        }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException)
        { return null; }
    }
    public BestiaryProgress[] Pending(string? uid = null)
    {
        lock (_gate)
        {
            if (!Directory.Exists(directory)) return [];
            // Recover a complete interrupted write before reading ordinary entries.
            foreach (string temporary in Directory.EnumerateFiles(directory, "*.json.tmp"))
            {
                var progress = Read(temporary);
                if (progress != null && Path.GetFileName(temporary) == Key(progress) + ".json.tmp")
                    File.Move(temporary, temporary[..^4], true);
            }
            return Directory.EnumerateFiles(directory, "*.json").Select(Read)
                .Where(p => p != null && (uid == null || p.Uid == uid)).Cast<BestiaryProgress>().ToArray();
        }
    }
    public void Acknowledge(BestiaryProgress sent)
    {
        lock (_gate)
        {
            string path = FileName(sent);
            if (!File.Exists(path)) return;
            var current = Read(path);
            if (current != null && current.Kills.All(p => p.Value <= sent.Kills.GetValueOrDefault(p.Key)))
                File.Delete(path);
        }
    }
}
