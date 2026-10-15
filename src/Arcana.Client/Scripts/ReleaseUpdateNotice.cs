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
    private const long MaximumDownloadBytes = ReleaseUpdatePlan.MaximumBytes;
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
    private bool _downloading, _completed, _installing, _checking, _disabled;
    private string _expectedHash = "", _dismissedVersion = "";
    private ReleaseUpdatePlan? _pendingUpdate;
    private ReleaseUpdatePlan? _activePlan;
    private bool _windowsDelta;
    private string _androidBasePath = "";
    private AndroidUpdateBridge? _androidInstaller;
    private double _checkClock;
    private int _progressGeneration;

    public override void _Ready()
    {
        GetViewport().SizeChanged += ResizeUpdateCard;
        if (OS.GetCmdlineUserArgs().Contains("--update-ui-smoke"))
        {
            _disabled = true;
            Callable.From(RunUpdateLayoutSmoke).CallDeferred();
            return;
        }
        _disabled = OS.HasFeature("editor") || !OS.HasFeature("windows") && !OS.HasFeature("android") ||
            OS.GetCmdlineUserArgs().Any(a => a.Contains("smoke") || a.Contains("preview"));
        if (_disabled) return;
        if (OS.HasFeature("android"))
        {
            _androidInstaller = new AndroidUpdateBridge();
            try { _androidBasePath = _androidInstaller.InstalledApkPath; } catch (Exception) { }
            _androidInstaller.Status += (status, message) =>
                Callable.From(() => AndroidInstallStatus(status, message)).CallDeferred();
        }
        _ = Check();
    }

    public override void _Process(double delta)
    {
        if (_disabled) return;
        _checkClock += delta;
        if (_pendingUpdate != null && _overlay == null && GetParent<Main>().CanApplyUpdates)
        {
            var pending = _pendingUpdate;
            _pendingUpdate = null;
            if (pending.Version != _dismissedVersion)
            {
                _activePlan = pending;
                ShowNotice(pending.Version, pending.DownloadUrl, pending.Bytes, pending.ChecksumUrl, pending.Filename);
            }
        }
        if (_checkClock >= 60 && !_checking && _overlay == null && _pendingUpdate == null)
        { _checkClock = 0; _ = Check(); }
    }

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
        if (_checking || _stop.IsCancellationRequested) return;
        _checking = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await MetadataHttp.SendAsync(request, _stop.Token);
            if (!response.IsSuccessStatusCode) return;
            using var document = JsonDocument.Parse(await RoomBrowser.ReadBoundedAsync(response.Content, 256 * 1024, _stop.Token));
            var plan = ReleaseUpdatePlan.Read(document.RootElement, BuildInfo.Version, OS.HasFeature("android"));
            if (plan?.Differential == true && (OS.HasFeature("android") && _androidBasePath.Length == 0 ||
                System.IO.File.Exists(System.IO.Path.Combine(OS.GetUserDataDir(), "updates", "skip-delta-" + plan.Version + ".txt"))))
                plan = plan.FullPackage;
            if (plan != null) Callable.From(() => { if (!_stop.IsCancellationRequested) _pendingUpdate = plan; }).CallDeferred();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Sin red, se puede seguir jugando. */ }
        finally { _checking = false; }
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
        string checksumUrl = "", string assetName = "", bool automatic = true)
    {
        if (!IsInsideTree() || _stop.IsCancellationRequested || _overlay != null) return;
        _version = version;
        _downloadUrl = download;
        _checksumUrl = checksumUrl;
        _assetName = assetName;
        _expectedBytes = size;
        _completed = false;
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
        var scroll = new ContentScrollContainer
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
        _headline = Ui.Label("Actualización disponible", Ui.IsMobile ? 24 : 25, Ui.Text);
        AddText(topCopy, _headline);

        _subtitle = Ui.Label(_activePlan?.Differential == true ? "Descargando únicamente los cambios de tu versión." :
            "La nueva versión se descargará e instalará automáticamente.", 16, Ui.Muted);
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
            ? "Se instalará automáticamente cuando Android lo permita. El sistema puede solicitar autorización."
            : "La actualización se instalará y el juego se reiniciará automáticamente.", 15, Ui.Muted);
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
        string? previousFailure = OS.HasFeature("windows") ? WindowsUpdateInstaller.PreviousFailure(
            System.IO.Path.Combine(OS.GetUserDataDir(), "updates"), _version) : null;
        if (automatic && previousFailure == null) StartDownload();
        else if (automatic) ShowFailure(_progressGeneration, previousFailure!);
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
            ShowNotice("99.0.0", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678, automatic: false);
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
        if (_completed) _ = InstallDownloaded();
        else StartDownload();
    }

    private void StartDownload()
    {
        if (_downloading || _completed || _stop.IsCancellationRequested) return;
        _downloading = true;
        _downloadStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        _downloadStop.CancelAfter(TimeSpan.FromMinutes(10));
        _primary!.Disabled = true;
        _primary.Text = "Descargando…";
        _progress!.Indeterminate = false;
        _progress.Value = 0;
        _percentLabel!.Text = "0 %";
        _progressLabel!.Text = _expectedBytes > 0 ? "0 B de " + Size(_expectedBytes) : "0 B descargados";
        _secondary!.Text = "Cancelar descarga";
        _headline!.Text = "Descargando actualización";
        _subtitle!.Text = _activePlan?.Differential == true ? "Solo se descargan los cambios compatibles con tu versión." :
            "Descargando el paquete completo de esta versión.";
        _speedLabel!.Text = "Conectando con GitHub…";
        _ = DownloadUpdate(_downloadUrl, _downloadStop.Token, ++_progressGeneration);
    }

    private async Task DownloadUpdate(string url, CancellationToken token, int generation)
    {
        var plan = _activePlan ?? new ReleaseUpdatePlan(_version, _assetName, url, _checksumUrl, _expectedBytes);
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

            string folder = System.IO.Path.Combine(OS.GetUserDataDir(), "updates");
            System.IO.Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, _assetName);
            if (plan.Differential && OS.HasFeature("android"))
            {
                var full = plan.FullPackage!;
                byte[] fullHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, full.Filename);
                string cachedApk = System.IO.Path.Combine(folder, full.Filename);
                if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(cachedApk, full.Bytes, fullHash, token))
                {
                    string hash = Convert.ToHexString(fullHash);
                    Callable.From(() => ShowDownloaded(generation, cachedApk, hash, false)).CallDeferred();
                    return;
                }
            }
            if (await ReleaseDownloadVerifier.IsVerifiedFileAsync(target, _expectedBytes, expectedHash, token))
            {
                await CompleteTransfer(plan, target, manifest, expectedHash, token, generation);
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ArcanaArena-Updater/2.0");
            using var response = await DownloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            long? headerLength = response.Content.Headers.ContentLength;
            if (headerLength > MaximumDownloadBytes) throw new InvalidDataException("La actualización supera el límite de 2 GB.");
            if (headerLength.HasValue && headerLength.Value != _expectedBytes)
                throw new InvalidDataException("El tamaño descargado no coincide con la Release.");
            long expected = _expectedBytes;
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
                if (received != expected)
                    throw new InvalidDataException("La descarga está incompleta.");
                if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectedHash))
                    throw new InvalidDataException("El SHA-256 descargado no coincide con GitHub Releases. No se instalará el archivo.");
                Callable.From(() => UpdateProgress(generation, received, expected, 0)).CallDeferred();
            }
            token.ThrowIfCancellationRequested();
            System.IO.File.Move(temporary, target, true);
            temporary = null;
            await CompleteTransfer(plan, target, manifest, expectedHash, token, generation);
        }
        catch (OperationCanceledException)
        {
            if (!_stop.IsCancellationRequested)
                Callable.From(() => ShowFailure(generation, "Se ha agotado el tiempo de descarga. Puedes volver a intentarlo.")).CallDeferred();
        }
        catch (Exception error)
        {
            if (plan.Differential) Callable.From(() => FallbackToFull(generation)).CallDeferred();
            else Callable.From(() => ShowFailure(generation, error.Message)).CallDeferred();
        }
        finally
        {
            if (temporary != null)
            {
                try { System.IO.File.Delete(temporary); } catch (IOException) { }
            }
        }
    }

    private async Task CompleteTransfer(ReleaseUpdatePlan plan, string path, string manifest, byte[] transportHash,
        CancellationToken token, int generation)
    {
        byte[] installedHash = transportHash;
        bool windowsDelta = plan.Differential && !OS.HasFeature("android");
        if (plan.Differential && OS.HasFeature("android"))
        {
            var full = plan.FullPackage!;
            installedHash = ReleaseDownloadVerifier.ReadExpectedHash(manifest, full.Filename);
            string destination = System.IO.Path.Combine(OS.GetUserDataDir(), "updates", full.Filename);
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
        string folder = System.IO.Path.Combine(OS.GetUserDataDir(), "updates");
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
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
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
        _headline!.Text = "Actualización verificada";
        _subtitle!.Text = "Preparando la instalación automática.";
        _speedLabel!.Text = "Descarga completada";
        _secondary!.Text = "Volver al juego";
        _primary!.Disabled = false;
        _primary.Text = "Instalar actualización";
        _ = InstallDownloaded();
    }

    private void ShowFailure(int generation, string reason)
    {
        if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
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

    private void Close()
    {
        if (_installing) return;
        _dismissedVersion = _version;
        if (_downloading)
        {
            _progressGeneration++;
            _downloadStop?.Cancel();
            _downloadStop?.Dispose();
            _downloadStop = null;
            _downloading = false;
        }
        if (GodotObject.IsInstanceValid(_canvas)) _canvas!.QueueFree();
        _canvas = null;
        _overlay = null;
        _responsiveCard = null;
        _responsiveButtons = null;
    }

    private async Task InstallDownloaded()
    {
        if (_downloadPath.Length == 0 || _installing || _stop.IsCancellationRequested) return;
        _installing = true;
        _primary!.Disabled = _secondary!.Disabled = true;
        _primary.Text = "Instalando…";
        try
        {
            while (!GetParent<Main>().CanApplyUpdates)
            {
                _subtitle!.Text = "La instalación comenzará al terminar la partida y detener el alojamiento local.";
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _stop.Token.ThrowIfCancellationRequested();
            }
            if (OS.HasFeature("android"))
            {
                (_androidInstaller ?? throw new InvalidOperationException("No se ha iniciado el instalador Android."))
                    .Install(_downloadPath, _version, _expectedHash);
            }
            else
            {
                _subtitle!.Text = "Preparando los archivos y la copia de recuperación.";
                await WindowsUpdateInstaller.PrepareAsync(_downloadPath, Launcher.PackageDirectory(), _version,
                    _expectedHash, System.IO.Path.Combine(OS.GetUserDataDir(), "updates"), _stop.Token);
                GetTree().Quit();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_windowsDelta) FallbackToFull(_progressGeneration);
            else ShowFailure(_progressGeneration, error.Message);
        }
    }

    private void AndroidInstallStatus(string status, string message)
    {
        if (!GodotObject.IsInstanceValid(_overlay) || _stop.IsCancellationRequested) return;
        if (status == "failed") { ShowFailure(_progressGeneration, message); return; }
        _subtitle!.Text = message;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizeUpdateCard;
        _stop.Cancel();
        _downloadStop?.Cancel();
        _downloadStop?.Dispose();
        _androidInstaller?.Dispose();
        _stop.Dispose();
    }
}
