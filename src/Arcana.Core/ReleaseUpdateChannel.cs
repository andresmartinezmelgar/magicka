using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcana.Core;

/// <summary>La sesión autenticada decide el canal. Una caché de perfil sin sesión no habilita pruebas.</summary>
public static class ReleaseUpdateChannel
{
    public const string StableMetadata = "https://api.github.com/repos/andresmartinezmelgar/magicka/releases/latest";
    public const string PreviewMetadata = "https://api.github.com/repos/andresmartinezmelgar/magicka/releases?per_page=20";
    public const string BranchMarker = "Arcana-Preview-Branch: ";
    public static bool PreviewAllowed(string? uid, bool signedIn) => signedIn &&
        (uid == "qRUza2DieeTypFqBIRA5KhbtXoY2" || uid == "hg7F74kCNVQU3tU2itdjWu9SkyS2");

    public static ReleaseUpdatePlan? Select(JsonElement metadata, string? uid, bool signedIn,
        string installed, long installedBuild, bool android)
    {
        if (!PreviewAllowed(uid, signedIn)) return ReleaseUpdatePlan.Read(metadata, installed, android);
        if (metadata.ValueKind != JsonValueKind.Array) return null;
        return metadata.EnumerateArray().Select(r => ReadPreview(r, installed, installedBuild, android) ??
                ReleaseUpdatePlan.Read(r, installed, android))
            .Where(p => p != null).OrderByDescending(p => System.Version.Parse(p!.Version))
            .ThenByDescending(p => p!.BuildNumber).FirstOrDefault();
    }

    private static ReleaseUpdatePlan? ReadPreview(JsonElement release, string installed, long installedBuild, bool android)
    {
        try
        {
            if (release.GetProperty("draft").GetBoolean() || !release.GetProperty("prerelease").GetBoolean()) return null;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            var match = Regex.Match(tag, @"\Apreview-v([0-9]+\.[0-9]+\.[0-9]+)-r([1-9][0-9]{0,17})\z", RegexOptions.CultureInvariant);
            if (!match.Success || !System.Version.TryParse(match.Groups[1].Value, out var version) ||
                version.ToString() != match.Groups[1].Value ||
                !long.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long build) ||
                !System.Version.TryParse(installed.TrimStart('v', 'V'), out var current) || version < current ||
                version == current && build <= installedBuild) return null;
            string target = release.GetProperty("target_commitish").GetString() ?? "";
            if (target.Length == 0 || target is "main" or "refs/heads/main") return null;
            string body = release.GetProperty("body").GetString() ?? "";
            var branches = body.Split('\n').Where(l => l.StartsWith(BranchMarker, StringComparison.Ordinal))
                .Select(l => l[BranchMarker.Length..].TrimEnd('\r')).ToArray();
            if (branches.Length != 1 || branches[0].Length is < 1 or > 200 ||
                branches[0] is "main" or "refs/heads/main" || branches[0].Any(char.IsWhiteSpace) || branches[0].Any(char.IsControl)) return null;
            // Cada compilación tiene su SHA-256; los parches de una versión estable no sirven entre ramas.
            var plan = ReleaseUpdatePlan.ReadAssets(release, tag, version.ToString(), current, android, preferDelta: false);
            return plan == null ? null : plan with { BuildNumber = build, SourceBranch = branches[0] };
        }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { return null; }
    }
}
