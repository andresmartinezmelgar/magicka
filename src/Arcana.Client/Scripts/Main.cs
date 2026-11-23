using System.Collections.Concurrent;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Coordina launcher, entrada y representación. El daño siempre pertenece a GameWorld.</summary>
public partial class Main : Node
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _connectionGate = new();
    private ArenaConnection? _pendingConnection;
    private DiagnosticLog _log = null!;
    private ProcessDiagnostics _processDiagnostics = null!;
    private EngineLogBridge _engineLog = null!;
    private int _exiting;
    private bool _exporting;
    private double _metricsTime;
    private const float NetworkMetricsHeight = 82;
    private ElementTextLabel _networkStats = null!;
    private PanelContainer _networkPanel = null!;
    private double _networkStatsTime;
    private long _previousSent, _previousReceived;
    private ArenaConnection? _statsConnection;
    private WorldSnapshot? _backdropSnapshot;
    private ArenaRenderer _renderer = null!;
    private Control _ui = null!, _pause = null!;
    private HelpPanel _help = null!;
    private ElementTextLabel _diagnosticStatus = null!;
    private Launcher _launcher = null!;
    private Hud? _hud;
    private RoomWaitingPanel? _waitingPanel;
    private ArenaConnection? _connection;
    private GameWorld? _practice;
    private PracticeSession? _practiceSession;
    private PracticePanel? _practicePanel;
    private readonly GameWorld _backdrop = new(new WorldOptions { Mode = GameMode.Arena, Bots = 4 });
    private int _backdropSnapshotFrames;
    private WorldSnapshot? _snapshot;
    private int _localId;
    private long _localSequence;
    private bool _playing, _paused, _moveTo;
    private bool _ignoreLeftUntilRelease;
    private bool _localWasAlive;
    private int _localDeaths;
    private V2 _aim, _target;
    private double _inputTime, _snapshotAge;
    private long _lastTick = -1;
    private string _serverName = "";
    private int _renderFrames;
    private WorldSnapshot? _lastHealthSnapshot;
    private ClientVerification? _verification;
    private RenderProfiler? _profiler;
    private JsonElement? _lastRenderProfile;

    internal bool CanApplyUpdates => !_playing && !Loading && !_reconnecting && _connection == null && _pendingConnection == null &&
        !_exporting && !_launcher.Hosting;

    public override void _Ready()
    {
        PrepareMobileWindow();
        PrepareDesktopHeaderSmokeWindow();
        RenderingServer.FramePostDraw += FrameDrawn;
        _log = new DiagnosticLog(ProjectSettings.GlobalizePath("user://logs"), "client"); Diagnostics.Log = _log;
        _androidSession = new AndroidSessionBridge();
        _processDiagnostics = new ProcessDiagnostics(_log);
        _engineLog = new EngineLogBridge(); OS.AddLogger(_engineLog);
        Diagnostics.Info("client.start", "Cliente iniciado.", new Dictionary<string, object?>
        { ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["display"] = DisplayServer.GetName(), ["editor"] = OS.HasFeature("editor") });
        _renderer = new ArenaRenderer(); AddChild(_renderer);
        InitializeAudio();
        RestoreAccountCache();
        var arguments = OS.GetCmdlineUserArgs();
        InitializeShopPreload(arguments);
        InitializeTutorial();
        var layer = new CanvasLayer(); AddChild(layer);
        _ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(_ui); Ui.Full(_ui);
        _aimMarker = new AimMarker(); _ui.AddChild(_aimMarker);
        // The minimap occupies the upper right (with a different layout on mobile).
        // Place metrics below the upper-left player/header area instead of behind it.
        var networkPanel = new PanelContainer { Name = "NetworkMetricsPanel", MouseFilter = Control.MouseFilterEnum.Ignore };
        networkPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        networkPanel.OffsetLeft = 22;
        networkPanel.OffsetTop = Ui.IsMobile ? 218 : 78;
        networkPanel.OffsetRight = 310;
        networkPanel.OffsetBottom = networkPanel.OffsetTop + NetworkMetricsHeight;
        var networkBackground = new StyleBoxFlat { BgColor = new Color(0.05f, 0.08f, 0.12f, 0.82f) };
        networkBackground.SetCornerRadiusAll(5);
        networkBackground.ContentMarginLeft = 9;
        networkBackground.ContentMarginRight = 9;
        networkBackground.ContentMarginTop = 5;
        networkBackground.ContentMarginBottom = 5;
        networkPanel.AddThemeStyleboxOverride("panel", networkBackground);
        _networkStats = Ui.Label("", 13, Ui.Muted);
        _networkStats.Name = "NetworkMetricsText";
        _networkStats.MouseFilter = Control.MouseFilterEnum.Ignore;
        _networkStats.HorizontalAlignment = HorizontalAlignment.Right;
        networkPanel.AddChild(_networkStats);
        _ui.AddChild(networkPanel);
        _networkPanel = networkPanel;
        networkPanel.Visible = false;
        _launcher = new Launcher(); _ui.AddChild(_launcher); _launcher.Build(this);
        InitializeGoogleAuthentication();
        if (!OS.HasFeature("arcana_play")) { _updateNotice = new ReleaseUpdateNotice(); AddChild(_updateNotice); }
        _help = BuildHelp(); _help.Visible = false;
        _pause = BuildPause(); _ui.AddChild(_pause); _pause.Visible = false;
        InitializeMatchStatistics(arguments);
        if (arguments.Contains("--element-text-smoke")) _ = RunElementTextSmoke();
        _accountEntrySmoke = arguments.Contains("--account-entry-smoke");
        _mobileMenuSmoke = arguments.Contains("--mobile-menu-smoke");
        _visualReview = arguments.Contains("--visual-review");
        _visualReviewDirectory = arguments.FirstOrDefault(a => a.StartsWith("--visual-directory=", StringComparison.Ordinal))?["--visual-directory=".Length..];
        _desktopHeaderSmoke = arguments.Contains("--desktop-header-smoke");
        foreach (string arg in arguments)
        {
            if (arg is "--preview-equipment" or "--equipment-smoke") EquipmentFixture(arg == "--equipment-smoke");
            if (arg == "--preview-equipment-totals") EquipmentTotalsFixture();
            if (arg == "--preview-equipment-picker")
            {
                EquipmentFixture(false, copies: 12);
                _equipmentPanel!.PressInspect(EquipmentSlot.Chest);
            }
            if (arg.StartsWith("--preview-equipment=") && Enum.TryParse<EquipmentFamily>(arg[20..], true, out var family) && Enum.IsDefined(family)) EquipmentFixture(false, family);
            if (arg == "--equipment-elements-smoke") { _equipmentElementsSmoke = true; EquipmentFixture(false, EquipmentFamily.Water); }
            if (arg == "--equipment-restore-smoke") { _equipmentRestoreSmoke = true; OpenEquipment("wss://servidor-central.example"); }
            if (arg is "--equipment-public-smoke" or "--preview-public-equipment") PublicEquipmentFixture(arg == "--equipment-public-smoke");
            if (arg.StartsWith("--server=")) _launcher.BrowseAddress(arg[9..]);
            if (arg.StartsWith("--create-room=")) _launcher.BrowseAddress(arg[14..], true);
            if (arg.StartsWith("--tab=") && int.TryParse(arg[6..], out int tab)) _launcher.ShowTab(tab);
        }
        if (ClientVerification.Requested(arguments))
        {
            _verification = new ClientVerification(); AddChild(_verification);
            _verification.Initialize(new VerificationAccess(this), arguments);
        }
        if (arguments.Contains("--regression-smoke")) _ = RunBugRegressionSmoke(arguments);
        if (arguments.Contains("--navigation-smoke")) _ = RunNavigationSmoke(arguments);
        if (arguments.Contains("--reconnect-smoke")) _ = VerifyReconnectInterface(arguments);
        if (arguments.Contains("--defense-smoke")) StartDefenseSmoke(arguments);
        if (arguments.Contains("--statistics-smoke")) StartStatisticsSmoke(arguments);
        if (arguments.Contains("--bestiary-smoke")) StartBestiarySmoke(arguments);
        if (arguments.Contains("--survival-save-smoke")) StartSurvivalSaveSmoke(arguments);
        if (arguments.Contains("--survival-biomes-smoke")) _ = RunSurvivalBiomeSmoke(arguments);
        if (arguments.Contains("--shop-smoke")) StartShopSmoke(arguments);
        if (arguments.Contains("--sale-smoke")) StartSaleSmoke(arguments);
        if (arguments.Contains("--usability-smoke")) StartUsabilitySmoke(arguments);
        if (arguments.Contains("--touch-layout-smoke")) _ = RunTouchLayoutSmoke(arguments);
        if (RenderProfiler.Requested(arguments))
        {
            if (_verification != null) throw new ArgumentException("El perfilado y las verificaciones se ejecutan por separado.");
            StartRenderProfile();
        }
    }
    public bool Post(Action action)
    { if (Volatile.Read(ref _exiting) != 0) return false; _posted.Enqueue(action); return true; }
    public override void _ExitTree()
    {
        _rewardedAds?.Dispose();
        _androidSession?.Dispose();
        AutosaveSurvival("shutdown");
        CaptureStatistics(MatchRecordStatus.Interrupted);
        CaptureLocalBestiary();
        try { _survivalWrite.GetAwaiter().GetResult(); _statisticsOutbox?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception error) { Diagnostics.Warn("client.pending_save_failed", "No se pudo completar el guardado al cerrar.", error); }
        RenderingServer.FramePostDraw -= FrameDrawn;
        GetViewport().SizeChanged -= UpdatePauseResponsive;
        CancelGameLoading();
        WizardSprites.ClearCache();
        MythicEyeVisuals.ClearCache();
        EquipmentAuraVisuals.ClearCache();
        ElementChannelVisuals.ClearCache();
        EquipmentIcons.ClearCache();
        Interlocked.Exchange(ref _exiting, 1); _lifetime.Cancel();
        _google?.Dispose();
        _desktopGoogle?.Dispose();
        Accounts.Dispose();
        ArenaConnection? pending; lock (_connectionGate) pending = _pendingConnection;
        try
        {
            if (pending != null) pending.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (_connection != null) _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception e) { Diagnostics.Warn("client.shutdown_failed", "Fallo al cerrar la conexión.", e); }
        if (_log != null)
        {
            Diagnostics.Info("client.stop", "Cliente cerrado.", Context());
            OS.RemoveLogger(_engineLog); _engineLog.Dispose(); _processDiagnostics.Dispose(); _log.Dispose();
        }
        // No libera la fuente mientras una operación cancelada aún puede consultar su Token.
    }
}
