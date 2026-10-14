using System.Text.Json;

namespace Arcana.Core;

/// <summary>Selecciona únicamente el paquete y la integridad de la misma versión y plataforma.</summary>
public sealed record ReleaseUpdatePlan(string Version, string Filename, string DownloadUrl,
    string ChecksumUrl, long Bytes)
{
    public const long MaximumBytes = 2L * 1024 * 1024 * 1024;
    private const string Repository = "andresmartinezmelgar/magicka";

    public static ReleaseUpdatePlan? Read(JsonElement release, string installed, bool android)
    {
        try { return Parse(release, installed, android); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { return null; }
    }

    private static ReleaseUpdatePlan? Parse(JsonElement release, string installed, bool android)
    {
        if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean() ||
            release.TryGetProperty("prerelease", out var preview) && preview.GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        string version = tag.StartsWith('v') ? tag[1..] : tag;
        if (!System.Version.TryParse(version, out var available) || available.Build < 0 ||
            available.Revision >= 0 || version != available.ToString() ||
            !System.Version.TryParse(installed.TrimStart('v', 'V'), out var local) || available <= local) return null;
        string filename = $"ArcanaArena-v{version}-" + (android ? "Android-arm64.apk" : "Windows-x64.zip");
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var package = assets.Where(a => a.GetProperty("name").GetString() == filename).ToArray();
        if (package.Length != 1) return null;
        var checksum = assets.Where(a => a.GetProperty("name").GetString() ==
            (android ? "SHA256SUMS-Android.txt" : "SHA256SUMS-Windows.txt")).ToArray();
        if (checksum.Length == 0)
            checksum = assets.Where(a => a.GetProperty("name").GetString() == "SHA256SUMS.txt").ToArray();
        if (checksum.Length != 1) return null;
        long bytes = package[0].GetProperty("size").GetInt64();
        if (bytes <= 0 || bytes > MaximumBytes) return null;
        string download = package[0].GetProperty("browser_download_url").GetString() ?? "";
        string hashes = checksum[0].GetProperty("browser_download_url").GetString() ?? "";
        string checksumName = checksum[0].GetProperty("name").GetString()!;
        if (!Official(download, tag, filename) || !Official(hashes, tag, checksumName)) return null;
        return new(version, filename, download, hashes, bytes);
    }

    private static bool Official(string url, string tag, string filename) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath == $"/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(filename)}";
}
