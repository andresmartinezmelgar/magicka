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
    private bool CurrentIdentity => _disabled || _identity == GetParent<Main>().UpdateChannelIdentity;

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
        _identity = GetParent<Main>().UpdateChannelIdentity;
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
        SynchronizeIdentity(GetParent<Main>().UpdateChannelIdentity);
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

    public async Task<string> CheckNow()
    {
        if (_disabled) return "La búsqueda de actualizaciones está disponible en las versiones para Android y Windows.";
        if (_stop.IsCancellationRequested || !IsInsideTree()) return "La búsqueda se ha cancelado.";
        SynchronizeIdentity(GetParent<Main>().UpdateChannelIdentity);
        if (_installing || _downloading) return "Hay una actualización en curso.";
        string identity = _identity;
        int generation = _channelGeneration;
        _dismissedVersion = ""; // Una búsqueda explícita puede volver a ofrecer una versión descartada.
        _checkClock = 0;
        var result = await Check();
        if (_stop.IsCancellationRequested || generation != _channelGeneration || identity != GetParent<Main>().UpdateChannelIdentity)
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
                BuildInfo.UpdateBuildNumber, _android, _stop.Token, GetParent<Main>().UpdateAccess);
            var plan = result.Update;
            if (plan?.Differential == true && (_android && _androidBasePath.Length == 0 ||
                System.IO.File.Exists(System.IO.Path.Combine(_updatesFolder, "skip-delta-" + plan.Version + ".txt"))))
                plan = plan.FullPackage;
            if (plan != null) Callable.From(() =>
            {
                if (!_stop.IsCancellationRequested && channelGeneration == _channelGeneration &&
                    identity == GetParent<Main>().UpdateChannelIdentity) _pendingUpdate = plan;
            }).CallDeferred();
            return result with { Update = plan };
        }
        catch (OperationCanceledException) { return new(false, null); }
        catch (Exception) { return new(false, null); }
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
