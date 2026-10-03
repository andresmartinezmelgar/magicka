using System.Collections.Concurrent;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Coordina launcher, entrada y representación. El daño siempre pertenece a GameWorld.</summary>
public partial class Main : Node
{
    private readonly ConcurrentQueue<Action> _posted = new();
    private ArenaRenderer _renderer = null!;
    private Control _ui = null!, _help = null!, _pause = null!;
    private Launcher _launcher = null!;
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
    private int _smokeFrames, _renderFrames;
    private string? _capturePath;

    public override void _Ready()
    {
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
            if (arg.StartsWith("--capture=")) _capturePath = arg[10..];
            if (arg.StartsWith("--server=")) _launcher.BrowseAddress(arg[9..]);
            if (arg.StartsWith("--create-room=")) _launcher.BrowseAddress(arg[14..], true);
            if (arg.StartsWith("--tab=") && int.TryParse(arg[6..], out int tab)) _launcher.ShowTab(tab);
        }
        if (_hostSmoke) _ = HostedSmoke();
        if (_smoke)
        {
            StartPractice("Aprendiz", 0, GameMode.Warfare, 3);
            var players = _practice!.Players.Values.ToArray();
            players[0].Position = new(-4, 0); players[1].Position = new(4, 0);
            players[2].Position = new(0, -4); players[3].Position = new(3, 4);
        }
    }
    public void Post(Action action) => _posted.Enqueue(action);
    private async Task HostedSmoke()
    {
        try
        {
            var room = await _launcher.StartSmokeRoom();
            await ConnectOnline("127.0.0.1:29025", "Launcher", -1, "", room.Id);
        }
        catch (Exception e) { Post(() => { GD.PushError("HOST_SMOKE_FAILED: " + e.Message); GetTree().Quit(1); }); }
    }

    public async Task ConnectOnline(string address, string name, int team, string password,
        string roomId = "default", string roomPassword = "")
    {
        var connection = new ArenaConnection();
        try
        {
            await connection.ConnectAsync(address, name, team, password, roomId: roomId, roomPassword: roomPassword);
            connection.Disconnected += reason => Post(() => { if (_connection == connection) ReturnToLauncher(reason); });
            Post(() =>
            {
                _connection = connection; _practice = null; _localId = connection.PlayerId;
                _serverName = (connection.Server?.Name ?? address) + " / " + (connection.Room?.Name ?? "Sala principal");
                StartGame(connection.Latest!);
            });
        }
        catch (Exception e)
        {
            await connection.DisposeAsync();
            string message = e is OperationCanceledException ? "El servidor no responde. Comprueba la IP, el puerto TCP y el cortafuegos."
                : e is ArgumentException or InvalidOperationException ? e.Message
                : "No se pudo conectar. Comprueba que el servidor está iniciado y que la dirección es correcta.";
            Post(() => _launcher.ShowError(message));
        }
    }
    public void StartPractice(string name, int team, GameMode mode, int bots)
    {
        _practice = new(new WorldOptions { Mode = mode, MaxPlayers = mode == GameMode.Duel ? 2 : 8,
            FriendlyFire = true, ScoreLimit = mode == GameMode.Duel ? 5 : 15, Tickets = 75 });
        var player = _practice.AddPlayer(name, team)!; _localId = player.Id;
        for (int i = 0; i < (mode == GameMode.Duel ? 1 : Math.Clamp(bots, 1, 7)); i++) _practice.AddPlayer($"Bot {i + 1}", -1, true);
        _serverName = "Práctica local"; _localSequence = 0;
        StartGame(_practice.Snapshot());
    }
    private void StartGame(WorldSnapshot snapshot)
    {
        _playing = true; _paused = false; _moveTo = false; _snapshotAge = 0; _lastTick = -1;
        _ignoreLeftUntilRelease = true;
        _launcher.Visible = false; _launcher.CloseRooms(); _launcher.SetBusy(false); _help.Visible = _pause.Visible = false;
        _renderer.Clear(); _snapshot = snapshot;
        _hud?.QueueFree(); _hud = new Hud(); _ui.AddChild(_hud); _hud.Build();
        _ui.MoveChild(_help, -1); _ui.MoveChild(_pause, -1);
    }
    public void ReturnToLauncher(string error = "")
    {
        _playing = false; _paused = false; _practice = null; _snapshot = null;
        var old = _connection; _connection = null;
        if (old != null) _ = old.DisposeAsync();
        _hud?.QueueFree(); _hud = null; _pause.Visible = false; _help.Visible = false;
        _renderer.Clear(); _launcher.Visible = true; _launcher.SetBusy(false);
        if (error.Length > 0) _launcher.ShowError(error);
    }

    public override void _Process(double delta)
    {
        while (_posted.TryDequeue(out var action)) action();
        _renderFrames++;
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
            if (_connection != null && _snapshotAge > 8) { ReturnToLauncher("El servidor ha dejado de enviar el estado de la partida."); return; }
            _renderer.Render(latest, _localId, delta, true);
            _hud?.Update(latest, _localId, _serverName, delta);
        }
        else _renderer.Render(_backdrop.Snapshot(), 0, delta, false);
        if (_capturePath != null && _renderFrames == 90) _ = Capture(_capturePath);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_playing) { _backdrop.Step(); return; }
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
            { GD.Print($"SMOKE_OK: {_snapshot?.Players.Length} jugadores, tick {_snapshot?.Tick}"); GetTree().Quit(); }
        }
        if (_hostSmoke && _connection?.Connected == true && ++_smokeFrames >= 180)
        { GD.Print($"HOST_SMOKE_OK: {_connection.Room?.Name}, {_snapshot?.Players.Length} jugadores"); GetTree().Quit(); }
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
        var root = Modal(out var box);
        box.AddChild(Ui.Label("CONTROLES Y COMBINACIONES", 27, Ui.Gold));
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
        box.AddChild(Ui.Button("Cerrar", () => _help.Visible = false, true));
        return root;
    }
    private Control BuildPause()
    {
        var root = Modal(out var box); box.AddChild(Ui.Label("PARTIDA", 30, Ui.Gold));
        box.AddChild(Ui.Label("En red, la partida continúa mientras estás en este menú.", 17, Ui.Muted));
        box.AddChild(Ui.Button("Continuar", TogglePause, true));
        box.AddChild(Ui.Button("Volver al launcher", () => ReturnToLauncher()));
        box.AddChild(Ui.Button("Salir del juego", () => GetTree().Quit()));
        return root;
    }
    private async Task Capture(string path)
    {
        if (DisplayServer.GetName() == "headless") { GD.Print("CAPTURE_SKIPPED: headless"); GetTree().Quit(); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"CAPTURE: {error} {path}"); GetTree().Quit(error == Error.Ok ? 0 : 1);
    }
    public override void _ExitTree() { if (_connection != null) _ = _connection.DisposeAsync(); }
}
