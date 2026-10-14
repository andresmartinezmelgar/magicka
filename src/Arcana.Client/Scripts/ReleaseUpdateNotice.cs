using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Actualizador visual no bloqueante. Descarga la release oficial y muestra progreso real.</summary>
public partial class ReleaseUpdateNotice : Node
{
    private const string Latest = "https://api.github.com/repos/andresmartinezmelgar/magicka/releases/latest";
    private const long MaximumDownloadBytes = 1024L * 1024 * 1024 * 2;
    private static readonly System.Net.Http.HttpClient MetadataHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly System.Net.Http.HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _downloadStop;
    private CanvasLayer? _canvas;
    private Control? _overlay;
    private PanelContainer? _responsiveCard;
    private GridContainer? _responsiveButtons;
    private Label? _headline, _subtitle, _sizeLabel, _progressLabel, _percentLabel, _speedLabel, _note;
    private ProgressBar? _progress;
    private Button? _primary, _secondary;
    private string _version = "", _downloadUrl = "", _downloadPath = "", _checksumUrl = "", _assetName = "";
    private long _expectedBytes;
    private bool _downloading, _completed;
    private int _progressGeneration;

    public override void _Ready()
    {
        GetViewport().SizeChanged += ResizeUpdateCard;
        if (OS.GetCmdlineUserArgs().Contains("--update-ui-smoke"))
        {
            Callable.From(RunUpdateLayoutSmoke).CallDeferred();
            return;
        }
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
                var checksums = release.GetProperty("assets").EnumerateArray()
                    .Select(item => new {
                        Name = item.GetProperty("name").GetString() ?? "",
                        Url = item.GetProperty("browser_download_url").GetString() ?? ""
                    }).FirstOrDefault(item =>
                        item.Name == (android ? "SHA256SUMS-Android.txt" : "SHA256SUMS-Windows.txt"))
                    ?? release.GetProperty("assets").EnumerateArray()
                        .Select(item => new {
                            Name = item.GetProperty("name").GetString() ?? "",
                            Url = item.GetProperty("browser_download_url").GetString() ?? ""
                        }).FirstOrDefault(item => item.Name == "SHA256SUMS.txt");
                if (checksums == null || !Uri.TryCreate(checksums.Url, UriKind.Absolute, out var checksumUri) ||
                    checksumUri.Scheme != Uri.UriSchemeHttps ||
                    !checksumUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return;
                Callable.From(() => ShowNotice(version, candidate!, size, checksums.Url, name)).CallDeferred();
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

    private void ShowNotice(string version, string download, long size,
        string checksumUrl = "", string assetName = "")
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested || _overlay != null) return;
        _version = version;
        _downloadUrl = download;
        _checksumUrl = checksumUrl;
        _assetName = assetName;
        _expectedBytes = size;
        _canvas = new CanvasLayer { Layer = 90 };
        AddChild(_canvas);
        // Ocultar mientras se construye: un fallo intermedio nunca deja un fondo
        // oscuro bloqueando el menú sin botones utilizables.
        _overlay = new Control
        {
            Name = "ReleaseUpdateOverlay",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Theme = Ui.Theme()
        };
        _canvas.AddChild(_overlay);
        _overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0.015f, 0.03f, 0.055f, 0.94f), MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(dim);
        Ui.Full(dim);

        var center = new CenterContainer { Name = "CenterContainer", MouseFilter = Control.MouseFilterEnum.Stop };
        _overlay.AddChild(center);
        Ui.Full(center);

        // El antiguo ScrollContainer era hijo directo del CenterContainer y su
        // altura mínima de 0 podía contraerse a cero: solo quedaba el fondo negro.
        // Ahora la tarjeta tiene medidas acotadas por la pantalla y las acciones
        // están FUERA del scroll, siempre visibles y pulsables.
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(240, Math.Min(Ui.IsMobile ? 710 : 680, viewport.X - 24));
        float height = Math.Max(210, Math.Min(Ui.IsMobile ? 570 : 550, viewport.Y - 24));
        var card = Card();
        card.Name = "ReleaseUpdateCard";
        card.CustomMinimumSize = new Vector2(width, height);
        _responsiveCard = card;
        center.AddChild(card);

        var layout = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        layout.AddThemeConstantOverride("separation", 10);
        card.AddChild(layout);
        var scroll = new ScrollContainer
        {
            Name = "ReleaseUpdateDetails",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        layout.AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", Ui.IsMobile ? 15 : 12);
        scroll.AddChild(body);

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

        layout.AddChild(new HSeparator());
        var actions = new GridContainer { Columns = width < 600 ? 1 : 2 };
        _responsiveButtons = actions;
        actions.Name = "ReleaseUpdateActions";
        actions.AddThemeConstantOverride("separation", 12);
        actions.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        layout.AddChild(actions);
        _secondary = Ui.Button("Ahora no", Close);
        _secondary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_secondary);
        _primary = Ui.Button("Descargar actualización", PrimaryAction, true);
        _primary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        actions.AddChild(_primary);
        ResizeUpdateCard();
        _overlay.Visible = true;
    }

    private void ResizeUpdateCard()
    {
        if (_responsiveCard == null || !GodotObject.IsInstanceValid(_responsiveCard)) return;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(210, Math.Min(Ui.IsMobile ? 710 : 680, screen.X - 24));
        float height = Math.Max(180, Math.Min(Ui.IsMobile ? 570 : 550, screen.Y - 24));
        _responsiveCard.CustomMinimumSize = new Vector2(width, height);
        if (_responsiveButtons != null) _responsiveButtons.Columns = width < 600 ? 1 : 2;
    }

    // Prueba de geometría real sin consultar GitHub: verifica que el aviso es
    // visible y que sus acciones no quedan fuera del viewport ni colapsadas.
    private async void RunUpdateLayoutSmoke()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ShowNotice("v99.0.0", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Rect2 viewport = GetViewport().GetVisibleRect();
            if (_overlay == null || !_overlay.Visible || _overlay.Size.X < 100 || _overlay.Size.Y < 100)
                throw new InvalidOperationException("El overlay de actualización no ocupa el viewport.");
            var card = _overlay.GetNode<PanelContainer>("CenterContainer/ReleaseUpdateCard");
            if (!viewport.Encloses(card.GetGlobalRect()))
                throw new InvalidOperationException("La tarjeta de actualización queda fuera de pantalla.");
            foreach (var button in new[] { _primary, _secondary })
            {
                if (button == null || !button.IsVisibleInTree() || button.Size.X < 90 ||
                    button.Size.Y < 40 || !viewport.Encloses(button.GetGlobalRect()))
                    throw new InvalidOperationException("Los botones del actualizador no son accesibles.");
            }
            GD.Print("UPDATE_NOTICE_LAYOUT_OK");
            _secondary!.EmitSignal(BaseButton.SignalName.Pressed);
            if (_overlay != null) throw new InvalidOperationException("Ahora no no libera el overlay.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("UPDATE_NOTICE_LAYOUT_FAILED: " + error);
            GetTree().Quit(1);
        }
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
        _progress!.Indeterminate = false;
        _progress.Value = 0;
        _percentLabel!.Text = "0 %";
        _progressLabel!.Text = _expectedBytes > 0 ? "0 B de " + Size(_expectedBytes) : "0 B descargados";
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
            if (_checksumUrl.Length == 0 || _assetName.Length == 0)
                throw new InvalidDataException("Esta versión no incluye un manifiesto de integridad verificable.");
            using var checksumsRequest = new HttpRequestMessage(HttpMethod.Get, _checksumUrl);
            checksumsRequest.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.0");
            using var checksumsResponse = await MetadataHttp.SendAsync(checksumsRequest, token);
            checksumsResponse.EnsureSuccessStatusCode();
            string manifest = await RoomBrowser.ReadBoundedAsync(checksumsResponse.Content, 64 * 1024, token);
            byte[] expectedHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, _assetName);

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
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0, lastReported = 0;
                var watch = Stopwatch.StartNew();
                var lastUpdate = TimeSpan.Zero;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) != 0)
                {
                    received += count;
                    if (received > MaximumDownloadBytes) throw new InvalidDataException("Descarga demasiado grande.");
                    hasher.AppendData(buffer, 0, count);
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
                if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectedHash))
                    throw new InvalidDataException("El SHA-256 descargado no coincide con GitHub Releases. No se instalará el archivo.");
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
        if (generation != _progressGeneration || !_downloading || !GodotObject.IsInstanceValid(_overlay)) return;
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
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
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
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        _downloading = false;
        _downloadStop?.Dispose();
        _downloadStop = null;
        _headline!.Text = "No se ha completado la descarga";
        _subtitle!.Text = reason.Length > 140 ? reason[..140] + "…" : reason;
        _speedLabel!.Text = "Comprueba la conexión y vuelve a intentarlo.";
        _primary!.Disabled = false;
        _primary.Text = "Reintentar";
        _secondary!.Text = "Ahora no";
    }

    private void Close()
    {
        if (_downloading)
        {
            _progressGeneration++;
            _downloadStop?.Cancel();
            _downloading = false;
        }
        if (GodotObject.IsInstanceValid(_canvas)) _canvas!.QueueFree();
        _canvas = null;
        _overlay = null;
        _responsiveCard = null;
        _responsiveButtons = null;
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
        GetViewport().SizeChanged -= ResizeUpdateCard;
        _stop.Cancel();
        _downloadStop?.Cancel();
        _downloadStop?.Dispose();
        _stop.Dispose();
    }
}
