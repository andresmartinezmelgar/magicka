using System.Net.Http;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class ReleaseUpdateNotice
{
    private void PrimaryAction()
    {
        if (_completed) _ = InstallDownloaded();
        else StartDownload();
    }

    private void ShowSavedDownload()
    {
        long saved = _activePlan == null ? 0 : ReleaseUpdateCache.SavedBytes(_activePlan, _updatesFolder);
        if (saved <= 0 || _expectedBytes <= 0) return;
        double percent = 100d * saved / _expectedBytes;
        _progress!.Value = percent;
        _percentLabel!.Text = $"{percent:0} %";
        _progressLabel!.Text = Size(saved) + " de " + Size(_expectedBytes);
        _primary!.Text = "Continuar descarga";
        _speedLabel!.Text = "Descarga guardada. Puedes continuar desde este punto.";
    }

    private void StartDownload()
    {
        if (_downloading || _completed || _stop.IsCancellationRequested || !CurrentIdentity) return;
        _downloading = true;
        _downloadStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _downloadStop.CancelAfter(TimeSpan.FromMinutes(10));
        _primary!.Disabled = true;
        _primary.Text = "Descargando…";
        _progress!.Indeterminate = false;
        _progress.Value = 0;
        _percentLabel!.Text = "0 %";
        _progressLabel!.Text = _expectedBytes > 0 ? "0 B de " + Size(_expectedBytes) : "0 B descargados";
        _secondary!.Text = "Pausar descarga";
        _headline!.Text = "Descargando actualización";
        _subtitle!.Text = _activePlan?.Differential == true ? "Solo se descargan los cambios compatibles con tu versión." :
            "Descargando el paquete completo de esta versión.";
        _speedLabel!.Text = "Conectando con GitHub…";
        ShowSavedDownload();
        _primary.Text = "Descargando…";
        _ = DownloadUpdate(_downloadUrl, _downloadStop.Token, ++_progressGeneration);
    }

    private async Task DownloadUpdate(string url, CancellationToken token, int generation)
    {
        var plan = _activePlan ?? new ReleaseUpdatePlan(_version, _assetName, url, _checksumUrl, _expectedBytes);
        try
        {
            var asset = await Downloader.DownloadAsync(plan, _updatesFolder,
                (current, expected, speed) => Callable.From(() => UpdateProgress(generation, current, expected, speed)).CallDeferred(),
                attempt => Callable.From(() => ShowRetry(generation, attempt)).CallDeferred(), token);
            try { await CompleteTransfer(asset.Plan, asset.Path, asset.Manifest, asset.Hash, token, generation); }
            catch (Exception error) when (asset.Plan.Differential && error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Diagnostics.Warn("update.delta_failed", "No se ha podido aplicar el parche; se usará el paquete completo.", error);
                Callable.From(() => FallbackToFull(generation)).CallDeferred();
            }
        }
        catch (OperationCanceledException)
        {
            if (!_stop.IsCancellationRequested)
                Callable.From(() => ShowFailure(generation, "Se ha agotado el tiempo de descarga. Puedes volver a intentarlo.")).CallDeferred();
        }
        catch (Exception error)
        {
            Diagnostics.Warn("update.download_failed", "No se ha podido descargar y verificar la actualización.", error);
            Callable.From(() => ShowFailure(generation, error.Message)).CallDeferred();
        }
    }

    private void ShowRetry(int generation, int attempt)
    {
        if (generation != _progressGeneration || !_downloading || !GodotObject.IsInstanceValid(_overlay)) return;
        _subtitle!.Text = "La conexión se ha interrumpido. Se reintentará automáticamente.";
        _speedLabel!.Text = $"Reconectando · intento {attempt} de {ReleaseAssetDownloader.MaximumAttempts}";
    }

    private async Task CompleteTransfer(ReleaseUpdatePlan plan, string path, string manifest, byte[] transportHash,
        CancellationToken token, int generation)
    {
        byte[] installedHash = transportHash;
        bool windowsDelta = plan.Differential && !_android;
        if (plan.Differential && _android)
        {
            var full = plan.FullPackage!;
            installedHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, full.Filename);
            string destination = System.IO.Path.Combine(_updatesFolder, full.Filename);
            if (!await ReleaseDownloadVerifier.IsVerifiedFileAsync(destination, full.Bytes, installedHash, token))
            {
                Callable.From(() => {
                    if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay)) return;
                    _headline!.Text = "Reconstruyendo actualización";
                    _subtitle!.Text = "Se están reutilizando los datos de la APK instalada.";
                    _progress!.Indeterminate = true;
                }).CallDeferred();
                byte[] targetHash = installedHash;
                await Task.Run(() => BinaryUpdateDelta.Apply(_androidBasePath, path, destination, BuildInfo.Version,
                    full.Version, targetHash, full.Bytes, token), token);
            }
            try { System.IO.File.Delete(path); } catch (IOException) { }
            ReleaseUpdateCache.DeleteDownloadedMetadata(path);
            path = destination;
        }
        token.ThrowIfCancellationRequested();
        string completedPath = path, hash = Convert.ToHexString(installedHash);
        Callable.From(() => ShowDownloaded(generation, completedPath, hash, windowsDelta)).CallDeferred();
    }

    private void FallbackToFull(int generation)
    {
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        var full = _activePlan?.FullPackage;
        if (full == null) { ShowFailure(generation, "No se ha podido recuperar el paquete completo."); return; }
        string folder = _updatesFolder;
        try {
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "skip-delta-" + full.Version + ".txt"), "incompatible");
            // Un rechazo antes del cierre del cliente no es un fallo de la instalación completa.
            System.IO.File.Delete(System.IO.Path.Combine(folder, "last-failure.json"));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        _downloadStop?.Cancel(); _downloadStop?.Dispose(); _downloadStop = null;
        _activePlan = full;
        _downloadUrl = full.DownloadUrl; _checksumUrl = full.ChecksumUrl; _assetName = full.Filename;
        _expectedBytes = full.Bytes;
        _downloading = _completed = _installing = _windowsDelta = false;
        _secondary!.Disabled = false;
        _sizeLabel!.Text = "Tamaño de descarga · " + Size(full.Bytes);
        _note!.Text = "El parche no es compatible. Se instalará automáticamente el paquete completo.";
        StartDownload();
    }

    private void UpdateProgress(int generation, long downloaded, long total, double bytesPerSecond)
    {
        if (generation != _progressGeneration || !_downloading || !GodotObject.IsInstanceValid(_overlay)) return;
        bool known = total > 0;
        double fraction = known ? Math.Clamp((double)downloaded / total, 0, 1) : 0;
        _progress!.Indeterminate = !known;
        if (known) _progress.Value = fraction * 100;
        _percentLabel!.Text = known ? $"{fraction * 100:0} %" : "…";
        _progressLabel!.Text = Size(downloaded) + (known ? " de " + Size(total) : " descargados");
        _speedLabel!.Text = bytesPerSecond > 0 ? Size((long)bytesPerSecond) + "/s" : "Finalizando descarga…";
    }

    private void ShowDownloaded(int generation, string path, string hash, bool windowsDelta)
    {
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested || !CurrentIdentity) return;
        _downloading = false;
        _completed = true;
        _downloadPath = path;
        _expectedHash = hash;
        _windowsDelta = windowsDelta;
        _downloadStop?.Dispose();
        _downloadStop = null;
        _progress!.Indeterminate = false;
        _progress.Value = 100;
        _percentLabel!.Text = "100 %";
        _primary!.Disabled = false;
        _primary.Text = "Instalar actualización";
        _ = InstallDownloaded();
    }

    private void ShowFailure(int generation, string reason)
    {
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        Diagnostics.Warn("update.failed", reason, context: new Dictionary<string, object?>
        { ["version"] = _version, ["asset"] = _assetName, ["phase"] = _completed ? "installation" : "download" });
        _downloading = false;
        _downloadStop?.Dispose();
        _downloadStop = null;
        _installing = false;
        _secondary!.Disabled = false;
        _headline!.Text = _completed ? "No se ha completado la instalación" : "No se ha completado la descarga";
        _subtitle!.Text = reason.Length > 140 ? reason[..140] + "…" : reason;
        _speedLabel!.Text = "La versión actual sigue disponible.";
        _primary!.Disabled = false;
        _primary.Text = "Reintentar";
        _secondary!.Text = "Ahora no";
    }
}
