using System.Text.Json;

namespace Arcana.Core;

/// <summary>Notas breves de la publicación seleccionada; nunca interpreta HTML ni BBCode.</summary>
public static class ReleaseChanges
{
    public const string Start = "<!-- Arcana-Changes -->", End = "<!-- /Arcana-Changes -->";
    public const int MaximumEntries = 5, MaximumLength = 200;

    public static string[] Read(JsonElement release)
    {
        if (!release.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.String) return [];
        string text = body.GetString() ?? "";
        int start = text.IndexOf(Start, StringComparison.Ordinal);
        if (start >= 0)
        {
            start += Start.Length;
            int end = text.IndexOf(End, start, StringComparison.Ordinal);
            if (end < 0) return [];
            text = text[start..end];
        }
        // Las publicaciones antiguas pueden tener viñetas sin el bloque explícito.
        return text.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => string.Concat(l[2..].Where(c => !char.IsControl(c))).Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Length > MaximumLength ? l[..(MaximumLength - 1)] + "…" : l)
            .Distinct(StringComparer.Ordinal).Take(MaximumEntries).ToArray();
    }
}
