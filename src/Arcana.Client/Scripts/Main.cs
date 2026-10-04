using System.Collections.Concurrent;
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
    private bool _exporting, _diagnosticsSmoke;
    private double _metricsTime;
    private WorldSnapshot? _backdropSnapshot;
    private ArenaRenderer _renderer = null!;
    private Control _ui = null!, _help = null!, _pause = null!;
    private Label _diagnosticStatus = null!;
    private Launcher _launcher = null!;
    private TabContainer _helpPages = null!;
    private Label _currentObjective = null!;
    private SpellBook _spellBook = null!;
    private Hud? _hud;
    private ArenaConnection? _connection;
    private GameWorld? _practice;
    private readonly GameWorld _backdrop = new(new WorldOptions { Mode = GameMode.Arena, Bots = 4 });
    private WorldSnapshot? _snapshot;
    private int _localId;
    private long _localSequence;
    private bool _playing, _paused, _moveTo;
    private bool _ignoreLeftUntilRelease;
    private V2 _aim, _target;
    private double _inputTime, _snapshotAge;
    private long _lastTick = -1;
    private string _serverName = "";
    private bool _smoke;
    private bool _hostSmoke;
    private bool _holdPostedForVerification;
    private bool _hudSmoke;
    private bool _effectsSmoke, _previewEffects;
    private int _previewStrength;
    private bool _spritesSmoke;
    private bool _combinationsSmoke;
    private bool _mapsSmoke;
    private ArenaMapId? _previewMap;
    private string? _previewCombination;
    private CastMode _previewCastMode;
    private DemonstrationKind _previewInteraction;
    private static readonly (string Keys, CastMode Mode, DemonstrationKind Kind)[] CombinationCases =
    [
        ("D", CastMode.Forward, DemonstrationKind.Combination), ("DF", CastMode.Forward, DemonstrationKind.Combination),
        ("DQ", CastMode.Forward, DemonstrationKind.Combination), ("DR", CastMode.Forward, DemonstrationKind.Combination),
        ("EW", CastMode.Self, DemonstrationKind.Combination), ("ES", CastMode.Forward, DemonstrationKind.Combination),
        ("EW", CastMode.Forward, DemonstrationKind.Combination), ("SS", CastMode.Forward, DemonstrationKind.CrossedBeams),
        ("SS", CastMode.Forward, DemonstrationKind.ShieldRefraction), ("SS", CastMode.Forward, DemonstrationKind.Capture)
    ];
    private string? _previewWizards;
    private readonly HashSet<(WizardType Type, int Team, int Row, int Pose)> _spriteCoverage = [];
    private int _previewHelp = -1;
    private string? _previewShield;
    private static readonly (string State, float Shield, float Maximum)[] HudCases =
        [("none", 0, 420), ("full", 420, 420), ("half", 210, 420), ("low", 40, 420), ("ward", 90, 180), ("stone", 325, 650)];
    private int _smokeFrames, _renderFrames;
    private string? _capturePath;
    private bool _captureReview, _mapOverview;
    private bool _smokeFailed;
    private bool SmokeMode => _smoke || _hostSmoke || _diagnosticsSmoke || _hudSmoke || _effectsSmoke || _spritesSmoke || _combinationsSmoke || _mapsSmoke;

    private bool CheckSmokeErrors()
    {
        if (_smokeFailed) return true;
        if (!SmokeMode || _engineLog.ErrorCount == 0) return false;
        _smokeFailed = true;
        GD.PushError($"SMOKE_FAILED: {_engineLog.ErrorCount} errores del motor o de scripts.");
        GetTree().Quit(1);
        return true;
    }
    private void CompleteSmoke(string message)
    {
        if (CheckSmokeErrors()) return;
        GD.Print(message); GetTree().Quit();
    }

    public override void _Ready()
    {
        _log = new DiagnosticLog(ProjectSettings.GlobalizePath("user://logs"), "client"); Diagnostics.Log = _log;
        _processDiagnostics = new ProcessDiagnostics(_log);
        _engineLog = new EngineLogBridge(); OS.AddLogger(_engineLog);
        Diagnostics.Info("client.start", "Cliente iniciado.", new Dictionary<string, object?>
        { ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["display"] = DisplayServer.GetName(), ["editor"] = OS.HasFeature("editor") });
        _renderer = new ArenaRenderer(); AddChild(_renderer);
        var layer = new CanvasLayer(); AddChild(layer);
        _ui = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(_ui); Ui.Full(_ui);
        _launcher = new Launcher(); _ui.AddChild(_launcher); _launcher.Build(this);
        _help = BuildHelp(); _ui.AddChild(_help); _help.Visible = false;
        _pause = BuildPause(); _ui.AddChild(_pause); _pause.Visible = false;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--smoke") _smoke = true;
            if (arg == "--host-smoke") _hostSmoke = true;
            if (arg == "--diagnostics-smoke") _diagnosticsSmoke = true;
            if (arg == "--hud-smoke") _hudSmoke = true;
            if (arg == "--effects-smoke") _effectsSmoke = true;
            if (arg == "--preview-effects") _previewEffects = true;
            if (arg.StartsWith("--preview-strength=") && int.TryParse(arg[19..], out int strength)) _previewStrength = Math.Clamp(strength, 1, 3);
            if (arg == "--sprites-smoke") _spritesSmoke = true;
            if (arg == "--combinations-smoke") _combinationsSmoke = true;
            if (arg == "--maps-smoke") _mapsSmoke = true;
            if (arg.StartsWith("--preview-map=") && Enum.TryParse<ArenaMapId>(arg[14..], true, out var map) && Enum.IsDefined(map)) _previewMap = map;
            if (arg.StartsWith("--preview-combination=")) _previewCombination = arg[22..];
            if (arg.StartsWith("--preview-cast=") && Enum.TryParse<CastMode>(arg[15..], true, out var castMode)) _previewCastMode = castMode;
            if (arg.StartsWith("--preview-interaction=") && Enum.TryParse<DemonstrationKind>(arg[22..], true, out var interaction)) _previewInteraction = interaction;
            if (arg == "--preview-wizards") _previewWizards = "front";
            if (arg.StartsWith("--preview-wizards=")) _previewWizards = arg[18..];
            if (arg.StartsWith("--preview-help=") && int.TryParse(arg[15..], out int page)) _previewHelp = Math.Clamp(page, 0, _helpPages.GetTabCount() - 1);
            if (arg.StartsWith("--preview-shield=")) _previewShield = arg[17..];
            if (arg.StartsWith("--capture=")) _capturePath = arg[10..];
            if (arg == "--capture-review") _captureReview = true;
            if (arg == "--map-overview") _mapOverview = true;
            if (arg.StartsWith("--server=")) _launcher.BrowseAddress(arg[9..]);
            if (arg.StartsWith("--create-room=")) _launcher.BrowseAddress(arg[14..], true);
            if (arg.StartsWith("--tab=") && int.TryParse(arg[6..], out int tab)) _launcher.ShowTab(tab);
        }
        if (_hostSmoke) _ = HostedSmoke();
        if (_mapsSmoke || _previewMap != null) { _localId = 1; StartGame(MapFixture(_previewMap ?? ArenaMapId.Citadel)); }
        if (_hudSmoke || _previewShield != null || _effectsSmoke || _previewEffects || _spritesSmoke || _previewWizards != null)
        {
            _practice = null; _localId = 1; _serverName = "Práctica · Colores y escudo";
            if (_spritesSmoke || _previewWizards != null)
            {
                _localId = 3; _serverName = "Práctica · Galería de magos";
                StartGame(VisualFixture.Wizards(0, _previewWizards));
            }
            else StartGame(VisualFixture.Create(_previewShield ?? "none"));
        }
        if (_previewStrength > 0)
        {
            _practice = null; _localId = 1; StartGame(VisualFixture.Strength(0, _previewStrength));
            _hud!.Visible = false;
            _renderer.SetPreviewZoom(24); _renderer.ShowControlPoints = false;
            _ui.AddChild(Ui.Label($"{_previewStrength} {(_previewStrength == 1 ? "carga" : "cargas")}", 28));
        }
        if (_previewHelp >= 0) { ToggleHelp(); _helpPages.CurrentTab = _previewHelp; }
        if (_combinationsSmoke || _previewCombination != null || _previewInteraction != DemonstrationKind.Combination)
        {
            _help.Visible = true; _helpPages.CurrentTab = _helpPages.GetTabIdxFromControl(_spellBook);
            _spellBook.FixedStepForVerification = _combinationsSmoke || _capturePath != null;
            _spellBook.SelectCombination(_previewCombination ?? "SS", _previewCastMode, _previewInteraction);
        }
        if (_smoke)
        {
            StartPractice("Aprendiz", 0, GameMode.Warfare, 3);
            var players = _practice!.Players.Values.ToArray();
            players[0].Position = new(-4, 0); players[1].Position = new(4, 0);
            players[2].Position = new(0, -4); players[3].Position = new(3, 4);
        }
    }
    public bool Post(Action action)
    { if (Volatile.Read(ref _exiting) != 0) return false; _posted.Enqueue(action); return true; }
    private async Task HostedSmoke()
    {
        try
        {
            var room = await _launcher.StartSmokeRoom();
            _holdPostedForVerification = true;
            var canceled = ConnectOnline("127.0.0.1:29025", "Cancelado", -1, "", room.Id);
            CancelPendingConnection(); await canceled;
            await ConnectOnline("127.0.0.1:29025", "Respuesta tardía", -1, "", room.Id);
            ReturnToLauncher();
            while (_posted.TryDequeue(out var action)) action();
            if (_connection != null || _playing) throw new InvalidOperationException("Se entró en una conexión cancelada.");
            await ConnectOnline("127.0.0.1:29025", "Anterior", -1, "", room.Id);
            var previous = _pendingConnection!;
            await ConnectOnline("127.0.0.1:29025", "Launcher", -1, "", room.Id);
            var current = _pendingConnection;
            while (_posted.TryDequeue(out var action)) action();
            if (current == null || _connection != current || !_playing)
                throw new InvalidOperationException("Una respuesta anterior sustituyó la conexión actual.");
            await previous.DisposeAsync();
            GD.Print("CONNECTION_LIFECYCLE_OK: cancelación antes del saludo, respuesta tardía y sustitución de conexión.");
        }
        catch (Exception e) { Post(() => { GD.PushError("HOST_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); }); }
        finally { _holdPostedForVerification = false; }
    }

    public async Task ConnectOnline(string address, string name, int team, string password,
        string roomId = "default", string roomPassword = "", WizardType wizardType = WizardType.Balanced)
    {
        if (Volatile.Read(ref _exiting) != 0) return;
        var connection = new ArenaConnection();
        connection.Disconnected += reason => Post(() => { if (_connection == connection) ReturnToLauncher(reason); });
        ArenaConnection? previous;
        lock (_connectionGate) { previous = _pendingConnection; _pendingConnection = connection; }
        if (previous != null) _ = previous.DisposeAsync();
        try
        {
            await connection.ConnectAsync(address, name, team, password, _lifetime.Token, roomId: roomId, roomPassword: roomPassword, wizardType: wizardType).ConfigureAwait(false);
            if (!Post(() =>
            {
                lock (_connectionGate)
                {
                    if (_pendingConnection != connection) { _ = connection.DisposeAsync(); return; }
                    _pendingConnection = null;
                }
                if (!connection.Connected) { _ = connection.DisposeAsync(); _launcher.ShowError("La conexión se cerró antes de entrar en la partida."); return; }
                if (_connection != null) _ = _connection.DisposeAsync();
                _connection = connection; _practice = null; _localId = connection.PlayerId;
                _serverName = (connection.Server?.Name ?? address) + " / " + (connection.Room?.Name ?? "Sala principal");
                StartGame(connection.Latest!);
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
                _launcher.ShowError(message);
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
        ArenaMapId map = ArenaMapId.Classic)
    {
        CancelPendingConnection();
        var old = _connection; _connection = null;
        if (old != null) _ = old.DisposeAsync();
        _practice = new(new WorldOptions { Mode = mode, Map = map, MaxPlayers = mode == GameMode.Duel ? 2 : 8,
            FriendlyFire = true, ScoreLimit = mode == GameMode.Duel ? 5 : 15, Tickets = 75 });
        var player = _practice.AddPlayer(name, team, wizardType: wizardType)!; _localId = player.Id;
        for (int i = 0; i < (mode == GameMode.Duel ? 1 : Math.Clamp(bots, 1, 7)); i++)
            _practice.AddPlayer($"Bot {i + 1}", -1, true, (WizardType)(i % 5));
        _serverName = "Práctica · " + _practice.Map.Name; _localSequence = 0;
        Diagnostics.Info("practice.start", "Práctica iniciada.", new Dictionary<string, object?> { ["mode"] = mode, ["bots"] = bots, ["wizardType"] = wizardType });
        StartGame(_practice.Snapshot());
    }
    private void StartGame(WorldSnapshot snapshot)
    {
        _playing = true; _paused = false; _moveTo = false; _snapshotAge = 0; _lastTick = -1;
        _inputTime = 0;
        Diagnostics.Info("game.start", "Partida iniciada.", Context());
        _ignoreLeftUntilRelease = true;
        _launcher.Visible = false; _launcher.CloseRooms(); _launcher.SetBusy(false); _help.Visible = _pause.Visible = false;
        _renderer.Clear(); _snapshot = snapshot;
        _hud?.QueueFree(); _hud = new Hud(); _ui.AddChild(_hud); _hud.Build();
        _ui.MoveChild(_help, -1); _ui.MoveChild(_pause, -1);
    }
    public void ReturnToLauncher(string error = "")
    {
        CancelPendingConnection();
        if (_playing) Diagnostics.Info("game.leave", "Vuelta al launcher.", Context());
        _playing = false; _paused = false; _practice = null; _snapshot = null;
        var old = _connection; _connection = null;
        if (old != null) _ = old.DisposeAsync();
        _hud?.QueueFree(); _hud = null; _pause.Visible = false; _help.Visible = false;
        _renderer.Clear(); _launcher.Visible = true; _launcher.SetBusy(false);
        if (error.Length > 0) _launcher.ShowError(error);
    }

    public override void _Process(double delta)
    {
        if (CheckSmokeErrors()) return;
        try { ProcessFrame(delta); }
        catch (Exception e)
        {
            Diagnostics.Error("client.frame_failed", "Fallo al actualizar el cliente.", e, Context());
            if (SmokeMode)
            {
                _smokeFailed = true;
                GD.PushError("SMOKE_FAILED: " + e.Message);
                GetTree().Quit(1);
                return;
            }
            throw;
        }
    }

    private void ProcessFrame(double delta)
    {
        if (CheckSmokeErrors()) return;
        while (!_holdPostedForVerification && _posted.TryDequeue(out var action))
            try { action(); }
            catch (Exception e)
            {
                Diagnostics.Error("client.callback_failed", "Fallo en una operación de interfaz.", e, Context());
                if (SmokeMode) { GD.PushError("SMOKE_FAILED: " + e.Message); GetTree().Quit(1); return; }
            }
        _renderFrames++;
        if (_mapOverview && _previewMap != null && _renderFrames == 2)
        {
            var overview = ArenaMap.Get(_previewMap.Value);
            _renderer.SetPreviewZoom(Math.Max(overview.HalfWidth * 2 / (GetViewport().GetVisibleRect().Size.X / GetViewport().GetVisibleRect().Size.Y),
                overview.HalfHeight * 1.58f) + 6);
        }
        if (_mapsSmoke && _renderFrames <= 150 && _renderFrames % 30 == 1)
            _snapshot = MapFixture(_renderFrames <= 30 || _renderFrames > 120 ? ArenaMapId.Citadel
                : _renderFrames <= 60 ? ArenaMapId.Classic : _renderFrames <= 90 ? ArenaMapId.Ascension : ArenaMapId.Relics);
        if (_mapsSmoke && _renderFrames == 76)
            _snapshot = _snapshot! with { Pickups = _snapshot!.Pickups!.Select(i => i.Id == 1 ? i with { Position = new V2(0, 20) } : i).ToArray() };
        if (_combinationsSmoke) CheckCombinations();
        if (_effectsSmoke || _previewEffects) _snapshot = VisualFixture.Effects(_renderFrames);
        if (_previewStrength > 0) _snapshot = VisualFixture.Strength(_renderFrames, _previewStrength);
        if (_spritesSmoke || _previewWizards != null) _snapshot = VisualFixture.Wizards(_renderFrames, _previewWizards);
        if (_hudSmoke && _renderFrames <= HudCases.Length * 3 && _renderFrames % 3 == 1)
        {
            _snapshot = VisualFixture.Create(HudCases[(_renderFrames - 1) / 3].State);
            _snapshot = _snapshot with { Players = _snapshot.Players.Select(p => p with { Charge = 0.5f, Casting = false }).ToArray() };
        }
        _metricsTime += delta;
        if (_metricsTime >= 30)
        {
            Diagnostics.Info("client.metrics", "Estado periódico del cliente.", Context()); _metricsTime = 0;
        }
        if (_playing)
        {
            _aim = _renderer.Cursor(GetViewport().GetMousePosition());
            if (!Input.IsMouseButtonPressed(MouseButton.Left)) _ignoreLeftUntilRelease = false;
            if (!_paused && !_help.Visible && !_ignoreLeftUntilRelease && !Input.IsPhysicalKeyPressed(Key.Shift) && Input.IsMouseButtonPressed(MouseButton.Left))
            { _target = _aim; _moveTo = true; }
            var latest = _connection?.Latest ?? _snapshot;
            if (latest == null) return;
            _snapshot = latest;
            if (latest.Tick != _lastTick) { _lastTick = latest.Tick; _snapshotAge = 0; } else _snapshotAge += delta;
            if (_connection != null && _snapshotAge > 8)
            {
                Diagnostics.Warn("client.stale_snapshot", "El servidor dejó de enviar el estado.", context: Context());
                ReturnToLauncher("El servidor ha dejado de enviar el estado de la partida. Puedes exportar el diagnóstico desde el launcher."); return;
            }
            _renderer.Render(latest, _localId, _effectsSmoke || _spritesSmoke || _previewStrength > 0 ? 1 / 60.0 : delta, true);
            _hud?.Update(latest, _localId, _serverName, delta);
            if (_mapsSmoke && _renderFrames % 30 == 0) CheckMaps(latest);
            if (_previewMap != null && !_mapOverview && _renderFrames == 1) _renderer.SetPreviewZoom(72);
            if (_hudSmoke && _renderFrames <= HudCases.Length * 3 && _renderFrames % 3 == 2) CheckHudCase();
            if (_effectsSmoke) CheckEffects();
            if (_spritesSmoke) CheckSprites(latest);
        }
        else _renderer.Render(_backdropSnapshot ??= _backdrop.Snapshot(), 0, delta, false);
        if (_diagnosticsSmoke && _renderFrames == 30) ExportDiagnostics();
        if (_capturePath != null && _renderFrames == 90) _ = Capture(_capturePath);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (CheckSmokeErrors()) return;
        if (_hudSmoke || _previewShield != null || _effectsSmoke || _previewEffects || _previewStrength > 0 || _spritesSmoke || _previewWizards != null || _combinationsSmoke || _mapsSmoke || _previewMap != null) return;
        if (!_playing) { _backdrop.Step(); _backdropSnapshot = _backdrop.Snapshot(); return; }
        _inputTime += delta;
        if (_practice != null)
        {
            if (!_paused && !_help.Visible)
            {
                Submit(InputMessage()); _practice.Step(); _snapshot = _practice.Snapshot();
            }
        }
        else if (_inputTime >= 1.0 / 30) { Submit(InputMessage()); _inputTime = 0; }
        if (_smoke)
        {
            _smokeFrames++;
            if (_smokeFrames == 30)
            {
                _aim = new(5, 0);
                Submit(new() { Type = "input", AimX = 5, AimY = 0 });
                foreach (var e in new[] { Element.Arcane, Element.Arcane, Element.Fire }) Submit(new() { Type = "element", Element = e });
                Submit(new() { Type = "cast" });
            }
            if (_smokeFrames == 140) { Submit(new() { Type = "release" }); Submit(new() { Type = "element", Element = Element.Shield }); Submit(new() { Type = "self" }); }
            if (_smokeFrames >= 240 && _capturePath == null)
            CompleteSmoke($"SMOKE_OK: {_snapshot?.Players.Length} jugadores, tick {_snapshot?.Tick}");
        }
        if (_hostSmoke && _connection?.Connected == true && ++_smokeFrames >= 180)
        CompleteSmoke($"HOST_SMOKE_OK: {_connection.Room?.Name}, {_snapshot?.Players.Length} jugadores");
    }
    private WorldSnapshot MapFixture(ArenaMapId id)
    {
        var map = ArenaMap.Get(id); _serverName = map.Name;
        var source = VisualFixture.Create("none");
        bool hold = id == ArenaMapId.Citadel;
        var items = map.HasPickups ? new GameWorld(new() { Map = id, PickupSeed = 271 }).Snapshot().Pickups!
            .Select(i => i.Id == 1 ? i with { Active = false, Respawn = 6 } : i).ToArray() : [];
        return source with { Map = id, HoldingTeam = hold ? 0 : -1, HoldSeconds = hold ? 7.5f : 0,
            Players = source.Players.Select(p => p with { Position = _previewMap != null ? new V2(0, -1) : map.Spawn(p.Team, p.Id),
                MaxHealth = id == ArenaMapId.Ascension ? 1030 : id == ArenaMapId.Relics && p.Id == 1 ? 1100 : 1000,
                Health = id == ArenaMapId.Ascension ? 1020 : p.Health,
                Buffs = id == ArenaMapId.Relics && p.Id == 1 ? [new(BuffKind.Power, 2), new(BuffKind.Vitality, 1)] : [] }).ToArray(),
            Pickups = items,
            Points = map.ControlPoints.Select((p, i) => new PointSnapshot(i, p, hold ? 0 : -1, hold ? -1 : 0)).ToArray() };
    }
    private void CheckMaps(WorldSnapshot snapshot)
    {
        var choice = (OptionButton)_launcher.FindChild("PracticeMapChoice", true, false);
        var objective = (Label)_hud!.FindChild("MatchObjective", true, false);
        var map = ArenaMap.Get(snapshot.Map);
        bool valid = choice.ItemCount == Enum.GetValues<ArenaMapId>().Length && _renderer.CurrentMap == snapshot.Map && _renderer.Points.Count == 3 &&
            _renderer.SceneryTheme == map.Theme && (map.Theme == ArenaTheme.City ? _renderer.SceneryBuildings >= 12 : _renderer.SceneryTrees >= 20) &&
            _renderer.GetChildren().Count(n => n.Name == "MapGeometry") == 1 &&
            snapshot.Points.All(p => _renderer.Points[p.Id].Position == ArenaRenderer.World(p.Position));
        if (snapshot.Map == ArenaMapId.Citadel) valid &= objective.Text.Contains($"{snapshot.HoldSeconds:0.0} / 15 s") && Math.Abs(_renderer.Camera.Position.X) > 40;
        else valid &= objective.Text.Contains("tickets");
        if (snapshot.Map == ArenaMapId.Ascension)
        {
            var health = (ProgressBar)_hud.FindChild("HealthBar", true, false);
            valid &= objective.Text.Contains("+10") && objective.Text.Contains("10 s") && health.MaxValue == 1030 && health.Value == 1020;
        }
        if (map.HasPickups)
            valid &= _renderer.Pickups.Count == 14 && !_renderer.Pickups[1].Available && _renderer.Pickups.Values.Count(i => i.Available) == 13
                && snapshot.Pickups!.All(i => _renderer.Pickups[i.Id].Position == ArenaRenderer.World(i.Position));
        else valid &= _renderer.Pickups.Count == 0;
        if (snapshot.Map == ArenaMapId.Relics)
        {
            var buffs = (Label)_hud.FindChild("ActiveBuffs", true, false);
            valid &= buffs.Text.Contains("Potencia ×2") && buffs.Text.Contains("Vitalidad ×1") && objective.Text.Contains("hasta morir");
        }
        var local = snapshot.Players.Single(p => p.Id == _localId);
        valid &= _renderer.Wizards[_localId].ElementSlotCount == 3 && _renderer.Wizards[_localId].DisplayedElements.SequenceEqual(local.DisplayElements);
        if (!valid) { GD.PushError($"MAPS_SMOKE_FAILED: {map.Name}, cámara, selector, contador o geometría incorrectos"); GetTree().Quit(1); return; }
        GD.Print($"MAP_CASE_OK: {map.Name}, {map.Area} unidades²");
        if (_renderFrames >= 150) CompleteSmoke("MAPS_SMOKE_OK: bosque, ciudad, ruinas, santuario, objetos móviles, mejoras y tres casillas");
    }
    private void CheckHudCase()
    {
        var expected = HudCases[(_renderFrames - 1) / 3];
        var health = (ProgressBar)_hud!.FindChild("HealthBar", true, false);
        var charge = (ProgressBar)_hud.FindChild("ChargeBar", true, false);
        var shield = (ProgressBar)_hud.FindChild("ShieldBar", true, false);
        var text = (Label)_hud.FindChild("ShieldValue", true, false);
        var playerPanel = (Control)_hud.FindChild("PlayerStatusPanel", true, false);
        bool correct = _hud.Scale.IsEqualApprox(Vector2.One * Hud.GameplayScale) && (_hud.Size * Hud.GameplayScale).IsEqualApprox(GetViewport().GetVisibleRect().Size) &&
            playerPanel.GetGlobalTransform().Scale.IsEqualApprox(Vector2.One) && playerPanel.GetGlobalRect().Size.X >= 310 &&
            Math.Abs(playerPanel.GetGlobalRect().Position.X - 24) < 0.01f &&
            health.Value == 830 && health.MaxValue == 1000 && charge.Value == 0 && shield.Value == expected.Shield && shield.MaxValue == expected.Maximum &&
            (expected.Shield == 0 ? text.Text.Contains("INACTIVO") : text.Text.Contains($"{expected.Shield:0} / {expected.Maximum:0}") && text.Text.Contains(" s"));
        if (!correct) { GD.PushError($"HUD_SMOKE_FAILED: {expected.State}, {shield.Value}/{shield.MaxValue}, {text.Text}"); GetTree().Quit(1); return; }
        GD.Print($"HUD_CASE_OK: {expected.State}, {shield.Value}/{shield.MaxValue}");
        if (expected.State == HudCases[^1].State) CompleteSmoke("HUD_SMOKE_OK: 6 estados de escudo, vida, capacidad y duración");
    }
    private void CheckCombinations()
    {
        int index = (_renderFrames - 1) / 200;
        if (index >= CombinationCases.Length) return;
        var item = CombinationCases[index];
        if ((_renderFrames - 1) % 200 == 0) _spellBook.SelectCombination(item.Keys, item.Mode, item.Kind);
        if (_renderFrames % 200 != 0) return;
        try
        {
            var state = _spellBook.Demo.World.Snapshot(); SnapshotValidation.Check(state);
            if (!_spellBook.IsVisibleInTree() || state.Tick < 180)
                throw new InvalidDataException("La pestaña de combinaciones está oculta o la demostración no avanza.");
            if (item.Kind == DemonstrationKind.Combination)
            {
                var damageText = (Label)_spellBook.FindChild("SpellDamageValues", true, false);
                if (!damageText.IsVisibleInTree() || damageText.Text != SpellBalance.Format(SpellCatalogue.FromKeys(item.Keys)!.Recipe, item.Mode))
                    throw new InvalidDataException("F1 no muestra los valores de daño de la combinación y forma de lanzamiento.");
            }
            var effects = _spellBook.Preview.Effects;
            bool valid = effects.PooledCount <= CombatEffects.ParticleLimit + CombatEffects.RingLimit;
            if (index == 0) valid &= !effects.SeenKinds.Contains("splash");
            if (index is >= 1 and <= 3) valid &= effects.SeenKinds.Contains("splash") && state.Fields.Length > 0;
            if (index == 4) valid &= state.Players[0].RegenerationRate == 38 && state.Players[0].Health > 500;
            if (index == 5) valid &= effects.SeenKinds.Contains("explosion");
            if (index == 6) valid &= effects.SeenKinds.Contains("mine_heal");
            if (index == 7) valid &= effects.SeenKinds.Contains("beam_merge");
            if (index == 8) valid &= effects.SeenKinds.Contains("beam_refract");
            if (index == 9) valid &= _spellBook.Preview.Points[1].DisplayedProgress < -0.5f;
            if (!valid) throw new InvalidDataException("No se representó el comportamiento esperado.");
            GD.Print($"COMBINATION_CASE_OK: {item.Keys} {item.Mode} {item.Kind}");
            if (index == CombinationCases.Length - 1)
            CompleteSmoke($"COMBINATIONS_SMOKE_OK: {SpellCatalogue.All.Length} combinaciones y 10 escenas verificadas");
        }
        catch (Exception e) { GD.PushError($"COMBINATIONS_SMOKE_FAILED: {index}, {e.Message}"); GetTree().Quit(1); }
    }
    private void CheckEffects()
    {
        var effects = _renderer.Effects;
        bool valid = effects.ActiveParticles <= CombatEffects.ParticleLimit && effects.ActiveRings <= CombatEffects.RingLimit &&
            effects.PooledCount <= CombatEffects.ParticleLimit + CombatEffects.RingLimit && effects.EmittedLastFrame <= CombatEffects.EmissionBudget;
        if (_renderFrames == 130) valid &= effects.ActiveCount > 0 && effects.SeenKinds.Count == 12 &&
            ((Label)_hud!.FindChild("WizardAffinity", true, false)).Text == "Piromante";
        if (_renderFrames == 240)
        {
            valid &= effects.ActiveCount == 0 && effects.SeenCharges.SetEquals([1,2,3]);
            if (valid) valid = CheckSpellStrength();
            _renderer.Clear(); valid &= effects.ActiveCount == 0 && _renderer.ActiveBeamSegments == 0;
            if (valid) CompleteSmoke("EFFECTS_SMOKE_OK: 1/2/3 cargas, elementos repetidos, 12 eventos, límites, expiración, reutilización y limpieza");
        }
        if (!valid) { GD.PushError("EFFECTS_SMOKE_FAILED: efectos o tipo de mago inválidos"); GetTree().Quit(1); }
    }
    private bool CheckSpellStrength()
    {
        var probe = new CombatEffects(); AddChild(probe);
        var empty = _snapshot! with { Players = [], Projectiles = [], Beams = [], Fields = [], Walls = [], Points = [], Events = [], Pickups = [] };
        var impacts = new int[3]; var trails = new int[3]; var waves = new int[3];
        bool valid = true;
        for (int charges = 1; charges <= 3; charges++)
        {
            var elements = Enumerable.Repeat(Element.Earth, charges).ToArray();
            probe.Clear(); probe.Play(new(1,"impact",V2.Zero,elements,"")); impacts[charges-1] = probe.ActiveParticles;
            probe.Clear(); probe.Play(new(1,"nova",V2.Zero,elements,"",4)); waves[charges-1] = probe.ActiveRings;
            probe.Clear();
            var shot = empty with { Projectiles = [new(1,1,V2.Zero,new(1,0),elements,0.3f)] };
            probe.Advance(shot,0.04f,true); probe.Advance(shot,0.04f,true); trails[charges-1] = probe.ActiveParticles;
            _renderer.Render(shot,0,1/60.0,true);
            var meshes = _renderer.ProjectileVisuals[1].GetChildren().OfType<MeshInstance3D>().ToArray();
            valid &= meshes.Length == charges && ((SphereMesh)meshes[0].Mesh).Radius == 0.3f;
            _renderer.Clear();
        }
        valid &= impacts[0] > 0 && impacts[1] > impacts[0] && impacts[2] > impacts[1] && impacts[2] >= impacts[0]*2.5f;
        valid &= trails[0] > 0 && trails[1] > trails[0] && trails[2] > trails[1] && waves[0] == 1 && waves[1] == 1 && waves[2] == 2;
        foreach (var shape in new[] { SpellShape.Beam, SpellShape.Spray, SpellShape.Force, SpellShape.Lightning })
        {
            int previous = 0;
            for (int charges = 1; charges <= 3; charges++)
            {
                var elements = Enumerable.Repeat(Element.Arcane,charges).ToArray();
                _renderer.Render(empty with { Beams = [new(1,new(-3,0),new(3,0),elements,shape)] },0,1/60.0,true);
                valid &= _renderer.ActiveBeamSegments > previous; previous = _renderer.ActiveBeamSegments;
            }
        }
        // Un hechizo ya lanzado conserva su nivel aunque se preparen tres elementos para el siguiente.
        probe.Clear();
        var caster = _snapshot!.Players[0] with { Burning=0,Wet=0,Chill=0,Frozen=0,Haste=0,RegenerationRate=0,
            Casting=true,Charge=0,Selected=[Element.Fire,Element.Fire,Element.Fire],CastingElements=[Element.Arcane] };
        for (int i=0;i<3;i++) probe.Advance(empty with { Players=[caster] },0.05f,true);
        valid &= probe.ActiveParticles == 1;
        probe.Clear();
        for (int frame=0;frame<40;frame++)
        {
            for (int i=0;i<20;i++) probe.Play(new(frame*20+i,"nova",V2.Zero,[Element.Fire,Element.Fire,Element.Fire],"",4));
            probe.Advance(empty,0.05f,true);
            valid &= probe.ActiveParticles <= CombatEffects.ParticleLimit && probe.ActiveRings <= CombatEffects.RingLimit &&
                probe.EmittedLastFrame <= CombatEffects.EmissionBudget;
        }
        int allocated = probe.PooledCount; probe.Clear(); probe.Play(new(1,"impact",V2.Zero,[Element.Fire],""));
        valid &= probe.ActiveParticles > 0 && probe.PooledCount == allocated;
        for (int i=0;i<25;i++) probe.Advance(empty,0.05f,false);
        valid &= probe.ActiveCount == 0; probe.QueueFree();
        if (valid) GD.Print($"SPELL_STRENGTH_OK: impactos {string.Join('/',impacts)}, estelas {string.Join('/',trails)}, anillos {string.Join('/',waves)}, 4 formas y saturación comprobadas");
        return valid;
    }
    private void CheckSprites(WorldSnapshot snapshot)
    {
        bool valid = _renderer.Wizards.Count == 10;
        foreach (var player in snapshot.Players)
        {
            if (!_renderer.Wizards.TryGetValue(player.Id, out var wizard)) { valid = false; continue; }
            var sprite = wizard.Sprite;
            var atlas = WizardSprites.Load(player.WizardType);
            valid &= sprite.Texture == atlas.Texture && sprite.IsVisibleInTree() == player.Alive &&
                sprite.Frame is >= 0 and < 16 && sprite.Offset == atlas.Offset(sprite.Frame);
            valid &= wizard.ElementSlotCount == 3 && wizard.ElementSlotsVisible == player.Alive;
            if (player.Alive && player.Frozen <= 0) _spriteCoverage.Add((player.WizardType, player.Team, sprite.Frame / 4, sprite.Frame % 4));
            if (player.Frozen > 0) valid &= sprite.Frame % 4 == 0;
            if (_renderFrames == 7) valid &= sprite.Frame % 4 == 3; // Evento instantáneo, sin Casting en el snapshot.
            var material = (ShaderMaterial)sprite.MaterialOverride;
            valid &= material.GetShaderParameter("team_color").AsColor() == (player.Team == 0 ? Ui.Blue : Ui.Red);
        }
        if (_renderFrames == 270)
        {
            // Cinco atlas compartidos, ambos equipos, cuatro direcciones y tres poses usadas.
            valid &= _spriteCoverage.Count == 120;
            _renderer.Clear(); valid &= _renderer.Wizards.Count == 0;
            if (valid) CompleteSmoke("SPRITES_SMOKE_OK: 5 atlas, 120 combinaciones, equipos, anclajes, congelación, muerte, reaparición y limpieza");
        }
        if (!valid) { GD.PushError($"SPRITES_SMOKE_FAILED: frame {_renderFrames}, cobertura {_spriteCoverage.Count}"); GetTree().Quit(1); }
    }
    private ClientMessage InputMessage()
    {
        float x = 0, y = 0;
        if (!_paused && !_help.Visible)
        {
            x = (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);
            y = (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
        }
        if (x != 0 || y != 0) _moveTo = false;
        return new() { Type = "input", MoveX = x, MoveY = y, MoveTo = _moveTo && !_paused && !_help.Visible,
            TargetX = _target.X, TargetY = _target.Y, AimX = _aim.X, AimY = _aim.Y };
    }
    private void Submit(ClientMessage message)
    {
        if (_practice != null) { message.Sequence = ++_localSequence; _practice.ApplyMessage(_localId, message); }
        else _connection?.Send(message);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.F1) { ToggleHelp(); GetViewport().SetInputAsHandled(); return; }
            if (key.PhysicalKeycode == Key.Escape)
            {
                if (_help.Visible) _help.Visible = false;
                else if (_playing) TogglePause();
                return;
            }
        }
        if (!_playing || _paused || _help.Visible) return;
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
            if (slot >= 0) { Submit(InputMessage()); Submit(new() { Type = "magick", Slot = slot }); }
        }
        if (input is InputEventMouseButton mouse)
        {
            _aim = _renderer.Cursor(mouse.Position); Submit(InputMessage());
            if (mouse.ButtonIndex == MouseButton.Left && mouse.Pressed)
            {
                if (Input.IsPhysicalKeyPressed(Key.Shift)) Submit(new() { Type = "melee" });
                else { _target = _aim; _moveTo = true; Submit(InputMessage()); }
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
        _help.Visible = !_help.Visible;
        _currentObjective.Text = _playing && _snapshot != null
            ? "ESTA PARTIDA: " + MatchRules.Objective(_snapshot.Mode, _snapshot.ScoreLimit, _snapshot.StartingTickets, _snapshot.Map)
            : "Los límites se eligen al crear la sala. Su objetivo aparece antes de entrar.";
        if (_playing && _help.Visible) StopInput();
    }
    private void TogglePause()
    {
        _paused = !_paused; _pause.Visible = _paused;
        if (_paused) StopInput();
    }
    private void StopInput() { _moveTo = false; Submit(new() { Type = "release" }); Submit(InputMessage()); }

    private Control Modal(out VBoxContainer content)
    {
        var root = new Control { Theme = Ui.Theme() }; Ui.Full(root);
        var shade = new ColorRect { Color = new Color(0.018f, 0.03f, 0.05f, 0.86f) }; root.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); root.AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(750, 0) }; center.AddChild(panel);
        content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 16); panel.AddChild(content);
        return root;
    }
    private Control BuildHelp()
    {
        var root = Modal(out var outer);
        outer.AddChild(Ui.Label("CÓMO JUGAR", 27, Ui.Gold));
        _helpPages = new TabContainer { CustomMinimumSize = new(960, 510) }; outer.AddChild(_helpPages);
        var box = new VBoxContainer { Name = "Controles" }; box.AddThemeConstantOverride("separation", 16); _helpPages.AddChild(box);
        var table = new GridContainer { Columns = 2 }; table.AddThemeConstantOverride("h_separation", 30); table.AddThemeConstantOverride("v_separation", 9); box.AddChild(table);
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
        for (int i = 0; i < rows.GetLength(0); i++) { table.AddChild(Ui.Label(rows[i, 0], 15, Ui.Gold)); table.AddChild(Ui.Label(rows[i, 1], 15)); }
        box.AddChild(Ui.Label("FFF: fuego · SSS: rayo arcano · WW + central: curación\nE + central: escudo · EF + central: guarda de fuego · DDF: roca incendiaria\nQF: vapor · QR: hielo · agua seguida de frío congela al objetivo", 16, Ui.Muted));
        box.AddChild(Ui.Label("Fuego/frío, vida/arcano, agua/rayo, tierra/rayo y escudo/escudo se anulan.\nLas magias también pueden afectar a aliados. Tres elementos por hechizo.", 15, Ui.Muted));
        var winsScroll = new ScrollContainer { Name = "Cómo ganar", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _helpPages.AddChild(winsScroll);
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
        var relicsScroll = new ScrollContainer { Name = "Objetos", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; _helpPages.AddChild(relicsScroll);
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
        outer.AddChild(Ui.Button("Cerrar", () => _help.Visible = false, true));
        return root;
    }
    private static Label HelpText(Node parent, string text, Color? color = null)
    {
        var label = Ui.Label(text, 15, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label); return label;
    }
    private Control BuildPause()
    {
        var root = Modal(out var box); box.AddChild(Ui.Label("PARTIDA", 30, Ui.Gold));
        box.AddChild(Ui.Label("En red, la partida continúa mientras estás en este menú.", 17, Ui.Muted));
        box.AddChild(Ui.Button("Continuar", TogglePause, true));
        box.AddChild(Ui.Button("Volver al launcher", () => ReturnToLauncher()));
        box.AddChild(Ui.Button("Exportar diagnóstico", ExportDiagnostics));
        _diagnosticStatus = Ui.Label("", 15, Ui.Muted); _diagnosticStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(_diagnosticStatus);
        box.AddChild(Ui.Button("Salir del juego", () => GetTree().Quit()));
        return root;
    }
    private async Task Capture(string path)
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("CAPTURE_SKIPPED: headless"); GetTree().Quit(); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var captured = GetViewport().GetTexture().GetImage(); var error = captured.SavePng(path);
        if (_captureReview)
        {
            captured.Resize(_previewStrength > 0 ? 960 : 640, _previewStrength > 0 ? 600 : 400, Image.Interpolation.Lanczos);
            string marker = _previewStrength > 0 ? "EFFECT_REVIEW_BASE64:" + _previewStrength : "MAP_REVIEW_BASE64:" + _previewMap;
            GD.Print(marker + ":" + Convert.ToBase64String(captured.SaveJpgToBuffer(0.8f)));
        }
        GD.Print($"CAPTURE: {error} {path}"); GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
    private Dictionary<string, object?> Context() => new()
    {
        ["room"] = _connection?.Room?.Id, ["player"] = _localId, ["tick"] = _snapshot?.Tick,
        ["mode"] = _snapshot?.Mode, ["online"] = _connection != null, ["snapshotAgeSeconds"] = _snapshotAge,
        ["managedBytes"] = GC.GetTotalMemory(false), ["gen0Collections"] = GC.CollectionCount(0), ["frames"] = _renderFrames,
        ["logError"] = _log?.LastError,
        ["wizardType"] = _snapshot?.Players.FirstOrDefault(p => p.Id == _localId)?.WizardType,
        ["activeEffects"] = _renderer?.Effects.ActiveCount, ["pooledEffects"] = _renderer?.Effects.PooledCount
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
        bool smoke = _diagnosticsSmoke;
        if (smoke) GD.PushWarning("DIAGNOSTICS_SMOKE_MARKER: aviso de prueba controlado.");
        try
        {
            Diagnostics.Info("diagnostic.export", "Exportando diagnóstico.", context);
            string path = await _log.ExportReportAsync(directory, context, _lifetime.Token).ConfigureAwait(false);
            if (smoke)
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                bool marker = false;
                foreach (var entry in zip.Entries.Where(e => e.Name.StartsWith("client-") && e.Name.EndsWith(".jsonl")))
                {
                    using var reader = new StreamReader(entry.Open());
                    if ((await reader.ReadToEndAsync().ConfigureAwait(false)).Contains("DIAGNOSTICS_SMOKE_MARKER")) marker = true;
                }
                if (!marker || zip.GetEntry("report.json") == null) throw new IOException("El informe no incluye el aviso del motor o los metadatos.");
                Post(() => CompleteSmoke("DIAGNOSTICS_SMOKE_OK: " + path));
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
        Interlocked.Exchange(ref _exiting, 1); _lifetime.Cancel();
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
