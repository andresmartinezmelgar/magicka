using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Arcana.Core;

public sealed record VerifiedReleaseAsset(ReleaseUpdatePlan Plan, string Path, string Manifest, byte[] Hash);

/// <summary>Transporte verificable: reintenta la red sin confundirla con la compatibilidad de un parche.</summary>
public sealed class ReleaseAssetDownloader(HttpClient metadataHttp, HttpClient downloadHttp)
{
    // Cerrar y reabrir el aviso no puede dejar dos tareas escribiendo el mismo parcial.
    private static readonly SemaphoreSlim TransferGate = new(1, 1);
    public const int MaximumAttempts = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public async Task<VerifiedReleaseAsset> DownloadAsync(ReleaseUpdatePlan plan, string folder,
        Action<long, long, double>? progress = null, Action<int>? retrying = null,
        CancellationToken token = default)
    {
        await TransferGate.WaitAsync(token).ConfigureAwait(false);
        try { return await DownloadLockedAsync(plan, folder, progress, retrying, token).ConfigureAwait(false); }
        finally { TransferGate.Release(); }
    }

    private async Task<VerifiedReleaseAsset> DownloadLockedAsync(ReleaseUpdatePlan plan, string folder,
        Action<long, long, double>? progress, Action<int>? retrying, CancellationToken token)
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
        if (plan.Bytes <= 0 || plan.Bytes > ReleaseUpdatePlan.MaximumBytes)
            throw new InvalidDataException("Tamaño de descarga inválido.");
        string target = ReleaseUpdateCache.Target(folder, plan.Filename);
        Directory.CreateDirectory(folder);
        ReleaseUpdateCache.KeepPlan(folder, plan);

        // También se reutiliza un paquete completo ya verificado tras un intento de instalación.
        if (plan.FullPackage is { } full)
        {
            byte[] fullHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, full.Filename);
            string cached = ReleaseUpdateCache.Target(folder, full.Filename);
            if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(cached, full.Bytes, fullHash, token).ConfigureAwait(false))
                return new(full, cached, manifest, fullHash);
        }
        if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(target, plan.Bytes, expectedHash, token).ConfigureAwait(false))
            return new(plan, target, manifest, expectedHash);

        await RetryAsync(async () =>
        {
            string temporary = target + ".part";
            try
            {
                var state = ReleaseUpdateCache.ReadState(target);
                long offset = ReleaseUpdateCache.Matches(state, plan, expectedHash)
                    ? ReleaseUpdateCache.SavedBytes(plan, folder) : 0;
                if (offset == plan.Bytes)
                {
                    if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(temporary, plan.Bytes, expectedHash, token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested();
                        File.Move(temporary, target, overwrite: true);
                        progress?.Invoke(plan.Bytes, plan.Bytes, 0);
                        return true;
                    }
                    ReleaseUpdateCache.DiscardPartial(target);
                    offset = 0;
                }
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
                inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                using var request = Request(plan.DownloadUrl);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    if (EntityTagHeaderValue.TryParse(state?.ETag, out var etag) && !etag.IsWeak)
                        request.Headers.IfRange = new RangeConditionHeaderValue(etag);
                }
                using var response = await downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, inactivity.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
                {
                    ReleaseUpdateCache.DiscardPartial(target);
                    throw new HttpRequestException("El servidor ha rechazado el tramo guardado; se reiniciará la descarga.");
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("La descarga no permite verificar sus rangos de bytes.");
                // Si el servidor ignora Range o cambia la representación, 200 reemplaza el parcial.
                if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range?.Unit != "bytes" || range.From != offset || range.Length != plan.Bytes ||
                        range.To is not { } end || end < offset || end >= plan.Bytes ||
                        response.Content.Headers.ContentLength is { } rangeBytes && rangeBytes != end - offset + 1)
                        throw new InvalidDataException("El tramo descargado no coincide con la actualización guardada.");
                    if (offset > 0 && state?.ETag is { } previous && response.Headers.ETag is { } current &&
                        previous != current.ToString())
                    {
                        ReleaseUpdateCache.DiscardPartial(target);
                        throw new HttpRequestException("La actualización ha cambiado; se reiniciará la descarga.");
                    }
                }
                else throw new InvalidDataException("El servidor no ha entregado un paquete de actualización.");
                long? declared = response.Content.Headers.ContentLength;
                if (response.StatusCode == HttpStatusCode.OK && declared.HasValue && declared.Value != plan.Bytes)
                    throw new InvalidDataException("El tamaño descargado no coincide con la Release.");
                long responseEnd = response.StatusCode == HttpStatusCode.PartialContent
                    ? response.Content.Headers.ContentRange!.To!.Value + 1 : plan.Bytes;
                await using (var input = await response.Content.ReadAsStreamAsync(inactivity.Token).ConfigureAwait(false))
                await using (var output = new FileStream(temporary, offset == 0 ? FileMode.Create : FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    ReleaseUpdateCache.WriteState(target, new(plan.Identity, plan.DownloadUrl, plan.Bytes,
                        Convert.ToHexString(expectedHash), response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null));
                    byte[] buffer = new byte[65536];
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    // El hash incluye todos los bytes conservados, incluso tras terminar el proceso.
                    if (offset > 0)
                    {
                        if (output.Length != offset) throw new InvalidDataException("El parcial ha cambiado durante la reanudación.");
                        int prefix;
                        while ((prefix = await output.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                            hasher.AppendData(buffer, 0, prefix);
                    }
                    long received = offset, lastReported = offset;
                    progress?.Invoke(received, plan.Bytes, 0);
                    var watch = Stopwatch.StartNew();
                    var lastUpdate = TimeSpan.Zero;
                    var lastFlush = TimeSpan.Zero;
                    while (true)
                    {
                        int count;
                        try { count = await input.ReadAsync(buffer, inactivity.Token).ConfigureAwait(false); }
                        catch (IOException error) { throw new HttpRequestException("Se interrumpió la descarga.", error); }
                        if (count == 0) break;
                        inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                        received += count;
                        if (received > responseEnd) throw new InvalidDataException("La descarga supera el tramo publicado.");
                        hasher.AppendData(buffer, 0, count);
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        if (watch.Elapsed - lastFlush >= TimeSpan.FromSeconds(1))
                        {
                            await output.FlushAsync(token).ConfigureAwait(false);
                            lastFlush = watch.Elapsed;
                        }
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
                    {
                        // Un prefijo corrupto se recupera descargando el archivo completo.
                        if (offset > 0) throw new CorruptPartialException();
                        throw new InvalidDataException("El SHA-256 descargado no coincide con GitHub Releases. No se instalará el archivo.");
                    }
                    await output.FlushAsync(token).ConfigureAwait(false);
                    progress?.Invoke(received, plan.Bytes, 0);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, target, overwrite: true);
                return true;
            }
            catch (CorruptPartialException)
            {
                ReleaseUpdateCache.DiscardPartial(target);
                throw new HttpRequestException("El tramo guardado está alterado; se reiniciará la descarga.");
            }
            catch (InvalidDataException) { ReleaseUpdateCache.DiscardPartial(target); throw; }
            // Cancelar, cerrar o perder la conexión conserva el parcial y su identidad en disco.
        }, retrying, token).ConfigureAwait(false);
        return new(plan, target, manifest, expectedHash);
    }

    private sealed class CorruptPartialException : Exception { }

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
