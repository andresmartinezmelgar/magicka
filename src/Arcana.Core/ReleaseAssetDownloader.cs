using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Arcana.Core;

public sealed record VerifiedReleaseAsset(ReleaseUpdatePlan Plan, string Path, string Manifest, byte[] Hash);

/// <summary>Transporte verificable: reintenta la red sin confundirla con la compatibilidad de un parche.</summary>
public sealed class ReleaseAssetDownloader(HttpClient metadataHttp, HttpClient downloadHttp)
{
    public const int MaximumAttempts = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public async Task<VerifiedReleaseAsset> DownloadAsync(ReleaseUpdatePlan plan, string folder,
        Action<long, long, double>? progress = null, Action<int>? retrying = null,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(plan.ChecksumUrl) || string.IsNullOrWhiteSpace(plan.Filename))
            throw new InvalidDataException("Esta versión no incluye un manifiesto de integridad verificable.");
        string manifest = await RetryAsync(async () =>
        {
            using var request = Request(plan.ChecksumUrl);
            using var response = await metadataHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try { return await RoomBrowser.ReadBoundedAsync(response.Content, 64 * 1024, timeout.Token).ConfigureAwait(false); }
            catch (IOException error) { throw new HttpRequestException("Se interrumpió la lectura del manifiesto.", error); }
        }, retrying, token).ConfigureAwait(false);
        byte[] expectedHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, plan.Filename);
        if (plan.Filename is "." or "..") throw new InvalidDataException("Nombre de descarga inválido.");
        Directory.CreateDirectory(folder);

        // También se reutiliza un paquete completo ya verificado tras un intento de instalación.
        if (plan.FullPackage is { } full)
        {
            byte[] fullHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, full.Filename);
            string cached = Path.Combine(folder, full.Filename);
            if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(cached, full.Bytes, fullHash, token).ConfigureAwait(false))
                return new(full, cached, manifest, fullHash);
        }
        string target = Path.Combine(folder, plan.Filename);
        if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(target, plan.Bytes, expectedHash, token).ConfigureAwait(false))
            return new(plan, target, manifest, expectedHash);

        await RetryAsync(async () =>
        {
            // Cada intento posee su temporal: cancelar uno nunca borra el del siguiente.
            string temporary = target + ".part-" + Guid.NewGuid().ToString("N");
            try
            {
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
                inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                using var request = Request(plan.DownloadUrl);
                using var response = await downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, inactivity.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                long? declared = response.Content.Headers.ContentLength;
                if (plan.Bytes <= 0 || plan.Bytes > ReleaseUpdatePlan.MaximumBytes ||
                    declared.HasValue && declared.Value != plan.Bytes)
                    throw new InvalidDataException("El tamaño descargado no coincide con la Release.");
                await using (var input = await response.Content.ReadAsStreamAsync(inactivity.Token).ConfigureAwait(false))
                await using (var output = File.Create(temporary))
                {
                    byte[] buffer = new byte[65536];
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long received = 0, lastReported = 0;
                    var watch = Stopwatch.StartNew();
                    var lastUpdate = TimeSpan.Zero;
                    while (true)
                    {
                        int count;
                        try { count = await input.ReadAsync(buffer, inactivity.Token).ConfigureAwait(false); }
                        catch (IOException error) { throw new HttpRequestException("Se interrumpió la descarga.", error); }
                        if (count == 0) break;
                        inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                        received += count;
                        if (received > plan.Bytes) throw new InvalidDataException("La descarga supera el tamaño publicado.");
                        hasher.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        if (watch.Elapsed - lastUpdate >= TimeSpan.FromMilliseconds(180))
                        {
                            double seconds = Math.Max(0.001, (watch.Elapsed - lastUpdate).TotalSeconds);
                            progress?.Invoke(received, plan.Bytes, (received - lastReported) / seconds);
                            lastReported = received;
                            lastUpdate = watch.Elapsed;
                        }
                    }
                    if (received != plan.Bytes) throw new HttpRequestException("La conexión terminó antes de completar la descarga.");
                    if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectedHash))
                        throw new InvalidDataException("El SHA-256 descargado no coincide con GitHub Releases. No se instalará el archivo.");
                    await output.FlushAsync(token).ConfigureAwait(false);
                    progress?.Invoke(received, plan.Bytes, 0);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
                return true;
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }, retrying, token).ConfigureAwait(false);
        return new(plan, target, manifest, expectedHash);
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> operation, Action<int>? retrying, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return await operation().ConfigureAwait(false); }
            catch (Exception error) when (!token.IsCancellationRequested && attempt < MaximumAttempts && Transient(error))
            {
                retrying?.Invoke(attempt + 1);
                await Task.Delay(RetryDelay * attempt, token).ConfigureAwait(false);
            }
        }
    }

    private static bool Transient(Exception error) => error is OperationCanceledException ||
        error is HttpRequestException http && (http.StatusCode == null ||
            http.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound or HttpStatusCode.Forbidden ||
            (int)http.StatusCode.Value >= 500);

    private static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.1");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        return request;
    }
}
