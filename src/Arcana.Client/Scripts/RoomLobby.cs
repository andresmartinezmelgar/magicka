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
    private ElementTextLabel _objective = null!, _creationObjective = null!;
    private RoomInfo[] _rooms = [];
    private int _roomPageStart;
    private const int VisibleRoomsPerPage = 3;
    private ElementTextLabel? _roomPageLabel;
    private Button? _roomPagePrevious, _roomPageNext;
    private ScrollChoiceList _list = null!;
    private ElementTextLabel _status = null!, _heading = null!, _emptyRooms = null!;
    private LineEdit _joinPassword = null!, _serverPasswordEntry = null!, _roomName = null!, _roomPassword = null!;
    private OptionButton _mode = null!, _map = null!, _bestOf = null!;
    private ElementTextLabel _rewardHint = null!;
    private bool _rewardsConfigured;
    private SpinBox _capacity = null!, _bots = null!, _score = null!, _tickets = null!;
    private CheckBox _friendly = null!;
    private VBoxContainer _listPane = null!, _createPane = null!;
    private GridContainer? _mobileListActions, _mobileCreateActions;
    private ScrollContainer? _mobileScroll;
    private GridContainer? _roomFormGrid;
    private readonly List<Button> _buttons = [];
    private readonly List<Button> _matchButtons = [];
    private bool _serverStarting, _startupRetry;
    private string _startupMessage = "";
    private bool _busy;
    private readonly CancellationTokenSource _lifetime = new();
    internal bool UsesPvpDirectory { get; private set; }

    public void Build(Main main, string address, string password, string name, int team, WizardType wizardType,
        bool refreshRooms = true)
    {
        _main = main; UsesPvpDirectory = address == PvpServer.Automatic; _address = address; _serverPassword = password; _playerName = name; _wizardType = wizardType;
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
        // Al alojar en PC, se muestra el enlace WSS renovado que los móviles
        // deben usar para enviar la sesión Google y cobrar recompensas.
        if (!Ui.IsMobile && address.StartsWith("127.0.0.1:", StringComparison.Ordinal))
        {
            string? publicAddress = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://"));
            if (publicAddress?.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) == true)
            {
                var publicLabel = Ui.Label(PvpServer.DisplayText("ENLACE SEGURO PARA MÓVIL Y RECOMPENSAS:\n" + publicAddress), 13, Ui.Gold);
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
        _list = new ScrollChoiceList { Name = "RoomListScroll", CustomMinimumSize = new(0, Ui.IsMobile ? 116 : 160) };
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
        _mode = new ScrollOptionButton { CustomMinimumSize = new(0, 42) };
        _mode.AddItem("Guerra de puntos", (int)GameMode.Warfare); _mode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _mode.AddItem("Duelo", (int)GameMode.Duel); _mode.AddItem("Supervivencia", (int)GameMode.Defense); Row(grid, "Modo", _mode);
        _map = new ScrollOptionButton { Name = "RoomMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice)) _map.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        Row(grid, "Mapa", _map);
        _capacity = Number(2, 16, 8); Row(grid, "Plazas (incluidos los bots)", _capacity);
        _bots = Number(0, 7, 0); Row(grid, "IA (aliados en Defensa)", _bots);
        _bestOf = new ScrollOptionButton { CustomMinimumSize = new(0, 42), FitToLongestItem = false,
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
        _list.CustomMinimumSize = new Vector2(0, Ui.IsMobile ? 116 : 160);
        ArcanaMenuChrome.Attach(this, safeMargin, _main, "Salas PvP", 1, _main.NavigateOptionAMenu);
        GetViewport().SizeChanged += UpdateRoomResponsive;
        UpdateRoomResponsive();
        if (refreshRooms) Refresh();
    }
    internal bool IsBusyForSmoke => _busy;
    internal string StatusForSmoke => _status.Text;
    internal Button[] MobileRoomButtons => (_createPane.Visible ? _mobileCreateActions : _mobileListActions)?
        .GetChildren().OfType<Button>().Where(button => button.Visible).ToArray() ?? [];
    internal Button? MobileBackButton => FindChildren("*", "Button", true, false)
        .OfType<Button>().FirstOrDefault(button => button.Name == "RoomLobbyBack" && button.IsVisibleInTree());

    private bool Active => GodotObject.IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion();
}
