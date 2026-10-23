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
    private Label _networkStats = null!;
    private PanelContainer _networkPanel = null!;
    private double _networkStatsTime;
    private long _previousSent, _previousReceived;
    private ArenaConnection? _statsConnection;
    private WorldSnapshot? _backdropSnapshot;
    private ArenaRenderer _renderer = null!;
    private Control _ui = null!, _help = null!, _pause = null!;
    private Label _diagnosticStatus = null!;
    private Launcher _launcher = null!;
    private TabContainer _helpPages = null!;
    private Label _currentObjective = null!;
    private SpellBook _spellBook = null!;
    private Hud? _hud;
    private RoomWaitingPanel? _waitingPanel;
    private ArenaConnection? _connection;
    private GameWorld? _practice;
    private PracticeSession? _practiceSession;
    private PracticePanel? _practicePanel;
    private readonly GameWorld _backdrop = new(new WorldOptions { Mode = GameMode.Arena, Bots = 4 });
    private int _backdropSnapshotFrames;
    private GridContainer? _inGameHelpControls;
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

    internal bool CanApplyUpdates => !_playing && _connection == null && _pendingConnection == null &&
        !_exporting && !_launcher.Hosting;

    public override void _Ready()
    {
        PrepareMobileWindow();
        PrepareDesktopHeaderSmokeWindow();
        RenderingServer.FramePostDraw += FrameDrawn;
        _log = new DiagnosticLog(ProjectSettings.GlobalizePath("user://logs"), "client"); Diagnostics.Log = _log;
        _processDiagnostics = new ProcessDiagnostics(_log);
        _engineLog = new EngineLogBridge(); OS.AddLogger(_engineLog);
        Diagnostics.Info("client.start", "Cliente iniciado.", new Dictionary<string, object?>
        { ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["display"] = DisplayServer.GetName(), ["editor"] = OS.HasFeature("editor") });
        _renderer = new ArenaRenderer(); AddChild(_renderer);
        RestoreAccountCache();
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
        networkPanel.OffsetBottom = networkPanel.OffsetTop + 70;
        var networkBackground = new StyleBoxFlat { BgColor = new Color(0.05f, 0.08f, 0.12f, 0.82f) };
        networkBackground.SetCornerRadiusAll(5);
        networkBackground.ContentMarginLeft = 9;
        networkBackground.ContentMarginRight = 9;
        networkBackground.ContentMarginTop = 5;
        networkBackground.ContentMarginBottom = 5;
        networkPanel.AddThemeStyleboxOverride("panel", networkBackground);
        _networkStats = Ui.Label("", 13, Ui.Muted);
        _networkStats.MouseFilter = Control.MouseFilterEnum.Ignore;
        _networkStats.HorizontalAlignment = HorizontalAlignment.Right;
        networkPanel.AddChild(_networkStats);
        _ui.AddChild(networkPanel);
        _networkPanel = networkPanel;
        networkPanel.Visible = false;
        _launcher = new Launcher(); _ui.AddChild(_launcher); _launcher.Build(this);
        InitializeGoogleAuthentication();
        _updateNotice = new ReleaseUpdateNotice(); AddChild(_updateNotice);
        _help = BuildHelp(); _ui.AddChild(_help); _help.Visible = false;
        _pause = BuildPause(); _ui.AddChild(_pause); _pause.Visible = false;
        var arguments = OS.GetCmdlineUserArgs();
        InitializeMatchStatistics(arguments);
        _accountEntrySmoke = arguments.Contains("--account-entry-smoke");
        _mobileMenuSmoke = arguments.Contains("--mobile-menu-smoke");
        _visualReview = arguments.Contains("--visual-review");
        _visualReviewDirectory = arguments.FirstOrDefault(a => a.StartsWith("--visual-directory=", StringComparison.Ordinal))?["--visual-directory=".Length..];
        _desktopHeaderSmoke = arguments.Contains("--desktop-header-smoke");
        foreach (string arg in arguments)
        {
            if (arg is "--preview-equipment" or "--equipment-smoke") EquipmentFixture(arg == "--equipment-smoke");
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
        if (arguments.Contains("--reconnect-smoke")) _ = VerifyReconnectInterface(arguments);
        if (arguments.Contains("--defense-smoke")) StartDefenseSmoke(arguments);
        if (arguments.Contains("--statistics-smoke")) StartStatisticsSmoke(arguments);
        if (arguments.Contains("--bestiary-smoke")) StartBestiarySmoke(arguments);
        if (arguments.Contains("--survival-save-smoke")) StartSurvivalSaveSmoke(arguments);
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
    public void StartRenderProfile(ArenaMapId map = ArenaMapId.Classic)
    {
        if (_profiler != null) return;
        ReturnToLauncher(); _launcher.Visible = false;
        _profiler = new RenderProfiler(); AddChild(_profiler);
        try { _profiler.Initialize(_renderer, _ui, _engineLog, OS.GetCmdlineUserArgs(), map, (status, report) =>
        {
            if (report.ValueKind != JsonValueKind.Undefined) _lastRenderProfile = report;
            var completed = _profiler; _profiler = null; completed?.QueueFree();
            ReturnToLauncher(); _launcher.ShowStatus(status);
        }); }
        catch (Exception error)
        {
            _profiler?.QueueFree(); _profiler = null; ReturnToLauncher();
            Diagnostics.Error("render.profile_failed", "No se pudo iniciar la medición gráfica.", error, Context());
            _launcher.ShowError("No se pudo iniciar la medición gráfica: " + error.Message);
            if (RenderProfiler.Requested(OS.GetCmdlineUserArgs())) { GD.PushError("PROFILE_FAILED: " + error.Message); GetTree().Quit(1); }
        }
    }
    private string GoogleConnectionMessage() =>
        GoogleIdentityKnown
            ? "Google aparece conectado, pero la sesión Firebase no está disponible para jugar. Abre Perfil y comprueba que Firestore haya cargado sin errores; después reintenta Google."
            : "Debes iniciar sesión con Google en Perfil antes de entrar a una sala con recompensas.";

    public async Task ConnectOnline(string address, string name, int team, string password,
        string roomId = "default", string roomPassword = "", WizardType wizardType = WizardType.Balanced,
        Action<string>? onError = null, string resumeToken = "", Action? onConnected = null)
    {
        if (Volatile.Read(ref _exiting) != 0) return;
        var connection = new ArenaConnection();
        connection.AccountChanged += message => _ = RefreshRewardAccount(message);
        connection.Disconnected += reason => Post(() => { if (_connection == connection) _ = RecoverConnection(connection, address, name, team, password, roomId, roomPassword, wizardType, reason); });
        ArenaConnection? previous;
        lock (_connectionGate) { previous = _pendingConnection; _pendingConnection = connection; }
        if (previous != null) _ = previous.DisposeAsync();
        try
        {
            string destination = address;
            Uri endpoint = ArenaConnection.Address(destination);

            // La búsqueda LAN descubre salas mediante ws://, pero NO debe enviar
            // el ID token Firebase por un transporte sin cifrar. Si el servidor
            // LAN exige Google, localizar su misma instancia pública en Firestore
            // y cambiar a WSS antes de enviar cualquier credencial.
            if (endpoint.Scheme == "ws" && !endpoint.IsLoopback)
            {
                var localRooms = await RoomBrowser.ListAsync(destination, _lifetime.Token).ConfigureAwait(false);
                if (localRooms.Server.RewardsAvailable)
                {
                    if (!Accounts.IsDirectFirebaseSession)
                        throw new InvalidOperationException(GoogleConnectionMessage());
                    string officialUrl = await PublicAccountServer.Resolve(FirebaseProjectId, _lifetime.Token).ConfigureAwait(false);
                    var officialRooms = await RoomBrowser.ListAsync(officialUrl, _lifetime.Token).ConfigureAwait(false);
                    if (!PublicAccountServer.SameInstance(localRooms.Server, officialRooms.Server) ||
                        !officialRooms.Rooms.Any(room => room.Id == roomId))
                        throw new InvalidOperationException("Esta sala LAN no coincide con el servidor seguro anunciado en Firebase. Usa «Servidor online con Google».");
                    destination = officialUrl;
                    Diagnostics.Info("client.secure_route", "Sala LAN redirigida al mismo servidor por WSS.");
                }
            }

            string accountToken = Accounts.TokenFor(destination);
            // Las sesiones directas de Firebase solo se entregan a la dirección
            // WSS anunciada actualmente por Firestore; nunca a otros destinos.
            if (accountToken.Length == 0 && Accounts.IsDirectFirebaseSession &&
                ArenaConnection.Address(destination).Scheme == "wss")
            {
                string verified = await PublicAccountServer.Resolve(FirebaseProjectId, _lifetime.Token).ConfigureAwait(false);
                accountToken = Accounts.TokenForVerifiedServer(destination, verified);
                if (accountToken.Length == 0)
                    throw new InvalidOperationException("Este servidor no coincide con el servidor oficial anunciado en Firebase.");
            }
            // Las salas WSS sin Firebase continúan admitiendo invitados.
            // Solo el servidor que exige cuentas debe bloquear un token vacío.
            if (accountToken.Length == 0 && ArenaConnection.Address(destination).Scheme == "wss")
            {
                var publicRooms = await RoomBrowser.ListAsync(destination, _lifetime.Token).ConfigureAwait(false);
                if (publicRooms.Server.RewardsAvailable)
                    throw new InvalidOperationException(GoogleConnectionMessage());
            }
            await connection.ConnectAsync(destination, name, team, password, _lifetime.Token, roomId: roomId, roomPassword: roomPassword,
                wizardType: wizardType, deferReady: true, accountToken: accountToken, resumeToken: resumeToken).ConfigureAwait(false);
            if (!Post(() =>
            {
                lock (_connectionGate)
                {
                    if (_pendingConnection != connection) { _ = connection.DisposeAsync(); return; }
                    _pendingConnection = null;
                }
                if (!connection.Connected) { _ = connection.DisposeAsync(); _launcher.ShowError("La conexión se cerró antes de entrar en la partida."); return; }
                if (_connection != null) _ = _connection.DisposeAsync();
                _connection = connection; _practice = null; _practiceSession = null; _localId = connection.PlayerId;
                _serverName = (connection.Server?.Name ?? address) + " / " + (connection.Room?.Name ?? "Sala principal");
                CancelReconnect(); StartGame(connection.Latest!, resumeToken.Length > 0); onConnected?.Invoke();
            })) await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            await connection.DisposeAsync();
            lock (_connectionGate) if (_pendingConnection != connection || _lifetime.IsCancellationRequested) return;
            Diagnostics.Warn("client.connect_failed", "No se pudo entrar en la partida.", e,
                new Dictionary<string, object?> { ["room"] = roomId });
            string message = e is OperationCanceledException ? "El servidor no responde. Comprueba la IP, el puerto TCP y el cortafuegos."
                : e is ArgumentException or InvalidOperationException ? e.Message
                : "No se pudo conectar. Comprueba que el servidor está iniciado y que la dirección es correcta.";
            Post(() =>
            {
                lock (_connectionGate)
                {
                    if (_pendingConnection != connection) return;
                    _pendingConnection = null;
                }
                if (onError != null) onError(message);
                else _launcher.ShowError(message);
            });
        }
    }
    public void CancelPendingConnection()
    {
        ArenaConnection? pending;
        lock (_connectionGate) { pending = _pendingConnection; _pendingConnection = null; }
        if (pending != null) _ = pending.DisposeAsync();
    }
    public void StartPractice(string name, int team, GameMode mode, int bots, WizardType wizardType = WizardType.Balanced,
        ArenaMapId map = ArenaMapId.Classic, DefenseFlow defenseFlow = DefenseFlow.Continuous)
    {
        _resumingSurvival = null;
        CancelPendingConnection();
        var old = _connection; _connection = null;
        if (old != null) _ = old.DisposeAsync();
        _practiceSession = map == ArenaMapId.Practice ? new PracticeSession(name, wizardType) : null;
        if (_practiceSession != null)
        {
            _practice = _practiceSession.World; _localId = _practiceSession.Player.Id; _localSequence = 0;
            _serverName = "Práctica · " + _practice.Map.Name;
            ApplyPracticeEquipment(); StartGame(_practice.Snapshot()); return;
        }
        _practice = new(new WorldOptions { Mode = mode, Map = map, MaxPlayers = mode == GameMode.Defense ? 4 : mode == GameMode.Duel ? 2 : 8,
            FriendlyFire = mode != GameMode.Defense, ScoreLimit = mode == GameMode.Duel ? 5 : 15, Tickets = 75, DefenseFlow = defenseFlow });
        var player = _practice.AddPlayer(name, team, wizardType: wizardType)!; _localId = player.Id;
        ApplyPracticeEquipment();
        for (int i = 0; i < (mode == GameMode.Defense ? Math.Clamp(bots, 0, 3) : mode == GameMode.Duel ? 1 : Math.Clamp(bots, 1, 7)); i++)
            _practice.AddPlayer($"Bot {i + 1}", -1, true, (WizardType)(i % 5));
        _serverName = "Práctica · " + _practice.Map.Name; _localSequence = 0;
        Diagnostics.Info("practice.start", "Práctica iniciada.", new Dictionary<string, object?> { ["mode"] = mode, ["bots"] = bots, ["wizardType"] = wizardType });
        StartGame(_practice.Snapshot());
    }
    private void ActivateGame(WorldSnapshot snapshot, bool prepared = false)
    {
        if (_resumingSurvival is { } checkpoint) SurvivalSaves.Consume(checkpoint);
        _playing = true; _paused = false; _moveTo = false; _snapshotAge = 0; _lastTick = -1;
        ClosePlayerEquipment();
        BeginLocalBestiary();
        _inputTime = 0;
        _networkStatsTime = 0; _statsConnection = null; _previousSent = _previousReceived = 0;
        Diagnostics.Info("game.start", "Partida iniciada.", Context());
        _ignoreLeftUntilRelease = true;
        _launcher.Visible = false; _launcher.CloseRooms(); _launcher.SetBusy(false); _help.Visible = _pause.Visible = false;
        if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.QueueFree(); _equipmentPanel = null;
        if (!prepared) _renderer.Clear(); _snapshot = snapshot;
        var local = snapshot.Players.FirstOrDefault(p => p.Id == _localId);
        _localWasAlive = local?.Alive ?? false; _localDeaths = local?.Deaths ?? 0;
        if (!prepared) PrepareGameControls(snapshot);
        _ui.MoveChild(_networkPanel, -1);
        _ui.MoveChild(_help, -1); _ui.MoveChild(_pause, -1);
        if (_connection != null)
        {
            _waitingPanel?.QueueFree();
            _waitingPanel = new RoomWaitingPanel();
            _ui.AddChild(_waitingPanel);
            _waitingPanel.Build(Submit, () => ReturnToLauncher());
            _waitingPanel.Update(snapshot, _localId, _connection.Server?.RewardsAvailable == true);
            _ui.MoveChild(_waitingPanel, -1);
        }
    }
    public void ReturnToLauncher(string error = "")
    {
        CaptureStatistics(MatchRecordStatus.Left);
        CancelReconnect();
        CancelGameLoading();
        CancelPendingConnection();
        CaptureLocalBestiary(); _localBestiaryRun = ""; _localBestiaryUid = ""; _bestiaryClock = 10; _resumingSurvival = null;
        CloseRelics();
        if (_playing) Diagnostics.Info("game.leave", "Vuelta al launcher.", Context());
        ClosePlayerEquipment();
        _defensePanel?.QueueFree(); _defensePanel = null;
        _playing = false; _paused = false; _practice = null; _practiceSession = null; _snapshot = null;
        _aimMarker.Hide();
        _practicePanel?.QueueFree(); _practicePanel = null;
        var old = _connection; _connection = null;
        if (old != null) _ = CloseStatisticsConnection(old);
        _hud?.QueueFree(); _hud = null;
        _waitingPanel?.QueueFree(); _waitingPanel = null;
        _pause.Visible = false; _help.Visible = false;
        _networkPanel.Visible = false; _statsConnection = null; _networkStatsTime = 0;
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        _renderer.Clear(); _launcher.Visible = true; _launcher.SetBusy(false); _launcher.UpdateSurvivalSave();
        if (error.Length > 0) _launcher.ShowError(error);
    }

    public override void _Process(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        try { ProcessFrame(delta); }
        catch (Exception e)
        {
            Diagnostics.Error("client.frame_failed", "Fallo al actualizar el cliente.", e, Context());
            if (_verification?.FailFrame(e) == true) return;
            throw;
        }
    }

    private void ProcessFrame(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        while (_verification?.HoldPosted != true && _posted.TryDequeue(out var action))
            try { action(); }
            catch (Exception e)
            {
                Diagnostics.Error("client.callback_failed", "Fallo en una operación de interfaz.", e, Context());
                if (_verification?.FailFrame(e) == true) return;
            }
        TickGoogleAuth(delta);
        TickBestiary(delta);
        TickMatchStatistics(delta);
        _renderFrames++;
        if (CheckDesktopHeaderSmoke()) return;
        if (CheckMobileMenuSmoke()) return;
        if (CheckAccountEntrySmoke()) return;
        if (CheckPublicEquipmentSmoke()) return;
        if (CheckEquipmentSmoke()) return;
        if (CheckShopSmoke()) return;
        if (CheckSaleSmoke()) return;
        if (CheckUsabilitySmoke()) return;
        if (CheckDefenseSmoke()) return;
        if (CheckStatisticsSmoke()) return;
        if (CheckBestiarySmoke()) return;
        if (CheckSurvivalSaveSmoke()) return;
        if (_profiler?.Running == true || _reconnecting) return;
        if (Loading) { ProcessLoading(); _verification?.CheckLoadingFrame(); return; }
        _verification?.BeforeFrame(delta);
        _metricsTime += delta;
        if (_metricsTime >= 30)
        {
            Diagnostics.Info("client.metrics", "Estado periódico del cliente.", Context()); _metricsTime = 0;
        }
        if (_playing)
        {
            UpdateNetworkStats(delta);
            var latest = _connection?.Latest ?? _snapshot;
            if (latest == null) return;
            _snapshot = latest;
            ReportDefensePerformance(latest, delta);
            SynchronizeLocalLife();
            if (Ui.IsMobile) UpdateTouchAim();
            else
            {
                _aim = _renderer.Cursor(GetViewport().GetMousePosition());
                bool practiceUi = InspectingEquipment || _practicePanel?.PointerOverPanel == true || _defensePanel?.PointerOverPanel == true || _hud?.PointerOverPanel == true;
                if (practiceUi && Input.IsMouseButtonPressed(MouseButton.Left)) _moveTo = false;
                UpdateMouseMovement(!practiceUi && Input.IsMouseButtonPressed(MouseButton.Left), Input.IsPhysicalKeyPressed(Key.Shift));
            }
            if (!ReferenceEquals(latest, _lastHealthSnapshot)) { _lastHealthSnapshot = latest; _lastTick = latest.Tick; _snapshotAge = 0; } else _snapshotAge += delta;
            if (_connection != null && _snapshotAge > 8 && !latest.WaitingForPlayers)
            {
                Diagnostics.Warn("client.stale_snapshot", "El servidor dejó de enviar el estado.", context: Context());
                _connection.ReconnectAfterStall(); return;
            }
            _renderer.InterpolationDelay = _connection != null ? 0.075 : 1 / 60.0;
            if (_practiceSession != null) _renderer.PracticeCasterDescription = _practiceSession.CasterDescription;
            _renderer.Render(latest, _localId, _verification?.FixedRenderStep == true ? 1 / 60.0 : delta, true);
            _hud?.Update(latest, _localId, _serverName, delta);
            if (_defensePanel != null)
            {
                _defensePanel.Visible = !_paused && !_help.Visible;
                _defensePanel.Update(latest, _localId, _connection != null, _connection?.Server?.RewardsAvailable == true);
                if (latest.MatchEnded && !_defenseEnded) _defensePanel.OpenShop();
                _defenseEnded = latest.MatchEnded;
            }
            _waitingPanel?.Update(latest, _localId, _connection?.Server?.RewardsAvailable == true);
            UpdateTouchVisibility();
            if (_practicePanel != null)
            { _practicePanel.Visible = !_paused && !_help.Visible; _practicePanel.Update(_aim); }
            UpdateTouchLayout();
            _touch?.Update(latest.Players.FirstOrDefault(p => p.Id == _localId), latest);
            UpdateAimMarker(latest);
            _verification?.AfterRender(latest);
        }
        else _renderer.Render(_backdropSnapshot ??= _backdrop.Snapshot(), 0, delta, false);
        _verification?.AfterFrame();
    }

    private void UpdateNetworkStats(double delta)
    {
        if (_networkStats == null) return;
        _networkStatsTime += delta;
        if (_networkStatsTime < 0.5) return;
        double elapsed = _networkStatsTime;
        _networkStatsTime = 0;
        double fps = Engine.GetFramesPerSecond();
        if (_connection is not { Connected: true } connection)
        {
            _networkStats.Text = $"FPS: {fps:F0}  |  Red: sin conexión";
            _networkPanel.Visible = _snapshot?.Defense == null;
            _defensePanel?.SetNetworkStats(_networkStats.Text);
            _statsConnection = null;
            return;
        }
        long sent = connection.BytesSent, received = connection.BytesReceived;
        if (!ReferenceEquals(_statsConnection, connection))
        {
            _statsConnection = connection;
            _previousSent = sent;
            _previousReceived = received;
        }
        double sentRate = Math.Max(0, sent - _previousSent) / elapsed / 1024.0;
        double receivedRate = Math.Max(0, received - _previousReceived) / elapsed / 1024.0;
        _previousSent = sent;
        _previousReceived = received;
        _networkStats.Text = $"FPS: {fps:F0}  |  ↑ {sentRate:F1} KB/s  ↓ {receivedRate:F1} KB/s\nTotal: ↑ {sent / 1024.0:F1} KB  ↓ {received / 1024.0:F1} KB";
        _networkPanel.Visible = _snapshot?.Defense == null;
        _defensePanel?.SetNetworkStats(_networkStats.Text);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        if (Loading || _reconnecting) return;
        if (_verification?.FreezeSimulation == true) return;
        if (_profiler?.Running == true) return;
        if (!_playing)
        {
            _backdrop.Step();
            // Menú: mantener la simulación suave, pero evitar generar 60 snapshots/segundo
            // de un mundo que solo sirve como fondo decorativo.
            if (++_backdropSnapshotFrames >= (Ui.IsMobile ? 3 : 2) || _backdropSnapshot == null)
            {
                _backdropSnapshotFrames = 0;
                _backdropSnapshot = _backdrop.Snapshot();
            }
            return;
        }
        _inputTime += delta;
        if (_practice != null)
        {
            if (!_paused && !_help.Visible)
            {
                Submit(InputMessage());
                if (_practiceSession != null) _practiceSession.Step(); else _practice.Step();
                _snapshot = _practice.Snapshot();
            }
        }
        else if (_inputTime >= 1.0 / 30) { Submit(InputMessage()); _inputTime = 0; }
        _verification?.AfterPhysics();
    }
    private void UpdateMouseMovement(bool leftPressed, bool shiftPressed)
    {
        bool alive = SynchronizeLocalLife();
        if (!leftPressed) _ignoreLeftUntilRelease = false;
        if (alive && !_paused && !_help.Visible && !_ignoreLeftUntilRelease && !shiftPressed && leftPressed)
        { _target = _aim; _moveTo = true; }
    }
    private bool SynchronizeLocalLife()
    {
        var local = (_connection?.Latest ?? _snapshot)?.Players.FirstOrDefault(p => p.Id == _localId);
        if (local == null) return false;
        // Deaths permite detectar otra vida aunque se haya perdido el snapshot de la muerte.
        if (local.Alive != _localWasAlive || local.Deaths != _localDeaths)
        {
            _moveTo = false; _ignoreLeftUntilRelease = true; _target = local.Position;
            _touch?.Reset();
        }
        _localWasAlive = local.Alive; _localDeaths = local.Deaths;
        return local.Alive;
    }
    private void UseMagick(int slot)
    {
        if (!CheckMagickAvailable(slot, checkTarget: true)) return;
        if (slot == 1)
        {
            // El siguiente paquete no debe restaurar el destino anterior al salto.
            _moveTo = false; _ignoreLeftUntilRelease = true;
        }
        Submit(InputMessage()); Submit(new() { Type = "magick", Slot = slot });
    }
    private ClientMessage InputMessage()
    {
        bool alive = SynchronizeLocalLife();
        float x = 0, y = 0;
        if (alive && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen)
        {
            if (_touch != null) { x = _touch.Movement.X; y = _touch.Movement.Y; }
            else
            {
                x = (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);
                y = (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
            }
        }
        if (x != 0 || y != 0) _moveTo = false;
        return new() { Type = "input", MoveX = x, MoveY = y, MoveTo = alive && _moveTo && !_paused && !_help.Visible && !InspectingEquipment && !ControlsOpen,
            TargetX = _target.X, TargetY = _target.Y, AimX = _aim.X, AimY = _aim.Y };
    }
    private void Submit(ClientMessage message)
    {
        if (Loading) return;
        if (_practice != null) { message.Sequence = ++_localSequence; if (message.Type == "defense_restart") { CaptureStatistics(MatchRecordStatus.Left); CaptureLocalBestiary(); if (_practice.RestartDefense()) BeginLocalBestiary(); } else _practice.ApplyMessage(_localId, message); }
        else _connection?.Send(message);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (Loading)
        {
            if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) ReturnToLauncher();
            GetViewport().SetInputAsHandled(); return;
        }
        if (_profiler?.Running == true)
        {
            if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) _profiler.Cancel();
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (ControlsOpen && key.PhysicalKeycode != Key.Escape) { GetViewport().SetInputAsHandled(); return; }
            if (key.PhysicalKeycode == Key.F1) { if (!InspectingEquipment) ToggleHelp(); GetViewport().SetInputAsHandled(); return; }
            if (key.PhysicalKeycode == Key.Escape)
            {
                if (GodotObject.IsInstanceValid(_relicWindow) && _relicWindow!.Visible) CloseRelics();
                else if (ControlsOpen) _controlsSettings!.HandleBack();
                else if (GodotObject.IsInstanceValid(_itemShopPanel)) _itemShopPanel!.HandleBack();
                else if (_defensePanel?.ShopOpen == true) _defensePanel.HandleBack();
                else if (InspectingEquipment) ClosePlayerEquipment();
                else if (GodotObject.IsInstanceValid(_equipmentPanel)) _equipmentPanel!.HandleBack();
                else if (GodotObject.IsInstanceValid(_bestiaryPanel)) CloseBestiary();
                else if (GodotObject.IsInstanceValid(_accountProfilePanel)) CloseAccountProfile();
                else if (GodotObject.IsInstanceValid(_optionAHelp)) CloseOptionAHelp();
                else if (_help.Visible) { _help.Visible = false; UpdateTouchVisibility(); }
                else if (_playing) TogglePause();
                return;
            }
        }
        if (!_playing || _paused || _help.Visible || InspectingEquipment || ControlsOpen ||
            _waitingPanel?.Visible == true) return;
        if (input is InputEventKey { Pressed: true, Echo: false } press)
        {
            Element? element = press.PhysicalKeycode switch
            {
                Key.Q => Element.Water, Key.W => Element.Life, Key.E => Element.Shield, Key.R => Element.Cold,
                Key.A => Element.Lightning, Key.S => Element.Arcane, Key.D => Element.Earth, Key.F => Element.Fire, _ => null
            };
            if (element != null) Submit(new() { Type = "element", Element = element.Value });
            if (press.PhysicalKeycode == Key.Backspace) Submit(new() { Type = "clear" });
            int slot = press.PhysicalKeycode switch { Key.Key1 => 0, Key.Key2 => 1, Key.Key3 => 2, Key.Key4 => 3, _ => -1 };
            if (slot >= 0) UseMagick(slot);
        }
        if (input is InputEventMouseButton mouse)
        {
            if (Ui.IsMobile) return;
            _aim = _renderer.Cursor(mouse.Position); Submit(InputMessage());
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            {
                if (Input.IsPhysicalKeyPressed(Key.Shift)) Submit(new() { Type = "melee" });
                else if (SynchronizeLocalLife())
                { _ignoreLeftUntilRelease = false; _target = _aim; _moveTo = true; Submit(InputMessage()); }
            }
            if (mouse.ButtonIndex == MouseButton.Right)
                Submit(new() { Type = mouse.Pressed ? Input.IsPhysicalKeyPressed(Key.Shift) ? "nova" : "cast" : "release" });
            if (mouse.ButtonIndex == MouseButton.Middle && mouse.Pressed) Submit(new() { Type = "self" });
            if (mouse.ButtonIndex == MouseButton.WheelUp) _renderer.Zoom(-1.5f);
            if (mouse.ButtonIndex == MouseButton.WheelDown) _renderer.Zoom(1.5f);
        }
    }

    public void ToggleHelp()
    {
        // Solo rediseño de menú. En combate se conserva el libro y los controles originales.
        if (!_playing && !_help.Visible) { ToggleOptionAHelp(); return; }
        _help.Visible = !_help.Visible;
        _currentObjective.Text = _playing && _snapshot != null
            ? "ESTA PARTIDA: " + MatchRules.Objective(_snapshot.Mode, _snapshot.ScoreLimit, _snapshot.StartingTickets, _snapshot.Map)
            : "Los límites se eligen al crear la sala. Su objetivo aparece antes de entrar.";
        if (_playing && _help.Visible) StopInput();
        UpdateTouchVisibility();
    }
    private void TogglePause()
    {
        _paused = !_paused; _pause.Visible = _paused;
        if (_relicMenuButton != null) _relicMenuButton.Visible = HasRelics;
        UpdateSurvivalSaveActions();
        if (_paused) StopInput();
        UpdateTouchVisibility();
    }
    private void StopInput() { _moveTo = false; _aimMarker.Hide(); _touch?.Reset(); Submit(new() { Type = "release" }); Submit(InputMessage()); }

    private PanelContainer? _responsiveHelpModal;
    private ScrollContainer? _responsiveHelpScroll;
    private Control Modal(out VBoxContainer content, bool scrollable = false, bool trackHelp = true)
    {
        var root = new Control { Theme = Ui.Theme() }; Ui.Full(root);
        var shade = new ColorRect { Color = Ui.ModalDim }; root.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); root.AddChild(center); Ui.Full(center);
        float available = GetViewport().GetVisibleRect().Size.X;
        var panel = new PanelContainer { CustomMinimumSize = new(Math.Max(280, Math.Min(750, available - 50)), 0) };
        panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel());
        center.AddChild(panel);
        // Solo el modal de ayuda se actualiza tras construirlo.
        if (trackHelp) { _responsiveHelpModal = panel; _responsiveHelpScroll = null; }
        else _responsivePauseModal = panel;
        content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 16);
        if (Ui.IsMobile || scrollable)
        {
            var scroll = new ContentScrollContainer {
                CustomMinimumSize = new(Math.Max(270, Math.Min(Ui.IsMobile ? 1080 : 750, available - 50)),
                    Math.Max(240, GetViewport().GetVisibleRect().Size.Y - 64)),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
            if (trackHelp) _responsiveHelpScroll = scroll;
            else _responsivePauseScroll = scroll;
            panel.AddChild(scroll); content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(content);
        }
        else panel.AddChild(content);
        return root;
    }
    private Control BuildHelp()
    {
        var root = Modal(out var outer);
        outer.AddChild(Ui.Label("CÓMO JUGAR", 27, Ui.Gold));
        _helpPages = new TabContainer { CustomMinimumSize = new(0, 400),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        outer.AddChild(_helpPages);
        var box = new VBoxContainer { Name = "Controles" }; box.AddThemeConstantOverride("separation", 16); _helpPages.AddChild(box);
        var table = Ui.Table(2, "InGameHelpControlsTable"); _inGameHelpControls = table; box.AddChild(table);
        string[,] rows =
        {
            { "Q W E R / A S D F", "Agua, vida, escudo, frío / rayo, arcano, tierra, fuego" },
            { "Clic izquierdo / flechas", "Caminar hacia el cursor / movimiento directo" },
            { "Mantener clic derecho", "Lanzar. Los rayos y chorros se mantienen; tierra se carga." },
            { "Soltar clic derecho", "Soltar la roca cargada o interrumpir el hechizo" },
            { "Botón central", "Lanzar sobre ti: curación, escudo, guarda o secado" },
            { "Mayús + derecho", "Lanzar en área alrededor del mago" },
            { "Mayús + izquierdo", "Golpe de bastón" },
            { "1 / 2 / 3 / 4", "Celeridad / traslación / meteoro / reanimación" },
            { "Retroceso / rueda", "Vaciar elementos / ajustar distancia de cámara" },
            { "TAB / F1 / ESC", "Clasificación / controles / menú" }
        };
        if (Ui.IsMobile) rows = new string[,]
        {
            { "Zona izquierda", "Tocar y arrastrar para mover; soltar detiene al mago" },
            { "Ocho botones de elementos", "Seleccionar hasta tres cargas; se admiten repetidos" },
            { "Joystick derecho", "Apuntar y lanzar mientras mantienes el dedo" },
            { "Soltar joystick derecho", "Lanzar la roca cargada o interrumpir el rayo" },
            { "Sobre mí / En área", "Curación y escudo / pulso alrededor" },
            { "Cuatro magias especiales", "Celeridad / traslación / meteoro / reanimación" },
            { "Ayuda / Tabla / Menú", "Combinaciones y daño / clasificación / pausa" }
        };
        for (int i = 0; i < rows.GetLength(0); i++)
        { Ui.TableCell(table, rows[i, 0], header: true, size: 15); Ui.TableCell(table, rows[i, 1], size: 15); }
        box.AddChild(Ui.Label("FFF: fuego · SSS: rayo arcano · WW + central: curación\nE + central: escudo · EF + central: guarda de fuego · DDF: roca incendiaria\nQF: vapor · QR: hielo · agua seguida de frío congela al objetivo", 16, Ui.Muted));
        box.AddChild(Ui.Label("Fuego/frío, vida/arcano, agua/rayo, tierra/rayo y escudo/escudo se anulan.\nLas magias también pueden afectar a aliados. Tres elementos por hechizo.", 15, Ui.Muted));
        var winsScroll = new ContentScrollContainer { Name = "Cómo ganar", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _helpPages.AddChild(winsScroll);
        var wins = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; wins.AddThemeConstantOverride("separation", 13); winsScroll.AddChild(wins);
        _currentObjective = HelpText(wins, "Los límites se eligen al crear la sala. Su objetivo aparece antes de entrar.", Ui.Gold);
        foreach (var mode in new[] { GameMode.Warfare, GameMode.Duel, GameMode.Arena })
        {
            wins.AddChild(Ui.Label(Ui.ModeName(mode).ToUpperInvariant(), 19, Ui.Gold));
            HelpText(wins, MatchRules.Objective(mode, 15, 75));
            HelpText(wins, MatchRules.Details(mode), Ui.Muted);
        }
        wins.AddChild(Ui.Label("CIUDADELA DE LOS PASILLOS · 4×", 19, Ui.Gold));
        HelpText(wins, MatchRules.Objective(GameMode.Warfare, 15, 75, ArenaMapId.Citadel));
        HelpText(wins, MatchRules.Details(GameMode.Warfare, ArenaMapId.Citadel), Ui.Muted);
        wins.AddChild(Ui.Label("SANTUARIO DE LAS RELIQUIAS", 19, Ui.Gold));
        HelpText(wins, MatchRules.Details(GameMode.Warfare, ArenaMapId.Relics), Ui.Muted);
        wins.AddChild(Ui.Label("COLISEO DE LA ASCENSIÓN", 19, Ui.Gold));
        HelpText(wins, MatchRules.Details(GameMode.Warfare, ArenaMapId.Ascension), Ui.Muted);
        HelpText(wins, "En duelo, la primera muerte tarda 3 segundos; las siguientes añaden 0,5 segundos hasta 10. En todos contra todos y guerra, la primera tarda 4,5 segundos.", Ui.Muted);
        HelpText(wins, "Al terminar, se muestra el ganador y empieza otra partida a los 9 segundos. Puedes consultar el marcador con TAB. En práctica, el duelo se juega a 5 bajas.", Ui.Muted);
        var relicsScroll = new ContentScrollContainer { Name = "Objetos", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _helpPages.AddChild(relicsScroll);
        var relics = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; relics.AddThemeConstantOverride("separation", 14); relicsScroll.AddChild(relics);
        HelpText(relics, "En el Santuario y el Coliseo. Camina sobre un objeto para recogerlo; reaparece a los 12 s. En el Coliseo cada aparición elige una posición nueva al azar. Puedes acumular objetos iguales o distintos. Sus mejoras se pierden al morir; la vida permanente por bajas del Coliseo se conserva.", Ui.Gold);
        foreach (var kind in Enum.GetValues<BuffKind>())
        { relics.AddChild(Ui.Label(RelicBuffs.Name(kind).ToUpperInvariant(), 18, RelicVisual.ColorFor(kind))); HelpText(relics, RelicBuffs.Description(kind), Ui.Muted); }
        var types = new VBoxContainer { Name = "Tipos de mago" }; types.AddThemeConstantOverride("separation", 12); _helpPages.AddChild(types);
        HelpText(types, "Elige tu tipo en el launcher antes de entrar. Todos pueden combinar los ocho elementos.", Ui.Muted);
        foreach (var type in Enum.GetValues<WizardType>())
        {
            types.AddChild(Ui.Label(WizardTypes.Name(type).ToUpperInvariant(), 19, Ui.WizardColor(type)));
            HelpText(types, WizardTypes.Description(type));
        }
        HelpText(types, "El bonus solo aumenta su componente elemental, incluida la quemadura. La debilidad también consume más escudo. Si atacante y defensor combinan bonus y debilidad, ese daño se multiplica por 1,5625. La clase se conserva al reaparecer.", Ui.Muted);
        _spellBook = new SpellBook { Name = "Combinaciones" }; _helpPages.AddChild(_spellBook);
        var elementsScroll = new ContentScrollContainer { Name = "Elementos", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _helpPages.AddChild(elementsScroll);
        var elementsBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        elementsBox.AddThemeConstantOverride("separation", 14); elementsScroll.AddChild(elementsBox);
        HelpText(elementsBox, "VENTAJAS Y DEBILIDADES", Ui.Gold);
        HelpText(elementsBox, "Contra una protección elemental: +50 % al superar su elemento, −50 % si te contrarresta y −82 % contra el mismo elemento. En una mezcla, cada componente conserva su aporte de daño y se compara con los elementos de la defensa.", Ui.Muted);
        var elementalTable = Ui.Table(3, "ElementRelationsTable");
        elementsBox.AddChild(elementalTable);
        foreach (var title in new[] { "Elemento", "Supera a", "Vulnerable a" }) Ui.TableCell(elementalTable, title, header: true);
        foreach (var element in Enum.GetValues<Element>())
        {
            Ui.TableCell(elementalTable, SpellCatalogue.Name([element]));
            Ui.TableCell(elementalTable, SpellCatalogue.Name([ElementInteractions.StrongAgainst(element)]));
            Ui.TableCell(elementalTable, SpellCatalogue.Name([ElementInteractions.Weakness(element)]));
        }
        HelpText(elementsBox, "Agua moja y Vida cura. También desgastan protecciones enemigas con sus ventajas elementales; su presión no daña la salud al romper el escudo. Curar a un aliado conserva su escudo. Los bonus y debilidades propios del tipo de mago se aplican además de estas relaciones.", Ui.Muted);
        HelpText(elementsBox, "El escudo básico deja pasar los hechizos de su propietario. Las barreras imbuidas bloquean también los propios. Todas las barreras siguen bloqueando el movimiento.", Ui.Muted);
        outer.AddChild(Ui.Button("Cerrar", () => { _help.Visible = false; UpdateTouchVisibility(); }, true));
        GetViewport().SizeChanged += UpdateInGameHelpResponsive;
        UpdateInGameHelpResponsive();
        return root;
    }

    private void UpdateInGameHelpResponsive()
    {
        UpdatePauseResponsive();
        float width = GetViewport().GetVisibleRect().Size.X;
        float height = GetViewport().GetVisibleRect().Size.Y;
        if (_responsiveHelpModal != null)
            _responsiveHelpModal.CustomMinimumSize = new Vector2(Math.Max(230, Math.Min(750, width - 32)), 0);
        if (_responsiveHelpScroll != null)
            _responsiveHelpScroll.CustomMinimumSize = new Vector2(
                Math.Max(220, Math.Min(720, width - 52)), Math.Max(170, height - 84));
        _helpPages.CustomMinimumSize = new Vector2(
            Math.Max(220, Math.Min(Ui.IsMobile ? 680 : 690, width - (Ui.IsMobile ? 84 : 100))),
            Math.Max(190, Math.Min(510, height - 180)));
        if (_inGameHelpControls != null)
            _inGameHelpControls.Columns = ResponsiveLayout.Columns(
                Math.Max(250, width - (Ui.IsMobile ? 105 : 140)), 325, 2, 20);
    }
    private static Label HelpText(Node parent, string text, Color? color = null)
    {
        var label = Ui.Label(text, 15, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label); return label;
    }
    private Control BuildPause()
    {
        var root = Modal(out var box, scrollable: true, trackHelp: false); box.AddChild(Ui.Label("PARTIDA", 30, Ui.Gold));
        box.AddChild(Ui.Label("En red, la partida continúa mientras estás en este menú.", 17, Ui.Muted));
        box.AddChild(Ui.Button("Continuar", TogglePause, true));
        AddSurvivalSaveActions(box);
        _relicMenuButton = Ui.Button("Reliquias recogidas", OpenRelics);
        _relicMenuButton.Name = "PauseRelics"; _relicMenuButton.Visible = false; box.AddChild(_relicMenuButton);
        box.AddChild(Ui.Button("Volver al launcher", () => ReturnToLauncher()));
        box.AddChild(Ui.Button("Exportar diagnóstico", ExportDiagnostics));
        _diagnosticStatus = Ui.Label("", 15, Ui.Muted); _diagnosticStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_diagnosticStatus);
        box.AddChild(Ui.Button("Salir del juego", () => GetTree().Quit()));
        UpdatePauseResponsive();
        return root;
    }
    private Dictionary<string, object?> Context() => new()
    {
        ["room"] = _connection?.Room?.Id, ["player"] = _localId, ["tick"] = _snapshot?.Tick,
        ["mode"] = _snapshot?.Mode, ["online"] = _connection != null, ["snapshotAgeSeconds"] = _snapshotAge,
        ["managedBytes"] = GC.GetTotalMemory(false), ["gen0Collections"] = GC.CollectionCount(0), ["frames"] = _renderFrames,
        ["logError"] = _log?.LastError,
        ["wizardType"] = _snapshot?.Players.FirstOrDefault(p => p.Id == _localId)?.WizardType,
        ["activeEffects"] = _renderer?.Effects.ActiveCount, ["pooledEffects"] = _renderer?.Effects.PooledCount,
        ["renderProfile"] = _lastRenderProfile
    };
    public async void ExportDiagnostics()
    {
        if (_exporting || Volatile.Read(ref _exiting) != 0) return;
        _exporting = true;
        _diagnosticStatus.Text = "Guardando diagnóstico…";
        var context = Context();
        context["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        context["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        string directory = ProjectSettings.GlobalizePath("user://reports");
        bool smoke = _verification?.DiagnosticsSmoke == true;
        _verification?.BeforeDiagnosticExport();
        try
        {
            Diagnostics.Info("diagnostic.export", "Exportando diagnóstico.", context);
            string path = await _log.ExportReportAsync(directory, context, _lifetime.Token).ConfigureAwait(false);
            if (smoke)
            {
                await _verification!.CheckDiagnosticReport(path).ConfigureAwait(false);
            }
            else Post(() => { string status = "Diagnóstico guardado en: " + path;
                _launcher.ShowStatus(status); _diagnosticStatus.Text = status; OS.ShellOpen(directory); });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            Diagnostics.Error("diagnostic.export_failed", "No se pudo exportar el diagnóstico.", e);
            Post(() => { _launcher.ShowError("No se pudo guardar el diagnóstico. Comprueba el espacio y los permisos de la carpeta de usuario.");
                if (smoke) { GD.PushError("DIAGNOSTICS_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); } });
        }
        finally { Post(() => _exporting = false); }
    }
    public override void _ExitTree()
    {
        CaptureStatistics(MatchRecordStatus.Interrupted);
        CaptureLocalBestiary();
        RenderingServer.FramePostDraw -= FrameDrawn;
        GetViewport().SizeChanged -= UpdateInGameHelpResponsive;
        CancelGameLoading();
        WizardSprites.ClearCache();
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
