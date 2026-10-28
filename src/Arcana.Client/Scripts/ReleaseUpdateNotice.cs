using System.Net.Http;
using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Actualizador visual con canal estable o de pruebas según la sesión autenticada.</summary>
public partial class ReleaseUpdateNotice : Node
{
    private static readonly System.Net.Http.HttpClient MetadataHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly System.Net.Http.HttpClient DownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly ReleaseAssetDownloader Downloader = new(MetadataHttp, DownloadHttp);
    private readonly CancellationTokenSource _stop = new();
    private bool _android;
    private string _updatesFolder = "";
    private CancellationTokenSource? _downloadStop;
    private CanvasLayer? _canvas;
    private Control? _overlay;
    private PanelContainer? _responsiveCard;
    private GridContainer? _responsiveButtons;
    private ElementTextLabel? _headline, _subtitle, _sizeLabel, _progressLabel, _percentLabel, _speedLabel, _note;
    private ProgressBar? _progress;
    private Button? _primary, _secondary;
    private ElementTextLabel? _changes;
    private string _version = "", _downloadUrl = "", _downloadPath = "", _checksumUrl = "", _assetName = "";
    private long _expectedBytes;
    private bool _downloading, _completed, _installing, _waitingToInstall, _disabled;
    private Task<ReleaseUpdateQueryResult>? _checkTask;
    private bool Checking => _checkTask is { IsCompleted: false };
    private string _expectedHash = "", _dismissedVersion = "";
    private ReleaseUpdatePlan? _pendingUpdate;
    private ReleaseUpdatePlan? _activePlan;
    private bool _windowsDelta;
    private string _androidBasePath = "";
    private AndroidUpdateBridge? _androidInstaller;
    private double _checkClock;
    private int _progressGeneration;
    private string _identity = "";
    private int _channelGeneration;
    private bool CurrentIdentity => _disabled || _identity == GetParent<Main>().UpdateIdentityUid;

    public override void _Ready()
    {
        _android = OS.HasFeature("android");
        _updatesFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "updates");
        GetViewport().SizeChanged += ResizeUpdateCard;
        if (OS.GetCmdlineUserArgs().Contains("--update-ui-smoke"))
        {
            _disabled = true;
            Callable.From(RunUpdateLayoutSmoke).CallDeferred();
            return;
        }
        _disabled = OS.HasFeature("editor") || !OS.HasFeature("windows") && !_android ||
            OS.GetCmdlineUserArgs().Any(a => a.Contains("smoke") || a.Contains("preview"));
        if (_disabled) return;
        ReleaseUpdateCache.CleanInstalled(_updatesFolder, BuildInfo.Version, BuildInfo.UpdateBuildNumber);
        _identity = GetParent<Main>().UpdateIdentityUid;
        if (_android)
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
        SynchronizeIdentity(GetParent<Main>().UpdateIdentityUid);
        _checkClock += delta;
        if (_pendingUpdate != null && _overlay == null && GetParent<Main>().CanApplyUpdates)
        {
            var pending = _pendingUpdate;
            _pendingUpdate = null;
            if (pending.Identity != _dismissedVersion)
            {
                _activePlan = pending;
                ShowNotice(pending.Version, pending.DownloadUrl, pending.Bytes, pending.ChecksumUrl, pending.Filename);
            }
        }
        if (_checkClock >= 60 && !Checking && _overlay == null && _pendingUpdate == null)
        { _checkClock = 0; _ = Check(); }
    }

    private void SynchronizeIdentity(string identity)
    {
        if (_identity == identity || _installing) return;
        _identity = identity;
        _channelGeneration++;
        _checkTask = null;
        _downloadStop?.Cancel();
        _progressGeneration++;
        Close();
        _pendingUpdate = _activePlan = null;
        _dismissedVersion = "";
        _checkClock = 60;
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

    public async Task<string> CheckNow()
    {
        if (_disabled) return "La búsqueda de actualizaciones está disponible en las versiones para Android y Windows.";
        if (_stop.IsCancellationRequested || !IsInsideTree()) return "La búsqueda se ha cancelado.";
        SynchronizeIdentity(GetParent<Main>().UpdateIdentityUid);
        if (_installing || _downloading) return "Hay una actualización en curso.";
        string identity = _identity;
        int generation = _channelGeneration;
        _dismissedVersion = ""; // Una búsqueda explícita puede volver a ofrecer una versión descartada.
        _checkClock = 0;
        var result = await Check();
        if (_stop.IsCancellationRequested || generation != _channelGeneration || identity != GetParent<Main>().UpdateIdentityUid)
            return "La búsqueda se ha cancelado al cambiar de sesión.";
        return !result.Succeeded ? "No se ha podido consultar la actualización. Comprueba la conexión." :
            result.Update is { } plan ? $"Actualización {plan.Version} disponible. Puedes elegir descargarla en el aviso." :
            $"No hay una versión más reciente disponible para tu cuenta. Instalada: {BuildInfo.Version}.";
    }

    private Task<ReleaseUpdateQueryResult> Check() => Checking ? _checkTask! : _checkTask = FindUpdate();

    private async Task<ReleaseUpdateQueryResult> FindUpdate()
    {
        if (_stop.IsCancellationRequested) return new(false, null);
        string identity = _identity;
        int channelGeneration = _channelGeneration;
        try
        {
            var result = await ReleaseUpdateQuery.CheckAsync(MetadataHttp, identity, BuildInfo.Version,
                BuildInfo.UpdateBuildNumber, _android, _stop.Token);
            var plan = result.Update;
            if (plan?.Differential == true && (_android && _androidBasePath.Length == 0 ||
                System.IO.File.Exists(System.IO.Path.Combine(_updatesFolder, "skip-delta-" + plan.Version + ".txt"))))
                plan = plan.FullPackage;
            if (plan != null) Callable.From(() =>
            {
                if (!_stop.IsCancellationRequested && channelGeneration == _channelGeneration &&
                    identity == GetParent<Main>().UpdateIdentityUid) _pendingUpdate = plan;
            }).CallDeferred();
            return result with { Update = plan };
        }
        catch (OperationCanceledException) { return new(false, null); }
        catch (Exception) { return new(false, null); }
    }

    private static PanelContainer Card()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel());
        return panel;
    }

    private static void AddText(VBoxContainer parent, ElementTextLabel label)
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
        _downloading = _completed = _installing = _waitingToInstall = _windowsDelta = false;
        _downloadPath = _expectedHash = "";
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

        var dim = new ColorRect { Color = Ui.ModalDim, MouseFilter = Control.MouseFilterEnum.Stop };
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
        _headline = Ui.Label(_activePlan?.Preview == true ? "Versión de prueba disponible" : "Actualización disponible", Ui.IsMobile ? 24 : 25, Ui.Text);
        AddText(topCopy, _headline);

        _subtitle = Ui.Label(_activePlan?.Differential == true ? "Se descargarán solo los cambios compatibles con tu versión cuando lo elijas." :
            _activePlan?.Preview == true ? "Rama: " + _activePlan.SourceBranch + ". Elige cuándo descargar esta prueba."
                : "Elige si quieres descargar la nueva versión ahora o seguir jugando.", 16, Ui.Muted);
        AddText(body, _subtitle);

        var versions = new HBoxContainer();
        versions.AddThemeConstantOverride("separation", 12);
        body.AddChild(versions);
        var installed = Ui.Label("INSTALADA\n" + BuildInfo.Version, 16, Ui.Muted);
        installed.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(installed);
        versions.AddChild(Ui.Label("➜", 24, Ui.Gold));
        var available = Ui.Label("DISPONIBLE\n" + version + (_activePlan?.Preview == true ? $" · prueba #{_activePlan.BuildNumber}" : ""), 16, Ui.Gold);
        available.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        versions.AddChild(available);

        AddText(body, Ui.Label("NOVEDADES DE ESTA VERSIÓN", 17, Ui.Gold));
        _changes = Ui.Label(_activePlan?.Changes is { Length: > 0 } changes
            ? string.Join("\n", changes.Select(c => "• " + c))
            : "Esta publicación no incluye un resumen de cambios.", 16, Ui.Text);
        _changes.Name = "ReleaseChangeSummary";
        AddText(body, _changes);

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

        _note = Ui.Label(_android
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
        ShowSavedDownload();
        ResizeUpdateCard();
        _overlay.Visible = true;
        string? previousFailure = OS.HasFeature("windows") ? WindowsUpdateInstaller.PreviousFailure(
            _updatesFolder, _version) : null;
        if (previousFailure != null) ShowFailure(_progressGeneration, previousFailure);
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
            ShowNotice("99.0.0", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678);
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
            if (_downloading || _downloadStop != null || _primary!.Disabled || _secondary!.Disabled ||
                _primary.Text != "Descargar actualización")
                throw new InvalidOperationException("La descarga comienza sin esperar la elección del usuario.");
            GD.Print("UPDATE_NOTICE_CHOICE_OK: espera Descargar actualización o Ahora no sin iniciar la red.");
            GD.Print("UPDATE_NOTICE_LAYOUT_OK");
            _secondary!.EmitSignal(BaseButton.SignalName.Pressed);
            if (_overlay != null) throw new InvalidOperationException("Ahora no no libera el overlay.");
            _activePlan = new("99.0.0", "preview.apk", "https://github.com/andresmartinezmelgar/magicka/releases", "", 12345678)
                { BuildNumber = 72, SourceBranch = "feature/defensa-oleadas", Changes =
                    ["Guía de botín y enemigos descubiertos en Defensa.", "Inventario de partida compartido y mejoras en la tienda."] };
            ShowNotice(_activePlan.Version, _activePlan.DownloadUrl, _activePlan.Bytes);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_headline!.Text.Contains("prueba") || !_subtitle!.Text.Contains(_activePlan.SourceBranch) || _downloading)
                throw new InvalidOperationException("La preliminar no identifica su origen o descarga sin consentimiento.");
            if (_changes?.Text.Contains(_activePlan.Changes[0]) != true || !_changes.IsVisibleInTree())
                throw new InvalidOperationException("El aviso no muestra las novedades de la versión seleccionada.");
            _identity = "qRUza2DieeTypFqBIRA5KhbtXoY2";
            _pendingUpdate = _activePlan;
            int previousGeneration = _progressGeneration;
            SynchronizeIdentity("");
            if (_overlay != null || _pendingUpdate != null || _activePlan != null || _progressGeneration <= previousGeneration)
                throw new InvalidOperationException("Cambiar de cuenta conserva una actualización del canal anterior.");
            GD.Print("UPDATE_CHANNEL_IDENTITY_OK: identifica la rama y descarta avisos de pruebas al cerrar la sesión.");
            ShowNotice("99.0.1", "https://github.com/andresmartinezmelgar/magicka/releases", 12345678);
            var main = GetParent<Main>(); main.StartPractice("Prueba de salida", 0, GameMode.Duel, 0);
            _downloadPath = System.IO.Path.Combine(_updatesFolder, "qa-never-install.zip");
            _completed = true;
            var pending = InstallDownloaded();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!_waitingToInstall || _installing || _secondary!.Disabled)
                throw new InvalidOperationException("La espera de instalación bloquea la salida del aviso.");
            if (Ui.IsMobile) main._Notification((int)NotificationWMGoBackRequest);
            else main._UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
            await pending;
            if (_overlay != null || _waitingToInstall || _installing || main.CanApplyUpdates)
                throw new InvalidOperationException("Atrás no cancela solo la espera de instalación o inicia una instalación descartada.");
            main.ReturnToLauncher();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GD.Print("UPDATE_INSTALL_WAIT_EXIT_OK: Escape/Atrás cierra la espera, conserva la partida y no inicia la instalación descartada.");
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

    private void Close()
    {
        if (_installing) return;
        _progressGeneration++; _waitingToInstall = false;
        _dismissedVersion = _activePlan?.Identity ?? "stable:" + _version;
        if (_downloading)
        {
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

    internal bool HandleBack()
    {
        if (!GodotObject.IsInstanceValid(_overlay)) return false;
        Close(); return true;
    }

    private async Task InstallDownloaded()
    {
        if (_downloadPath.Length == 0 || _installing || _waitingToInstall || _stop.IsCancellationRequested || !CurrentIdentity) return;
        int generation = _progressGeneration;
        _waitingToInstall = true;
        _primary!.Disabled = true; _secondary!.Disabled = false;
        _primary.Text = "Esperando para instalar…";
        try
        {
            while (!GetParent<Main>().CanApplyUpdates)
            {
                if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay)) return;
                _subtitle!.Text = "La instalación comenzará al terminar la partida y detener el alojamiento local.";
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _stop.Token.ThrowIfCancellationRequested();
                if (!CurrentIdentity) return;
            }
            if (generation != _progressGeneration || !GodotObject.IsInstanceValid(_overlay)) return;
            _waitingToInstall = false; _installing = true;
            _primary.Disabled = _secondary.Disabled = true; _primary.Text = "Instalando…";
            if (_android)
            {
                (_androidInstaller ?? throw new InvalidOperationException("No se ha iniciado el instalador Android."))
                    .Install(_downloadPath, _version, _expectedHash, preview: _activePlan?.Preview == true);
            }
            else
            {
                _subtitle!.Text = "Preparando los archivos y la copia de recuperación.";
                await WindowsUpdateInstaller.PrepareAsync(_downloadPath, Launcher.PackageDirectory(), _version,
                    _expectedHash, _updatesFolder, _stop.Token, preview: _activePlan?.Preview == true);
                GetTree().Quit();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (_windowsDelta) FallbackToFull(_progressGeneration);
            else ShowFailure(_progressGeneration, error.Message);
        }
        finally { if (generation == _progressGeneration) _waitingToInstall = false; }
    }

    private void AndroidInstallStatus(string status, string message)
    {
        if (_stop.IsCancellationRequested) return;
        Diagnostics.Info("update.android_status", message, new Dictionary<string, object?>
            { ["status"] = status, ["version"] = _version, ["asset"] = _assetName });
        if (status == "diagnostic" || !GodotObject.IsInstanceValid(_overlay)) return;
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
