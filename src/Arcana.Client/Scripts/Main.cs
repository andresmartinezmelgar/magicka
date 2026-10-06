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
    private bool _localWasAlive;
    private int _localDeaths;
    private V2 _aim, _target;
    private double _inputTime, _snapshotAge;
    private long _lastTick = -1;
    private string _serverName = "";
    private int _renderFrames;
    private ClientVerification? _verification;
    private RenderProfiler? _profiler;
    private JsonElement? _lastRenderProfile;

    public override void _Ready()
    {
        PrepareMobileWindow();
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
        var arguments = OS.GetCmdlineUserArgs();
        foreach (string arg in arguments)
        {
            if (arg.StartsWith("--server=")) _launcher.BrowseAddress(arg[9..]);
            if (arg.StartsWith("--create-room=")) _launcher.BrowseAddress(arg[14..], true);
            if (arg.StartsWith("--tab=") && int.TryParse(arg[6..], out int tab)) _launcher.ShowTab(tab);
        }
        if (ClientVerification.Requested(arguments))
        {
            _verification = new ClientVerification(); AddChild(_verification);
            _verification.Initialize(new VerificationAccess(this), arguments);
        }
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
            _profiler.QueueFree(); _profiler = null; ReturnToLauncher();
            Diagnostics.Error("render.profile_failed", "No se pudo iniciar la medición gráfica.", error, Context());
            _launcher.ShowError("No se pudo iniciar la medición gráfica: " + error.Message);
            if (RenderProfiler.Requested(OS.GetCmdlineUserArgs())) { GD.PushError("PROFILE_FAILED: " + error.Message); GetTree().Quit(1); }
        }
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
        var local = snapshot.Players.FirstOrDefault(p => p.Id == _localId);
        _localWasAlive = local?.Alive ?? false; _localDeaths = local?.Deaths ?? 0;
        _hud?.QueueFree(); _hud = new Hud(); _ui.AddChild(_hud); _hud.Build();
        BuildTouchControls();
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
        _touch?.Reset(); _touch?.QueueFree(); _touch = null;
        _renderer.Clear(); _launcher.Visible = true; _launcher.SetBusy(false);
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
        _renderFrames++;
        if (_profiler?.Running == true) return;
        _verification?.BeforeFrame(delta);
        _metricsTime += delta;
        if (_metricsTime >= 30)
        {
            Diagnostics.Info("client.metrics", "Estado periódico del cliente.", Context()); _metricsTime = 0;
        }
        if (_playing)
        {
            var latest = _connection?.Latest ?? _snapshot;
            if (latest == null) return;
            _snapshot = latest;
            SynchronizeLocalLife();
            if (Ui.IsMobile) UpdateTouchAim();
            else
            {
                _aim = _renderer.Cursor(GetViewport().GetMousePosition());
                UpdateMouseMovement(Input.IsMouseButtonPressed(MouseButton.Left), Input.IsPhysicalKeyPressed(Key.Shift));
            }
            if (latest.Tick != _lastTick) { _lastTick = latest.Tick; _snapshotAge = 0; } else _snapshotAge += delta;
            if (_connection != null && _snapshotAge > 8)
            {
                Diagnostics.Warn("client.stale_snapshot", "El servidor dejó de enviar el estado.", context: Context());
                ReturnToLauncher("El servidor ha dejado de enviar el estado de la partida. Puedes exportar el diagnóstico desde el launcher."); return;
            }
            _renderer.InterpolationDelay = _connection != null ? 0.075 : 1 / 60.0;
            _renderer.Render(latest, _localId, _verification?.FixedRenderStep == true ? 1 / 60.0 : delta, true);
            _hud?.Update(latest, _localId, _serverName, delta);
            _touch?.Update(latest.Players.FirstOrDefault(p => p.Id == _localId));
            _verification?.AfterRender(latest);
        }
        else _renderer.Render(_backdropSnapshot ??= _backdrop.Snapshot(), 0, delta, false);
        _verification?.AfterFrame();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_verification?.CheckSmokeErrors() == true) return;
        if (_verification?.FreezeSimulation == true) return;
        if (_profiler?.Running == true) return;
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
        if (alive && !_paused && !_help.Visible)
        {
            if (_touch != null) { x = _touch.Movement.X; y = _touch.Movement.Y; }
            else
            {
                x = (Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0);
                y = (Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0);
            }
        }
        if (x != 0 || y != 0) _moveTo = false;
        return new() { Type = "input", MoveX = x, MoveY = y, MoveTo = alive && _moveTo && !_paused && !_help.Visible,
            TargetX = _target.X, TargetY = _target.Y, AimX = _aim.X, AimY = _aim.Y };
    }
    private void Submit(ClientMessage message)
    {
        if (_practice != null) { message.Sequence = ++_localSequence; _practice.ApplyMessage(_localId, message); }
        else _connection?.Send(message);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if (_profiler?.Running == true)
        {
            if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape }) _profiler.Cancel();
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode == Key.F1) { ToggleHelp(); GetViewport().SetInputAsHandled(); return; }
            if (key.PhysicalKeycode == Key.Escape)
            {
                if (_help.Visible) { _help.Visible = false; UpdateTouchVisibility(); }
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
        if (_paused) StopInput();
        UpdateTouchVisibility();
    }
    private void StopInput() { _moveTo = false; _touch?.Reset(); Submit(new() { Type = "release" }); Submit(InputMessage()); }

    private Control Modal(out VBoxContainer content)
    {
        var root = new Control { Theme = Ui.Theme() }; Ui.Full(root);
        var shade = new ColorRect { Color = new Color(0.018f, 0.03f, 0.05f, 0.86f) }; root.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); root.AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(750, 0) }; center.AddChild(panel);
        content = new VBoxContainer(); content.AddThemeConstantOverride("separation", 16);
        if (Ui.IsMobile)
        {
            var scroll = new ScrollContainer { CustomMinimumSize = new(1080, GetViewport().GetVisibleRect().Size.Y - 64),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            panel.AddChild(scroll); content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(content);
        }
        else panel.AddChild(content);
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
        if (Ui.IsMobile) rows = new string[,]
        {
            { "Zona izquierda", "Tocar y arrastrar para mover; soltar detiene al mago" },
            { "Ocho botones de elementos", "Seleccionar hasta tres cargas; se admiten repetidos" },
            { "Joystick derecho", "Apuntar y lanzar mientras mantienes el dedo" },
            { "Soltar joystick derecho", "Lanzar la roca cargada o interrumpir el rayo" },
            { "Sobre ti / Área / Bastón", "Curación y escudo / pulso alrededor / golpe" },
            { "Cuatro magias especiales", "Celeridad / traslación / meteoro / reanimación" },
            { "Vaciar", "Retirar los elementos seleccionados" },
            { "Ayuda / Tabla / Menú", "Combinaciones y daño / clasificación / pausa" }
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
        outer.AddChild(Ui.Button("Cerrar", () => { _help.Visible = false; UpdateTouchVisibility(); }, true));
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
        WizardSprites.ClearCache();
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
