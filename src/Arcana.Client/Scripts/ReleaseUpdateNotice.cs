using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Actualizador visual no bloqueante. Descarga la release oficial y muestra progreso real.</summary>
public partial class ReleaseUpdateNotice : Node
{
    private const string Latest = "https://api.github.com/repos/andresmartinezmelgar/magicka/releases/latest";
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024 * 2;
    private static readonly HttpClient MetadataHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _downloadStop;
    private Control? _overlay;
    private Label? _headline, _subtitle, _sizeLabel, _progressLabel, _percentLabel, _speedLabel, _note;
    private ProgressBar? _progress;
    private Button? _primary, _secondary;
    private string _version = "", _downloadUrl = "", _downloadPath = "";
    private long _expectedBytes;
    private bool _downloading, _completed;
    private int _progressGeneration;

    public override void _Ready()
    {
        if (OS.HasFeature("editor") || OS.GetCmdlineUserArgs().Any(a => a.Contains("smoke") || a.Contains("preview"))) return;
        _ = Check();
    }

    private static bool Newer(string remote, string local) =>
        Version.TryParse(remote.TrimStart('v', 'V'), out var available) &&
        Version.TryParse(local.TrimStart('v', 'V'), out var installed) && available > installed;

    private static string Size(long bytes)
    {
        if (bytes < 0) return "Tamaño por determinar";
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} B" : $"{value:0.0} {units[unit]}";
    }

    private async Task Check()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await MetadataHttp.SendAsync(request, _stop.Token);
            if (!response.IsSuccessStatusCode) return;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_stop.Token));
            var release = document.RootElement;
            string version = release.GetProperty("tag_name").GetString() ?? "";
            if (!Newer(version, BuildInfo.Version)) return;
            bool android = OS.HasFeature("android");
            string expected = android ? "-Android-arm64.apk" : "-Windows-x64.zip";
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(expected, StringComparison.OrdinalIgnoreCase)) continue;
                string? candidate = asset.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                long size = asset.TryGetProperty("size", out var field) && field.TryGetInt64(out long parsed) ? parsed : -1;
                if (size > MaximumDownloadBytes) return;
                Callable.From(() => ShowNotice(version, candidate!, size)).CallDeferred();
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Sin red, se puede seguir jugando. */ }
    }

    private static PanelContainer Card()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0b172a"), 18, new Color("44617e")));
        return panel;
    }

    private static void AddText(VBoxContainer parent, Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
    }

    private void ShowNotice(string version, string download, long size)
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested || _overlay != null) return;
        _version = version;
        _downloadUrl = download;
        _expectedBytes = size;
        _overlay = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = Ui.Theme() };
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_overlay);

        var dim = new ColorRect { Color = new Color(0.015f, 0.03f, 0.055f, 0.94f), MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(dim);
        Ui.Full(dim);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(center);
        Ui.Full(center);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(Ui.IsMobile ? 520 : 550, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        center.AddChild(scroll);
        var card = Card();
        card.CustomMinimumSize = new Vector2(Ui.IsMobile ? 510 : 530, 0);
        scroll.AddChild(card);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", Ui.IsMobile ? 15 : 12);
        card.AddChild(body);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        body.AddChild(top);
        top.AddChild(Ui.Label("✦", Ui.IsMobile ? 34 : 30, Ui.Gold));
        var topCopy = new VBoxContainer();
        topCopy.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(topCopy);
        AddText(topCopy, Ui.Label("ARCANA ARENA", 15, Ui.Gold));
        _headline = Ui.Label("Una nueva versión te espera", Ui.IsMobile ? 24 : 25, Ui.Text);
        AddText(topCopy, _headline);

        _subtitle = Ui.Label("Mejoras y novedades disponibles para tu aventura.", 16, Ui.Muted);
        AddText(body, _subtitle);

        var versions = new HBoxContainer();
        versions.AddThemeConstantOverride("separation", 12);
        body.AddChild(versions);
        var installed = Ui.Label("INSTALADA\n" + BuildInfo.Version, 16, Ui.Muted);
        installed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(installed);
        versions.AddChild(Ui.Label("➜", 24, Ui.Gold));
        var available = Ui.Label("DISPONIBLE\n" + version, 16, Ui.Gold);
        available.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(available);

        var rule = new HSeparator();
        body.AddChild(rule);
        _sizeLabel = Ui.Label(size >= 0 ? "Tamaño de descarga  ·  " + Size(size) : "Tamaño de descarga  ·  Por determinar", 17, Ui.Text);
        AddText(body, _sizeLabel);

        var progressHeader = new HBoxContainer();
        body.AddChild(progressHeader);
        _progressLabel = Ui.Label(size >= 0 ? "0 B de " + Size(size) : "Preparado para descargar", 16, Ui.Muted);
        _progressLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        progressHeader.AddChild(_progressLabel);
        _percentLabel = Ui.Label("0 %", 17, Ui.Gold);
        progressHeader.AddChild(_percentLabel);

        _progress = new ProgressBar { MinValue = 0, MaxValue = 100, Value = 0, ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, Ui.IsMobile ? 20 : 16) };
        body.AddChild(_progress);
        _speedLabel = Ui.Label("La descarga se realizará desde GitHub Releases.", 15, Ui.Muted);
        AddText(body, _speedLabel);

        _note = Ui.Label(OS.HasFeature("android")
            ? "Al terminar, podrás abrir el instalador de Android y confirmar la actualización."
            : "Al terminar, cierra el juego y extrae el ZIP para sustituir la versión anterior.", 15, Ui.Muted);
        AddText(body, _note);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 12);
        body.AddChild(actions);
        _secondary = Ui.Button("Ahora no", Close);
        _secondary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_secondary);
        _primary = Ui.Button("Descargar actualización", PrimaryAction, true);
        _primary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_primary);
    }

    private void PrimaryAction()
    {
        if (_completed) OpenDownloaded();
        else StartDownload();
    }

    private void StartDownload()
    {
        if (_downloading || _completed || _stop.IsCancellationRequested) return;
        _downloading = true;
        _downloadStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _primary!.Disabled = true;
        _primary.Text = "Descargando…";
        _secondary!.Text = "Cancelar descarga";
        _headline!.Text = "Descargando actualización";
        _subtitle!.Text = "Puedes comprobar el progreso en tiempo real.";
        _speedLabel!.Text = "Conectando con GitHub…";
        _ = DownloadUpdate(_downloadUrl, _downloadStop.Token, ++_progressGeneration);
    }

    private async Task DownloadUpdate(string url, CancellationToken token, int generation)
    {
        string? temporary = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.0");
            using var response = await DownloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long? headerLength = response.Content.Headers.ContentLength;
            if (headerLength > MaximumDownloadBytes) throw new InvalidDataException("La actualización supera el límite de 2 GB.");
            long expected = headerLength is > 0 ? headerLength.Value : _expectedBytes;
            string extension = OS.HasFeature("android") ? ".apk" : ".zip";
            string folder = System.IO.Path.Combine(OS.GetUserDataDir(), "updates");
            System.IO.Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, "ArcanaArena-" + _version + extension);
            temporary = target + ".part";

            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = System.IO.File.Create(temporary))
            {
                byte[] buffer = new byte[65536];
                long received = 0, lastReported = 0;
                var watch = Stopwatch.StartNew();
                var lastUpdate = TimeSpan.Zero;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) != 0)
                {
                    received += count;
                    if (received > MaximumDownloadBytes) throw new InvalidDataException("Descarga demasiado grande.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if (watch.Elapsed - lastUpdate >= TimeSpan.FromMilliseconds(180))
                    {
                        double elapsed = Math.Max(0.001, (watch.Elapsed - lastUpdate).TotalSeconds);
                        double speed = (received - lastReported) / elapsed;
                        long current = received;
                        Callable.From(() => UpdateProgress(generation, current, expected, speed)).CallDeferred();
                        lastUpdate = watch.Elapsed;
                        lastReported = received;
                    }
                }
                await output.FlushAsync(token);
                if (headerLength is > 0 && received != headerLength.Value)
                    throw new InvalidDataException("La descarga está incompleta.");
                Callable.From(() => UpdateProgress(generation, received, expected, 0)).CallDeferred();
            }
            token.ThrowIfCancellationRequested();
            System.IO.File.Move(temporary, target, true);
            temporary = null;
            Callable.From(() => ShowDownloaded(generation, target)).CallDeferred();
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Callable.From(() => ShowFailure(generation, error.Message)).CallDeferred();
        }
        finally
        {
            if (temporary != null)
            {
                try { System.IO.File.Delete(temporary); } catch (IOException) { }
            }
        }
    }

    private void UpdateProgress(int generation, long downloaded, long total, double bytesPerSecond)
    {
        if (generation != _progressGeneration || !_downloading || !IsInstanceValid(_overlay)) return;
        bool known = total > 0;
        double fraction = known ? Math.Clamp((double)downloaded / total, 0, 1) : 0;
        _progress!.Indeterminate = !known;
        if (known) _progress.Value = fraction * 100;
        _percentLabel!.Text = known ? $"{fraction * 100:0} %" : "…";
        _progressLabel!.Text = Size(downloaded) + (known ? " de " + Size(total) : " descargados");
        _speedLabel!.Text = bytesPerSecond > 0 ? Size((long)bytesPerSecond) + "/s" : "Finalizando descarga…";
    }

    private void ShowDownloaded(int generation, string path)
    {
        if (generation != _progressGeneration || !IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        _downloading = false;
        _completed = true;
        _downloadPath = path;
        _downloadStop?.Dispose();
        _downloadStop = null;
        _progress!.Indeterminate = false;
        _progress.Value = 100;
        _percentLabel!.Text = "100 %";
        _headline!.Text = "¡Actualización descargada!";
        _subtitle!.Text = "La nueva versión ya está guardada en tu dispositivo.";
        _speedLabel!.Text = "Descarga completada";
        _note!.Text = OS.HasFeature("android")
            ? "Abre el APK descargado con el instalador de Android para actualizar. Si Android no permite abrirlo directamente, búscalo en la carpeta de datos de la aplicación."
            : "Cierra Arcana Arena, extrae el archivo ZIP y sustituye la carpeta de la versión anterior.";
        _secondary!.Text = "Volver al juego";
        _primary!.Disabled = false;
        _primary.Text = OS.HasFeature("android") ? "Abrir APK" : "Abrir archivo ZIP";
    }

    private void ShowFailure(int generation, string reason)
    {
        if (generation != _progressGeneration || !IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        _downloading = false;
        _downloadStop?.Dispose();
        _downloadStop = null;
        _headline!.Text = "No se ha completado la descarga";
        _subtitle!.Text = reason.Length > 140 ? reason[..140] + "…" : reason;
        _speedLabel!.Text = "Comprueba la conexión y vuelve a intentarlo.";
        _primary!.Disabled = false;
        _primary.Text = "Reintentar";
        _secondary!.Text = "Ahora no";
        _primary.Pressed -= StartDownload;
        _primary.Pressed += RetryDownload;
    }

    private void RetryDownload()
    {
        _primary!.Pressed -= RetryDownload;
        _primary.Pressed += StartDownload;
        StartDownload();
    }

    private void Close()
    {
        if (_downloading)
        {
            _progressGeneration++;
            _downloadStop?.Cancel();
            _downloading = false;
        }
        if (IsInstanceValid(_overlay)) _overlay!.QueueFree();
        _overlay = null;
    }

    private void OpenDownloaded()
    {
        if (_downloadPath.Length == 0) return;
        var result = OS.ShellOpen(_downloadPath);
        if (result != Error.Ok)
        {
            _note!.Text = "El sistema no ha podido abrir el archivo directamente. Se guardó en: " + _downloadPath;
        }
    }

    public override void _ExitTree()
    {
        _stop.Cancel();
        _downloadStop?.Cancel();
        _downloadStop?.Dispose();
        _stop.Dispose();
    }
}
