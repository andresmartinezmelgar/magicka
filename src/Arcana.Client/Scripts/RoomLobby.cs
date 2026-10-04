using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Crear salas y entrar en ellas no requiere consola, archivos de configuración ni herramientas externas.</summary>
public partial class RoomLobby : Control
{
    private Main _main = null!;
    private string _address = "", _serverPassword = "", _playerName = "";
    private int _team;
    private WizardType _wizardType;
    private Label _objective = null!, _creationObjective = null!;
    private RoomInfo[] _rooms = [];
    private ItemList _list = null!;
    private Label _status = null!, _heading = null!;
    private LineEdit _joinPassword = null!, _roomName = null!, _roomPassword = null!;
    private OptionButton _mode = null!, _map = null!;
    private SpinBox _capacity = null!, _bots = null!, _score = null!, _tickets = null!;
    private CheckBox _friendly = null!;
    private VBoxContainer _listPane = null!, _createPane = null!;
    private readonly List<Button> _buttons = [];
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();

    public void Build(Main main, string address, string password, string name, int team, WizardType wizardType)
    {
        _main = main; _address = address; _serverPassword = password; _playerName = name; _team = team; _wizardType = wizardType;
        Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = new(0.01f, 0.025f, 0.045f, 0.94f) }; AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(860, 0) }; center.AddChild(panel);
        var scroll = new ScrollContainer { CustomMinimumSize = new(820, Math.Min(740, GetViewport().GetVisibleRect().Size.Y - 120)),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; panel.AddChild(scroll);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 16); scroll.AddChild(box);
        _heading = Ui.Label("SALAS", 28, Ui.Gold); box.AddChild(_heading);
        box.AddChild(Ui.Label(address, 15, Ui.Muted));
        _listPane = new VBoxContainer(); _listPane.AddThemeConstantOverride("separation", 14); box.AddChild(_listPane);
        _list = new ItemList { CustomMinimumSize = new(0, 240), SelectMode = ItemList.SelectModeEnum.Single };
        _listPane.AddChild(_list); _list.ItemActivated += _ => Join();
        _objective = Ui.Label("", 15, Ui.Gold); _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart; _listPane.AddChild(_objective);
        _list.ItemSelected += _ => ShowObjective();
        _listPane.AddChild(Ui.Label("Contraseña de la sala (si tiene)", 14, Ui.Muted));
        _joinPassword = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Opcional", CustomMinimumSize = new(0, 42) };
        _listPane.AddChild(_joinPassword);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 12); _listPane.AddChild(actions);
        ActionButton(actions, "Entrar", Join, true); ActionButton(actions, "Crear sala", () => SwitchPane(true));
        ActionButton(actions, "Actualizar", Refresh);
        _createPane = new VBoxContainer { Visible = false }; _createPane.AddThemeConstantOverride("separation", 12); box.AddChild(_createPane);
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", 10); _createPane.AddChild(grid);
        _roomName = new LineEdit { Text = "Sala de " + (name.Length > 24 ? name[..24] : name), MaxLength = 32, CustomMinimumSize = new(460, 42) };
        Row(grid, "Nombre", _roomName);
        _mode = new OptionButton { CustomMinimumSize = new(0, 42) };
        _mode.AddItem("Guerra de puntos", (int)GameMode.Warfare); _mode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _mode.AddItem("Duelo", (int)GameMode.Duel); Row(grid, "Modo", _mode);
        _map = new OptionButton { Name = "RoomMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>()) _map.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        Row(grid, "Mapa", _map);
        _capacity = Number(2, 16, 8); Row(grid, "Plazas (incluidos los bots)", _capacity);
        _bots = Number(0, 7, 0); Row(grid, "Bots", _bots);
        _friendly = new CheckBox { Text = "Permitir daño entre aliados", ButtonPressed = true }; Row(grid, "Fuego amigo", _friendly);
        _score = Number(1, 200, 15); Row(grid, "Puntos para ganar (arena / duelo)", _score);
        _tickets = Number(5, 1000, 75); Row(grid, "Tickets por equipo (guerra)", _tickets);
        _roomPassword = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Dejar vacío para sala abierta", CustomMinimumSize = new(0, 42) };
        Row(grid, "Contraseña de esta sala", _roomPassword);
        _creationObjective = Ui.Label("", 14, Ui.Gold); _creationObjective.AutowrapMode = TextServer.AutowrapMode.WordSmart; _createPane.AddChild(_creationObjective);
        _mode.ItemSelected += _ =>
        {
            bool duel = (GameMode)_mode.GetSelectedId() == GameMode.Duel;
            _capacity.Editable = !duel; if (duel) _capacity.Value = 2;
            _bots.MaxValue = _capacity.Value - 1;
            ShowCreationObjective();
        };
        _score.ValueChanged += _ => ShowCreationObjective(); _tickets.ValueChanged += _ => ShowCreationObjective();
        _map.ItemSelected += _ => ShowCreationObjective();
        ShowCreationObjective();
        _capacity.ValueChanged += value => _bots.MaxValue = value - 1;
        var creation = new HBoxContainer(); creation.AddThemeConstantOverride("separation", 12); _createPane.AddChild(creation);
        ActionButton(creation, "Crear y entrar", Create, true); ActionButton(creation, "Volver a salas", () => SwitchPane(false));
        box.AddChild(Ui.Label("Cada sala tiene su propia partida. Las salas vacías se retiran a los 15 minutos.", 14, Ui.Muted));
        _status = Ui.Label("Cargando salas…", 15, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new(0, 46); box.AddChild(_status);
        box.AddChild(Ui.Button("Volver al launcher", Close));
        Refresh();
    }
    private static SpinBox Number(int min, int max, int value) => new() { MinValue = min, MaxValue = max,
        Value = value, Step = 1, CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private static void Row(GridContainer grid, string title, Control control)
    { grid.AddChild(Ui.Label(title, 15, Ui.Muted)); grid.AddChild(control); }
    private void ActionButton(Node parent, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(button); _buttons.Add(button);
    }
    private void SwitchPane(bool create) { _listPane.Visible = !create; _createPane.Visible = create; _status.Text = ""; }
    public void SetBusy(bool busy)
    {
        _busy = busy; foreach (var b in _buttons) b.Disabled = busy;
        _list.MouseFilter = busy ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _roomName.Editable = _roomPassword.Editable = _joinPassword.Editable = !busy;
        _mode.Disabled = _map.Disabled = _friendly.Disabled = busy;
        _bots.Editable = _score.Editable = _tickets.Editable = !busy;
        _capacity.Editable = !busy && (GameMode)_mode.GetSelectedId() != GameMode.Duel;
        ShowCreationObjective();
    }
    private bool Active => GodotObject.IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion();
    public void ShowError(string error)
    { SetBusy(false); _status.Text = error; _status.AddThemeColorOverride("font_color", Ui.Red); }
    private void Loading(string text) { SetBusy(true); _status.Text = text; _status.AddThemeColorOverride("font_color", Ui.Muted); }
    private async void Refresh()
    {
        if (_busy) return;
        Loading("Consultando salas…");
        try
        {
            var directory = await RoomBrowser.ListAsync(_address, _lifetime.Token);
            _main.Post(() =>
            {
                if (!Active) return;
                _heading.Text = "SALAS  /  " + directory.Server.Name;
                string? previous = Selected()?.Id; _rooms = directory.Rooms; _list.Clear();
                foreach (var room in _rooms)
                    _list.AddItem($"{room.Name}{(room.PasswordRequired ? " · CON CLAVE" : "")}  ·  {Ui.ModeName(room.Mode)}  ·  {ArenaMap.Get(room.Map).Name}  ·  {room.Players}/{room.MaxPlayers} plazas");
                int at = Array.FindIndex(_rooms, r => r.Id == previous);
                if (_rooms.Length > 0) _list.Select(Math.Max(0, at));
                ShowObjective();
                SetBusy(false); _status.Text = $"{_rooms.Length} sala(s). Selecciona una o crea tu propia partida.";
            });
        }
        catch (Exception e) { Failure(e); }
    }
    private RoomInfo? Selected()
    { var selected = _list.GetSelectedItems(); return selected.Length > 0 && selected[0] < _rooms.Length ? _rooms[selected[0]] : null; }
    private void ShowObjective() => _objective.Text = Selected() is { } room
        ? MatchRules.Objective(room.Mode, room.ScoreLimit, room.Tickets, room.Map) : "Selecciona una sala para ver su objetivo.";
    private void ShowCreationObjective()
    {
        var mode = (GameMode)_mode.GetSelectedId(); var map = (ArenaMapId)_map.GetSelectedId();
        _tickets.Editable = !_busy && mode == GameMode.Warfare && ArenaMap.Get(map).HoldSeconds == 0;
        _score.Editable = !_busy && mode != GameMode.Warfare;
        _creationObjective.Text = MatchRules.Objective(mode, (int)_score.Value, (int)_tickets.Value, map);
    }
    public void OpenCreation() => SwitchPane(true);
    private void Join()
    {
        if (_busy) return;
        var room = Selected(); if (room == null) { ShowError("Selecciona una sala."); return; }
        if (room.Players >= room.MaxPlayers) { ShowError("La sala está llena. Crea otra o actualiza la lista."); return; }
        Loading("Entrando en " + room.Name + "…");
        _ = _main.ConnectOnline(_address, _playerName, _team, _serverPassword, room.Id, _joinPassword.Text, _wizardType);
    }
    private async void Create()
    {
        if (_busy) return;
        var request = new CreateRoomRequest { Name = _roomName.Text, Mode = (GameMode)_mode.GetSelectedId(), Map = (ArenaMapId)_map.GetSelectedId(),
            MaxPlayers = (int)_capacity.Value, Bots = (int)_bots.Value, FriendlyFire = _friendly.ButtonPressed,
            ScoreLimit = (int)_score.Value, Tickets = (int)_tickets.Value, Password = _roomPassword.Text, ServerPassword = _serverPassword };
        Loading("Creando sala…");
        try
        {
            var room = await RoomBrowser.CreateAsync(_address, request, _lifetime.Token);
            _main.Post(() => { if (Active) _ = _main.ConnectOnline(_address, _playerName, _team, _serverPassword, room.Id, request.Password, _wizardType); });
        }
        catch (Exception e) { Failure(e); }
    }
    private void Failure(Exception e)
    {
        if (_lifetime.IsCancellationRequested) return;
        Diagnostics.Warn("lobby.operation_failed", "Fallo en la gestión de salas.", e);
        string message = e is InvalidOperationException or ArgumentException ? e.Message
            : "No se pudo consultar el servidor. Comprueba la IP, los puertos y que esté iniciado.";
        _main.Post(() => { if (Active) ShowError(message); });
    }
    private void Close() { _lifetime.Cancel(); _main.CancelPendingConnection(); QueueFree(); }
    public override void _ExitTree() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
