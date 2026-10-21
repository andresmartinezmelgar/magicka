using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Crear salas y entrar en ellas no requiere consola, archivos de configuración ni herramientas externas.</summary>
public partial class RoomLobby : Control
{
    private Main _main = null!;
    private string _address = "", _serverPassword = "", _playerName = "";
    private bool _serverPasswordRequired;
    private string _selectedRoomId = "";
    private OptionButton _teamChoice = null!;
    private WizardType _wizardType;
    private Label _objective = null!, _creationObjective = null!;
    private RoomInfo[] _rooms = [];
    private int _roomPageStart;
    private const int VisibleRoomsPerPage = 3;
    private Label? _roomPageLabel;
    private Button? _roomPagePrevious, _roomPageNext;
    private ItemList _list = null!;
    private Label _status = null!, _heading = null!, _emptyRooms = null!;
    private Label? _serverAddressLabel;
    private LineEdit _joinPassword = null!, _serverPasswordEntry = null!, _roomName = null!, _roomPassword = null!;
    private OptionButton _mode = null!, _map = null!, _bestOf = null!;
    private Label _rewardHint = null!;
    private bool _rewardsConfigured;
    private SpinBox _capacity = null!, _bots = null!, _score = null!, _tickets = null!;
    private CheckBox _friendly = null!;
    private VBoxContainer _listPane = null!, _createPane = null!;
    private GridContainer? _mobileListActions, _mobileCreateActions;
    private ScrollContainer? _mobileScroll;
    private GridContainer? _roomFormGrid;
    private readonly List<Button> _buttons = [];
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();

    public void Build(Main main, string address, string password, string name, int team, WizardType wizardType)
    {
        _main = main; _address = address; _serverPassword = password; _playerName = name; _wizardType = wizardType;
        Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = Ui.ModalDim }; AddChild(shade); Ui.Full(shade);
        var safeMargin = new MarginContainer();
        AddChild(safeMargin); Ui.Full(safeMargin);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            safeMargin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 8 : 15);
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        var panelStyle = Ui.ModalPanel();
        panelStyle.ContentMarginTop = panelStyle.ContentMarginBottom = 8;
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        safeMargin.AddChild(panel);
        var layout = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 5);
        panel.AddChild(layout);
        var scroll = new ContentScrollContainer { Name = "OptionALobbyContent",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        layout.AddChild(scroll);
        Ui.VisibleVerticalScroll(scroll);
        var fixedActions = new VBoxContainer { Name = "OptionALobbyActions" };
        fixedActions.AddThemeConstantOverride("separation", 6);
        layout.AddChild(fixedActions);
        _mobileScroll = scroll;
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", Ui.IsMobile ? 6 : 9); scroll.AddChild(box);
        _heading = Ui.Label("SALAS", 28, Ui.Gold); _heading.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_heading);
        var addressInfo = Ui.Label(address, 15, Ui.Muted);
        addressInfo.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        box.AddChild(addressInfo);
        _serverAddressLabel = addressInfo;
        // Al alojar en PC, se muestra el enlace WSS renovado que los móviles
        // deben usar para enviar la sesión Google y cobrar recompensas.
        if (!Ui.IsMobile && address.StartsWith("127.0.0.1:", StringComparison.Ordinal))
        {
            string? publicAddress = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://"));
            if (publicAddress?.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) == true)
            {
                var publicLabel = Ui.Label("ENLACE SEGURO PARA MÓVIL Y RECOMPENSAS:\n" + publicAddress, 13, Ui.Gold);
                publicLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
                box.AddChild(publicLabel);
                var copyPublic = Ui.Button("Copiar enlace WSS para el móvil",
                    () => DisplayServer.ClipboardSet(publicAddress));
                box.AddChild(copyPublic);
            }
        }
        // El inventario siempre queda disponible en la barra inferior fija de la opción A.
        var teamRow = new HBoxContainer(); teamRow.AddThemeConstantOverride("separation", 12); box.AddChild(teamRow);
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 22);
        box.AddChild(headerRow); box.MoveChild(headerRow, 0);
        _heading.Reparent(headerRow);
        teamRow.Reparent(headerRow);
        _heading.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        teamRow.AddChild(Ui.Label("Tu equipo", 16, Ui.Muted));
        _teamChoice = Ui.TeamChoice(team); _teamChoice.Name = "RoomTeamChoice"; teamRow.AddChild(_teamChoice);
        _listPane = new VBoxContainer(); _listPane.AddThemeConstantOverride("separation", Ui.IsMobile ? 4 : 8); box.AddChild(_listPane);
        _list = new ItemList { CustomMinimumSize = new(0, Ui.IsMobile ? 100 : 160), SelectMode = ItemList.SelectModeEnum.Single };
        _listPane.AddChild(_list); _list.ItemActivated += _ => Join();
        _emptyRooms = Ui.Label("No hay salas disponibles. Pulsa «Crear sala» para abrir una partida.", 15, Ui.Muted);
        _emptyRooms.HorizontalAlignment = HorizontalAlignment.Center;
        _emptyRooms.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _listPane.AddChild(_emptyRooms);
        var pages = new HBoxContainer();
        pages.AddThemeConstantOverride("separation", 10);
        _listPane.AddChild(pages);
        _roomPagePrevious = Ui.Button("◀  Anteriores", () => ShowRoomPage(_roomPageStart - VisibleRoomsPerPage));
        _roomPagePrevious.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _roomPagePrevious.CustomMinimumSize = new(0, Ui.IsMobile ? 48 : 44);
        pages.AddChild(_roomPagePrevious);
        _roomPageLabel = Ui.Label("", 15, Ui.Gold);
        _roomPageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _roomPageLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pages.AddChild(_roomPageLabel);
        _roomPageNext = Ui.Button("Siguientes  ▶", () => ShowRoomPage(_roomPageStart + VisibleRoomsPerPage));
        _roomPageNext.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _roomPageNext.CustomMinimumSize = new(0, Ui.IsMobile ? 48 : 44);
        pages.AddChild(_roomPageNext);
        _objective = Ui.Label("", 15, Ui.Gold); _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart; _listPane.AddChild(_objective);
        _list.ItemSelected += _ => ShowObjective();
        var serverKeyLabel = Ui.Label("1. Contraseña del SERVIDOR (solo si está protegido)", 14, Ui.Muted);
        serverKeyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; _listPane.AddChild(serverKeyLabel);
        _serverPasswordEntry = new LineEdit
        {
            Name = "ServerPasswordInput",
            Secret = true, MaxLength = 128, Text = password,
            PlaceholderText = "Clave del anfitrión, distinta de la clave de la sala",
            CustomMinimumSize = new(0, 42)
        };
        _serverPasswordEntry.TextChanged += value => _serverPassword = value;
        _listPane.AddChild(_serverPasswordEntry);
        var roomKeyLabel = Ui.Label("2. Contraseña de la SALA seleccionada (si tiene)", 14, Ui.Muted);
        roomKeyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart; _listPane.AddChild(roomKeyLabel);
        _joinPassword = new LineEdit { Name = "RoomPasswordInput", Secret = true, MaxLength = 128,
            PlaceholderText = "Clave utilizada al crear la sala",
            CustomMinimumSize = new(0, 42) };
        _listPane.AddChild(_joinPassword);
        var actions = new GridContainer { Columns = 4 }; actions.AddThemeConstantOverride("h_separation", 12);
        actions.AddThemeConstantOverride("v_separation", 6); _listPane.AddChild(actions);
        ActionButton(actions, "Entrar", Join, true); ActionButton(actions, "Crear sala", () => SwitchPane(true));
        ActionButton(actions, "Actualizar", Refresh);
        _createPane = new VBoxContainer { Visible = false }; _createPane.AddThemeConstantOverride("separation", 12); box.AddChild(_createPane);
        var grid = new GridContainer { Name = "RoomCreationFields", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _roomFormGrid = grid;
        grid.Resized += UpdateRoomResponsive;
        grid.AddThemeConstantOverride("h_separation", 18);
        grid.AddThemeConstantOverride("v_separation", Ui.IsMobile ? 5 : 7); _createPane.AddChild(grid);
        _roomName = new LineEdit { Text = "Sala de " + (name.Length > 24 ? name[..24] : name), MaxLength = 32, CustomMinimumSize = new(0, 42) };
        Row(grid, "Nombre", _roomName);
        _mode = new OptionButton { CustomMinimumSize = new(0, 42) };
        _mode.AddItem("Guerra de puntos", (int)GameMode.Warfare); _mode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _mode.AddItem("Duelo", (int)GameMode.Duel); _mode.AddItem("Supervivencia", (int)GameMode.Defense); Row(grid, "Modo", _mode);
        _map = new OptionButton { Name = "RoomMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice)) _map.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        Row(grid, "Mapa", _map);
        _capacity = Number(2, 16, 8); Row(grid, "Plazas (incluidos los bots)", _capacity);
        _bots = Number(0, 7, 0); Row(grid, "IA (aliados en Defensa)", _bots);
        _bestOf = new OptionButton { CustomMinimumSize = new(0, 42), FitToLongestItem = false,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        _bestOf.AddItem("Partida única (sin sala de espera)", 1);
        _bestOf.AddItem("Mejor de 5 · gana 3", 5);
        _bestOf.AddItem("Mejor de 7 · gana 4", 7);
        _bestOf.AddItem("Mejor de 9 · gana 5", 9);
        _bestOf.Select(1);
        Row(grid, "Formato de serie", _bestOf);
        _rewardHint = Ui.Label("", 14, Ui.Gold);
        _rewardHint.AutowrapMode = TextServer.AutowrapMode.WordSmart; _createPane.AddChild(_rewardHint);
        _bots.ValueChanged += _ => UpdateRewardsHint();
        _bestOf.ItemSelected += _ => UpdateRewardsHint();
        _friendly = new CheckBox { Text = "Permitir daño entre aliados", ButtonPressed = true }; Row(grid, "Fuego amigo", _friendly);
        _score = Number(1, 200, 15); Row(grid, "Puntos para ganar (arena / duelo)", _score);
        _tickets = Number(5, 1000, 75); Row(grid, "Tickets por equipo (guerra)", _tickets);
        _roomPassword = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Dejar vacío para sala abierta", CustomMinimumSize = new(0, 42) };
        Row(grid, "Clave de la SALA", _roomPassword);
        _creationObjective = Ui.Label("", 14, Ui.Gold); _creationObjective.AutowrapMode = TextServer.AutowrapMode.WordSmart; _createPane.AddChild(_creationObjective);
        _mode.ItemSelected += _ =>
        {
            if ((GameMode)_mode.GetSelectedId() == GameMode.Defense) _map.Select(_map.GetItemIndex((int)ArenaMapId.Bastion));
            else if ((ArenaMapId)_map.GetSelectedId() == ArenaMapId.Bastion) _map.Select(0);
            bool duel = (GameMode)_mode.GetSelectedId() == GameMode.Duel;
            _bestOf.Disabled = (GameMode)_mode.GetSelectedId() == GameMode.Arena;
            UpdateRewardsHint();
            _capacity.Editable = !duel; if (duel) _capacity.Value = 2;
            _bots.MaxValue = _capacity.Value - 1;
            ShowCreationObjective();
        };
        _score.ValueChanged += _ => ShowCreationObjective(); _tickets.ValueChanged += _ => ShowCreationObjective();
        _map.ItemSelected += _ => ShowCreationObjective();
        ShowCreationObjective();
        UpdateRewardsHint();
        _capacity.ValueChanged += value => _bots.MaxValue = value - 1;
        var creation = new GridContainer { Columns = 3 }; creation.AddThemeConstantOverride("h_separation", 12);
        creation.AddThemeConstantOverride("v_separation", 6); _createPane.AddChild(creation);
        ActionButton(creation, "Crear y entrar", Create, true); ActionButton(creation, "Volver a salas", () => SwitchPane(false));
        _heading.TooltipText = "Cada sala tiene su partida. Las salas vacías se retiran tras 2 minutos; si el servidor se llena, se sustituyen las salas abandonadas.";
        _status = Ui.Label("Cargando salas…", 15, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new(0, 36); box.AddChild(_status);
        var back = Ui.Button("Volver al launcher", Close); back.Name = "RoomLobbyBack";
        back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(back);
        // Acciones en el mismo lugar en lista y creación; el botón de volver
        // está en la misma barra inferior de cada sección.
        _mobileListActions = actions;
        _mobileCreateActions = creation;
        _status.Reparent(fixedActions);
        back.Reparent(actions);
        var backCreate = Ui.Button("Volver al launcher", Close); backCreate.Name = "RoomLobbyBack";
        backCreate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        creation.AddChild(backCreate);
        actions.Reparent(fixedActions);
        creation.Reparent(fixedActions);
        _mobileCreateActions.Visible = false;
        foreach (var control in new Control[] { _teamChoice, _serverPasswordEntry, _joinPassword, _roomName, _roomPassword,
            _mode, _map, _bestOf, _capacity, _bots, _score, _tickets, _friendly })
            Ui.TouchTarget(control, Ui.IsMobile ? 48 : 46);
        _list.CustomMinimumSize = new Vector2(0, Ui.IsMobile ? 100 : 160);
        ArcanaMenuChrome.Attach(this, safeMargin, _main, "Salas PvP", 1, _main.NavigateOptionAMenu);
        GetViewport().SizeChanged += UpdateRoomResponsive;
        UpdateRoomResponsive();
        Refresh();
    }
    private void UpdateRoomResponsive()
    {
        if (_roomFormGrid == null) return;
        float width = GetViewport().GetVisibleRect().Size.X - (Ui.IsMobile ? 65 : 115);
        _roomFormGrid.Columns = ResponsiveLayout.Columns(_roomFormGrid.Size.X > 0 ? _roomFormGrid.Size.X : width, 260, 3, 18);
        bool compact = GetViewport().GetVisibleRect().Size.Y <= 600 || width < 720;
        if (_mobileListActions != null)
            _mobileListActions.Columns = ResponsiveLayout.Columns(width, compact ? 110 : 175, 4, 12);
        if (_mobileCreateActions != null)
            _mobileCreateActions.Columns = ResponsiveLayout.Columns(width, compact ? 160 : Ui.IsMobile ? 195 : 230, 3, 12);
        foreach (var group in new[] { _mobileListActions, _mobileCreateActions })
            if (group != null)
                foreach (var button in group.GetChildren().OfType<Button>())
                {
                    button.CustomMinimumSize = new(0, compact ? Ui.IsMobile ? 60 : 48 : Ui.IsMobile ? 66 : 48);
                    button.AddThemeFontSizeOverride("font_size", compact ? 16 : 20);
                    if (button.Name == "RoomLobbyBack") button.Text = compact ? "Volver" : "Volver al launcher";
                }
        _status.CustomMinimumSize = new(0, compact ? 0 : 36);
        _status.AddThemeFontSizeOverride("font_size", compact ? 14 : 15);
    }
    internal bool IsBusyForSmoke => _busy;
    internal string StatusForSmoke => _status.Text;
    internal Button[] MobileRoomButtons => (_createPane.Visible ? _mobileCreateActions : _mobileListActions)?
        .GetChildren().OfType<Button>().Where(button => button.Visible).ToArray() ?? [];
    internal Button? MobileBackButton => FindChildren("*", "Button", true, false)
        .OfType<Button>().FirstOrDefault(button => button.Name == "RoomLobbyBack" && button.IsVisibleInTree());

    private static SpinBox Number(int min, int max, int value) => new() { MinValue = min, MaxValue = max,
        Value = value, Step = 1, CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private static void Row(GridContainer grid, string title, Control control)
    {
        var field = new VBoxContainer { Name = "RoomField" + control.GetClass(), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        field.AddThemeConstantOverride("separation", 4);
        grid.AddChild(field);
        var label = Ui.Label(title, 15, Ui.Muted);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        field.AddChild(label);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (control is OptionButton choice)
        {
            choice.FitToLongestItem = false;
            choice.ClipText = true;
            choice.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        if (control is CheckBox check) check.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        field.AddChild(control);
    }
    private void ActionButton(Node parent, string text, Action action, bool primary = false)
    {
        var button = Ui.Button(text, action, primary); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        parent.AddChild(button); _buttons.Add(button);
    }
    private void SwitchPane(bool create)
    {
        _listPane.Visible = !create;
        _createPane.Visible = create;
        if (_serverAddressLabel != null) _serverAddressLabel.Visible = !create;
        if (_mobileListActions != null) _mobileListActions.Visible = !create;
        if (_mobileCreateActions != null) _mobileCreateActions.Visible = create;
        if (_mobileScroll != null) _mobileScroll.ScrollVertical = 0;
        _status.Text = "";
    }
    public void SetBusy(bool busy)
    {
        _busy = busy; foreach (var b in _buttons) b.Disabled = busy;
        _list.MouseFilter = busy ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
        _roomName.Editable = _roomPassword.Editable = _joinPassword.Editable =
            _serverPasswordEntry.Editable = !busy;
        _mode.Disabled = _map.Disabled = _friendly.Disabled = _teamChoice.Disabled = busy;
        _bestOf.Disabled = busy || (GameMode)_mode.GetSelectedId() == GameMode.Arena;
        _bots.Editable = _score.Editable = _tickets.Editable = !busy;
        _capacity.Editable = !busy && (GameMode)_mode.GetSelectedId() != GameMode.Duel;
        ShowCreationObjective();
    }
    private bool Active => GodotObject.IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion();
    public void ShowError(string error)
    {
        SetBusy(false);
        _status.Text = error;
        _status.AddThemeColorOverride("font_color", Ui.Red);
        // El error de conexión se muestra AQUÍ, no en el launcher oculto detrás.
        if (!Ui.IsMobile && error.Contains("Contraseña de la sala", StringComparison.OrdinalIgnoreCase))
            _joinPassword.GrabFocus();
        else if (!Ui.IsMobile && error.Contains("Contraseña del servidor", StringComparison.OrdinalIgnoreCase))
            _serverPasswordEntry.GrabFocus();
    }
    private void Loading(string text) { SetBusy(true); _status.Text = text; _status.AddThemeColorOverride("font_color", Ui.Muted); }
    private async void Refresh()
    {
        if (_busy) return;
        Loading("Consultando salas…");
        try
        {
            string target = _address;
            RoomDirectory directory = await RoomBrowser.ListAsync(target, _lifetime.Token);
            // Al descubrir por LAN un servidor que requiere Firebase, sustituir
            // automáticamente su ws:// por la WSS publicada. Así tanto Entrar
            // como Crear sala usan HTTPS y no transmiten claves por LAN.
            var endpoint = ArenaConnection.Address(target);
            if (directory.Server.RewardsAvailable && endpoint.Scheme == "ws" && !endpoint.IsLoopback &&
                _main.Accounts.IsDirectFirebaseSession)
            {
                string official = await PublicAccountServer.Resolve("arcana-arena-f2997", _lifetime.Token);
                RoomDirectory verified = await RoomBrowser.ListAsync(official, _lifetime.Token);
                if (!PublicAccountServer.SameInstance(directory.Server, verified.Server))
                    throw new InvalidOperationException("La sala LAN no coincide con el servidor oficial de Firebase. Revisa el alojamiento.");
                directory = verified;
                target = official;
            }
            string secureTarget = target;
            RoomDirectory result = directory;
            _main.Post(() =>
            {
                if (!Active) return;
                _address = secureTarget;
                if (_serverAddressLabel != null) _serverAddressLabel.Text = secureTarget;
                _heading.Text = "SALAS  /  " + result.Server.Name;
                string? previous = Selected()?.Id; _rooms = result.Rooms;
                _serverPasswordRequired = result.Server.PasswordRequired;
                _rewardsConfigured = result.Server.RewardsAvailable;
                UpdateRewardsHint();
                int at = Array.FindIndex(_rooms, r => r.Id == previous);
                if (at >= 0) _roomPageStart = at / VisibleRoomsPerPage * VisibleRoomsPerPage;
                else _roomPageStart = 0;
                ShowRoomPage(_roomPageStart);
                if (at >= 0) _list.Select(at - _roomPageStart);
                ShowObjective();
                SetBusy(false); _status.Text = $"{_rooms.Length} sala(s). Selecciona una o crea tu propia partida." +
                    (secureTarget.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) ? " · Conexión segura" : "");
            });
        }
        catch (Exception e) { Failure(e); }
    }
    private void ShowRoomPage(int requestedStart)
    {
        _roomPageStart = Math.Clamp(requestedStart, 0,
            Math.Max(0, (_rooms.Length - 1) / VisibleRoomsPerPage * VisibleRoomsPerPage));
        _list.Clear();
        _emptyRooms.Visible = _rooms.Length == 0;
        foreach (var room in _rooms.Skip(_roomPageStart).Take(VisibleRoomsPerPage))
            _list.AddItem($"{room.Name}{(room.PasswordRequired ? " · CON CLAVE" : "")}  ·  {Ui.ModeName(room.Mode)}  ·  {ArenaMap.Get(room.Map).Name}  ·  {room.Players}/{room.MaxPlayers} plazas" +
                (room.InProgress && room.BestOf > 1 ? " · EN PARTIDA" : " · DISPONIBLE"));
        if (_list.ItemCount > 0) _list.Select(0);
        if (_roomPagePrevious != null) _roomPagePrevious.Disabled = _roomPageStart == 0;
        if (_roomPageNext != null) _roomPageNext.Disabled = _roomPageStart + VisibleRoomsPerPage >= _rooms.Length;
        if (_roomPageLabel != null) _roomPageLabel.Text = _rooms.Length == 0
            ? "0 salas"
            : $"{_roomPageStart + 1}–{Math.Min(_rooms.Length, _roomPageStart + VisibleRoomsPerPage)} / {_rooms.Length}";
        ShowObjective();
    }
    private RoomInfo? Selected()
    {
        var selected = _list.GetSelectedItems();
        int index = selected.Length > 0 ? _roomPageStart + selected[0] : -1;
        return index >= 0 && index < _rooms.Length ? _rooms[index] : null;
    }
    private void ShowObjective()
    {
        var room = Selected();
        // Evita reutilizar sin querer la clave de otra sala tras cambiar de selección.
        if ((room?.Id ?? "") != _selectedRoomId)
        {
            _selectedRoomId = room?.Id ?? "";
            if (_joinPassword != null) _joinPassword.Text = "";
        }
        _objective.Text = room == null ? "Selecciona una sala para ver su objetivo."
            : MatchRules.Objective(room.Mode, room.ScoreLimit, room.Tickets, room.Map) +
                (room.PasswordRequired ? "\nEsta sala tiene una clave propia; no es la del servidor." : "\nSala sin contraseña propia.");
    }
    private void UpdateRewardsHint()
    {
        if (_rewardHint == null) return;
        _rewardHint.Text = !_rewardsConfigured
            ? "SIN RECOMPENSAS: el servidor no tiene Firebase habilitado. Configura el servidor con cuentas para probar premios."
            : (GameMode)_mode.GetSelectedId() == GameMode.Defense
                ? "Defensa: un objeto cada cinco minutos activos por humano con Google, hasta cuatro. Los bots aliados están permitidos."
                : _bots.Value > 0
                ? "SIN RECOMPENSAS: las partidas con IA no conceden premios."
                : (GameMode)_mode.GetSelectedId() == GameMode.Arena
                    ? "Sin premios por equipos: selecciona Guerra o Duelo para recompensas PvP."
                    : "Recompensas al ganar la serie: todos con Google, equipos humanos equilibrados (1v1–4v4), actividad mínima de 15 s.";
    }

    private void ShowCreationObjective()
    {
        var mode = (GameMode)_mode.GetSelectedId(); var map = (ArenaMapId)_map.GetSelectedId();
        var definition = ArenaMap.Get(map);
        if (map == ArenaMapId.Bastion) { _mode.Select(_mode.GetItemIndex((int)GameMode.Defense)); mode = GameMode.Defense; }
        for (int i = 0; i < _mode.ItemCount; i++)
        {
            var choice = (GameMode)_mode.GetItemId(i);
            _mode.SetItemDisabled(i, map != ArenaMapId.Bastion && choice != GameMode.Defense && !definition.SupportsMode(choice));
        }
        if (!definition.SupportsMode(mode)) { mode = GameMode.Warfare; _mode.Select(_mode.GetItemIndex((int)mode)); }
        _capacity.MinValue = mode == GameMode.Defense ? 1 : 2;
        _capacity.MaxValue = mode == GameMode.Defense ? 4 : 16;
        _bots.MaxValue = _capacity.Value - 1;
        _bestOf.Disabled = _busy || mode is GameMode.Defense or GameMode.Arena;
        _teamChoice.Disabled = _busy || mode == GameMode.Defense;
        _friendly.Disabled = _busy || mode == GameMode.Defense;
        if (mode == GameMode.Defense) _friendly.ButtonPressed = false;
        if (mode == GameMode.Duel) _capacity.Value = 2;
        if (mode == GameMode.Defense) _bestOf.Select(0);
        _capacity.Editable = !_busy && mode != GameMode.Duel;
        _tickets.Editable = !_busy && mode == GameMode.Warfare && definition.HoldSeconds == 0 && !definition.SequentialCapture;
        _score.Editable = !_busy && mode is GameMode.Duel or GameMode.Arena;
        _creationObjective.Text = MatchRules.Objective(mode, (int)_score.Value, (int)_tickets.Value, map);
        UpdateRewardsHint();
    }
    public void OpenCreation() => SwitchPane(true);
    private void Join()
    {
        if (_busy) return;
        var room = Selected(); if (room == null) { ShowError("Selecciona una sala."); return; }
        if (room.InProgress && room.BestOf > 1) { ShowError("Esta serie está en curso. Actualiza las salas para entrar en la siguiente."); return; }
        if (room.Players >= room.MaxPlayers) { ShowError("La sala está llena. Crea otra o actualiza la lista."); return; }
        if (_serverPasswordRequired && _serverPassword.Length == 0)
        {
            ShowError("Falta la contraseña del SERVIDOR (campo 1). Es distinta de la de la sala.");
            return;
        }
        string roomPassword = _joinPassword.Text;
        if (room.PasswordRequired && roomPassword.Length == 0)
        {
            ShowError("Falta la contraseña de la SALA (campo 2). Usa la clave con la que se creó.");
            return;
        }
        string serverPassword = _serverPasswordEntry.Text;
        Loading("Entrando en " + room.Name + "…");
        _ = _main.ConnectOnline(_address, _playerName, Ui.SelectedTeam(_teamChoice),
            serverPassword, room.Id, roomPassword, _wizardType, error => { if (Active) ShowError(error); });
    }
    private async void Create()
    {
        if (_busy) return;
        int team = Ui.SelectedTeam(_teamChoice);
        var request = new CreateRoomRequest { Name = _roomName.Text, Mode = (GameMode)_mode.GetSelectedId(), Map = (ArenaMapId)_map.GetSelectedId(),
            MaxPlayers = (int)_capacity.Value, Bots = (int)_bots.Value, FriendlyFire = _friendly.ButtonPressed,
            ScoreLimit = (int)_score.Value, Tickets = (int)_tickets.Value,
            BestOf = (GameMode)_mode.GetSelectedId() is GameMode.Arena or GameMode.Defense ? 1 : _bestOf.GetSelectedId(),
            DefenseFlow = DefenseFlow.Continuous,
            Password = _roomPassword.Text, ServerPassword = _serverPasswordEntry.Text };
        Loading("Creando sala…");
        try
        {
            var room = await RoomBrowser.CreateAsync(_address, request, _lifetime.Token);
            _main.Post(() =>
            {
                if (!Active) return;
                _ = _main.ConnectOnline(_address, _playerName, team, request.ServerPassword,
                    room.Id, request.Password, _wizardType,
                    error => { if (Active) ShowError("Sala creada, pero no se pudo entrar: " + error); });
            });
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
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateRoomResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}
