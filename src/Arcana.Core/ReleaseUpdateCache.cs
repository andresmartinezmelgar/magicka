using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcana.Core;

/// <summary>Conserva una descarga identificada y retira únicamente paquetes del actualizador.</summary>
public static class ReleaseUpdateCache
{
    internal sealed record DownloadState(string Identity, string Url, long Bytes, string Hash, string? ETag);
    private static readonly Regex PackageName = new(@"\A(?<asset>ArcanaArena-v(?<version>[0-9]+\.[0-9]+\.[0-9]+)-(?:Android-arm64|Windows-x64)(?:-from-v[0-9]+\.[0-9]+\.[0-9]+)?\.(?:apk|zip|delta))(?<suffix>\.(?:part(?:-[a-fA-F0-9]{32})?|download\.json(?:\.tmp)?))?\z", RegexOptions.CultureInvariant);

    internal static string Target(string folder, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename is "." or ".." || filename.Length > 180 ||
            filename.Any(c => char.IsControl(c) || "/\\<>:\"|?*".Contains(c)))
            throw new InvalidDataException("Nombre de descarga inválido.");
        return Path.Combine(folder, filename);
    }

    internal static DownloadState? ReadState(string target)
    {
        try
        {
            string path = target + ".download.json";
            if (new FileInfo(path).Length > 8192) return null;
            var state = JsonSerializer.Deserialize<DownloadState>(File.ReadAllText(path));
            return state is { Identity.Length: > 0, Url.Length: > 0, Bytes: > 0, Hash.Length: 64 } && state.Bytes <= ReleaseUpdatePlan.MaximumBytes &&
                state.Hash.All(Uri.IsHexDigit) ? state : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static bool Matches(DownloadState? state, ReleaseUpdatePlan plan, byte[]? hash = null) =>
        state != null && state.Identity == plan.Identity && state.Url == plan.DownloadUrl && state.Bytes == plan.Bytes &&
        (hash == null || state.Hash.Equals(Convert.ToHexString(hash), StringComparison.OrdinalIgnoreCase));

    internal static void WriteState(string target, DownloadState state)
    {
        string path = target + ".download.json";
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static void DiscardPartial(string target)
    {
        Delete(target + ".part");
        DeleteDownloadedMetadata(target);
    }

    public static void DeleteDownloadedMetadata(string target)
    {
        Delete(target + ".download.json");
        Delete(target + ".download.json.tmp");
    }

    public static long SavedBytes(ReleaseUpdatePlan plan, string folder)
    {
        try
        {
            string target = Target(folder, plan.Filename);
            if (!Matches(ReadState(target), plan)) return 0;
            long size = new FileInfo(target + ".part").Length;
            return size > 0 && size <= plan.Bytes ? size : 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Se llama al arrancar: una instalación nueva ya no necesita sus APK, parches o temporales.</summary>
    public static void CleanInstalled(string folder, string installedVersion, long installedBuild = 0)
    {
        if (!System.Version.TryParse(installedVersion, out var installed)) return;
        Clean(folder, (asset, version) =>
        {
            if (version > installed) return true;
            if (version < installed) return false;
            // Las pruebas pueden actualizar la misma versión base con otro número de compilación.
            var state = ReadState(Path.Combine(folder, asset));
            string prefix = $"preview:{version}:";
            return state != null && state.Identity.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(state.Identity[prefix.Length..], out long build) && build > installedBuild;
        }, version => version > installed);
    }

    /// <summary>Solo se retiene el destino ofrecido, su paquete completo y su marca de incompatibilidad.</summary>
    public static void KeepPlan(string folder, ReleaseUpdatePlan plan)
    {
        Clean(folder, (asset, _) => asset == plan.Filename || asset == plan.FullPackage?.Filename,
            version => version.ToString() == plan.Version);
    }

    private static void Clean(string folder, Func<string, System.Version, bool> keep, Func<System.Version, bool> keepMarker)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(path);
                var match = PackageName.Match(name);
                if (match.Success && System.Version.TryParse(match.Groups["version"].Value, out var version))
                {
                    string asset = match.Groups["asset"].Value, suffix = match.Groups["suffix"].Value;
                    // Los temporales antiguos con GUID y las escrituras incompletas no se pueden reanudar.
                    if (suffix.StartsWith(".part-", StringComparison.Ordinal) || suffix.EndsWith(".tmp", StringComparison.Ordinal) ||
                        !keep(asset, version) || suffix == ".part" && ReadState(Path.Combine(folder, asset)) == null)
                        Delete(path);
                }
                else if (name.StartsWith("skip-delta-", StringComparison.Ordinal) && name.EndsWith(".txt", StringComparison.Ordinal) &&
                    System.Version.TryParse(name[11..^4], out var markerVersion) && !keepMarker(markerVersion)) Delete(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
