using System.Net.Http;
using System.Text.Json;

namespace Arcana.Core;

public sealed record ReleaseUpdateQueryResult(bool Succeeded, ReleaseUpdatePlan? Update);

/// <summary>Consulta acotada y sin descargas; distingue falta de novedades de fallos de red o metadatos.</summary>
public static class ReleaseUpdateQuery
{
    public static async Task<ReleaseUpdateQueryResult> CheckAsync(HttpClient http, string identity, string installed,
        long installedBuild, bool android, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            bool preview = ReleaseUpdateChannel.PreviewAllowed(identity, identity.Length > 0);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                preview ? ReleaseUpdateChannel.PreviewMetadata : ReleaseUpdateChannel.StableMetadata);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.3");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.CacheControl = new() { NoCache = true };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(false, null);
            using var document = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content,
                preview ? 1024 * 1024 : 256 * 1024, cancellationToken).ConfigureAwait(false));
            var root = document.RootElement;
            if (preview ? root.ValueKind != JsonValueKind.Array : root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return new(false, null);
            return new(true, ReleaseUpdateChannel.Select(root, identity, identity.Length > 0, installed, installedBuild, android));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        { return new(false, null); }
    }
}
