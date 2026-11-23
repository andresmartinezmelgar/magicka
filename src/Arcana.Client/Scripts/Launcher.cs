using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher : Control
{
    private Main _main = null!;
    private LineEdit _password = null!, _hostName = null!;
    private OptionButton _team = null!, _practiceMode = null!, _practiceMap = null!;
    internal string PlayerName { get; private set; } = "Mago";
    private SpinBox _bots = null!, _discoveryPort = null!, _hostPort = null!, _hostDiscovery = null!;
    private ElementTextLabel _status = null!;
    private Button? _continueSurvival;
    private ScrollChoiceList _servers = null!;
    private Button _connect = null!, _refresh = null!, _stopHost = null!, _firewall = null!, _officialOnline = null!;
    private ElementTextLabel _hostInfo = null!;
    private RoomLobby? _lobby;
    private readonly LocalServerHost _host = new();
    internal WizardType SelectedWizardType { get; private set; }
    internal bool Hosting => _host.Running || _busy;
    private TabContainer _tabs = null!;
    private LanServer[] _lan = [];
    private CancellationTokenSource? _search;
    private CancellationTokenSource? _onlineSearch;
    private const string FirebaseProjectId = "arcana-arena-f2997";
    private readonly ConfigFile _config = new();
    private bool _busy;
    private VBoxContainer _homeMenu = null!;
    private VBoxContainer _playBody = null!;
    private Button _menuBack = null!;
    private int _page;
    private ScrollContainer? _mobileScroll;
    internal bool RefreshRoomsOnBuild { private get; set; } = true;
    internal RoomLobby? RoomsForVerification => GodotObject.IsInstanceValid(_lobby) ? _lobby : null;

    public void Build(Main main)
    {
        _main = main; Theme = Ui.Theme(); Ui.Full(this);
        _config.Load("user://launcher.cfg");
        PlayerName = _config.GetValue("player", "name", "Mago").AsString();
        int savedType = _config.GetValue("player", "wizard_type", 0).AsInt32();
        SelectedWizardType = Enum.IsDefined((WizardType)savedType) ? (WizardType)savedType : default;
        var shade = new ColorRect { Color = new Color(0.025f, 0.045f, 0.075f, 0.64f), MouseFilter = MouseFilterEnum.Ignore }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 8 : 32);
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 52); margin.AddChild(columns);
        // La columna decorativa del diseño antiguo permanecía siempre oculta.
        // Se elimina su construcción para no cargar nodos, etiquetas y ocho iconos innecesarios.
        // En móvil, el formulario se desplaza, pero sus acciones quedan siempre al alcance del pulgar.
        var rightColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddChild(rightColumn);
        var rightScroll = new ContentScrollContainer { CustomMinimumSize = new(Ui.IsMobile ? 0 : 570, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        rightColumn.AddChild(rightScroll);
        _mobileScroll = rightScroll;
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; rightScroll.AddChild(panel);
        var formStyle = Ui.Panel(new Color(0.035f, 0.057f, 0.093f, 0.97f), 16);
        formStyle.ContentMarginTop = formStyle.ContentMarginBottom = Ui.IsMobile ? 5 : 12;
        if (Ui.IsMobile) formStyle.ContentMarginLeft = formStyle.ContentMarginRight = 7; panel.AddThemeStyleboxOverride("panel", formStyle);
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", Ui.IsMobile ? 3 : 6); panel.AddChild(form);
        _homeMenu = new VBoxContainer();
        _homeMenu.AddThemeConstantOverride("separation", Ui.IsMobile ? 5 : 10);
        form.AddChild(_homeMenu);
        _homeMenu.AddChild(Ui.Button("PvP", OpenPvp, true));
        _homeMenu.AddChild(Ui.Button("Versus IA", () => OpenPage(2), true));
        var profile = Ui.Button("Mi perfil", _main.OpenAccountProfile); profile.Name = "HomeProfile";
        _homeMenu.AddChild(profile);
         _playBody = new VBoxContainer(); _playBody.AddThemeConstantOverride("separation", 6); form.AddChild(_playBody);
        _menuBack = Ui.Button("Volver", () => OpenPage(0)); form.AddChild(_menuBack);
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); _playBody.AddChild(identity);
        var teamColumn = new VBoxContainer { CustomMinimumSize = new(172, 0) }; identity.AddChild(teamColumn);
        teamColumn.AddChild(Ui.Label("Equipo", 14, Ui.Muted));
        _team = Ui.TeamChoice(_config.GetValue("player", "team", -1).AsInt32());
        _team.Name = "LauncherTeamChoice";
        teamColumn.AddChild(_team);
        _tabs = new TabContainer(); _playBody.AddChild(_tabs);
        var online = TabBox("Online");
        online.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        online.AddChild(Ui.Label(PvpServer.Name, 20, Ui.Gold));
        var onlineHelp = Ui.Label("Consulta las salas disponibles para entrar o crear una partida.", 15, Ui.Muted);
        onlineHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        online.AddChild(onlineHelp);
        var lan = TabBox("LAN");
        var lanActions = new HBoxContainer(); lanActions.AddThemeConstantOverride("separation", 10); lan.AddChild(lanActions);
        _refresh = Ui.Button("Buscar servidores", StartSearch); _refresh.SizeFlagsHorizontal = SizeFlags.ExpandFill; lanActions.AddChild(_refresh);
        // El servidor Windows publica la URL WSS efímera en Firestore. El móvil
        // la descubre en cada pulsación: nunca hay que copiar la URL de Cloudflare.
        _officialOnline = Ui.Button("Servidor online con Google · conexión automática", ConnectOfficialOnline, true);
        _officialOnline.Name = "OnlineFirebaseButton";
        _officialOnline.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _officialOnline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _officialOnline.CustomMinimumSize = new(0, Ui.IsMobile ? 60 : 46);
        lan.AddChild(_officialOnline);
        _discoveryPort = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = Protocol.DiscoveryPort, CustomMinimumSize = new(120, 45) }; lanActions.AddChild(_discoveryPort);
        _servers = new ScrollChoiceList { Name = "LanServersScroll", CustomMinimumSize = new(0, 126) }; lan.AddChild(_servers);
        _servers.ItemActivated += _ => Connect();
        lan.AddChild(Ui.Label("Misma red local · Puerto de búsqueda UDP a la derecha", 13, Ui.Muted));
        var practice = TabBox("Práctica");
        practice.AddChild(Ui.Label("Simulación local: las mismas reglas del servidor.", 15, Ui.Muted));
        _practiceMode = new ScrollOptionButton { CustomMinimumSize = new(0, 45) };
        _practiceMode.AddItem("Guerra de puntos", (int)GameMode.Warfare);
        _practiceMode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _practiceMode.AddItem("Duelo", (int)GameMode.Duel);
        _practiceMode.AddItem("Supervivencia", (int)GameMode.Defense); practice.AddChild(_practiceMode);
        var firstWave = Ui.Button("Primera oleada guiada", _main.OpenFirstWaveTutorial);
        firstWave.Name = "PracticeFirstWaveTutorial";
        practice.AddChild(firstWave);
        practice.AddChild(Ui.Label("Mapa", 14, Ui.Muted));
        _practiceMap = new ScrollOptionButton { Name = "PracticeMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>()) _practiceMap.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        int savedMap = _config.GetValue("practice", "map", 0).AsInt32();
        _practiceMap.Select(Enum.IsDefined((ArenaMapId)savedMap) ? savedMap : 0); practice.AddChild(_practiceMap);
        var measureSurvival = Ui.Button("Medir rendimiento de Supervivencia", () => _main.StartRenderProfile(ArenaMapId.Bastion));
        measureSurvival.Name = "SurvivalPerformanceButton"; measureSurvival.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var practiceObjective = Ui.Label("", 14, Ui.Gold); practiceObjective.AutowrapMode = TextServer.AutowrapMode.WordSmart; practice.AddChild(practiceObjective);
        void Objective()
        {
            var map = ArenaMap.Get((ArenaMapId)_practiceMap.GetSelectedId());
            _practiceMode.Disabled = map.IsPractice;
            if (map.Id == ArenaMapId.Bastion) _practiceMode.Select(_practiceMode.GetItemIndex((int)GameMode.Defense));
            if (_bots != null)
            { _bots.MinValue = map.Id == ArenaMapId.Bastion ? 0 : 1; _bots.MaxValue = map.Id == ArenaMapId.Bastion ? 3 : 7; }
            if (map.IsPractice) _practiceMode.Select(_practiceMode.GetItemIndex((int)GameMode.Warfare));
            for (int i = 0; i < _practiceMode.ItemCount; i++)
            {
                var choice = (GameMode)_practiceMode.GetItemId(i);
                _practiceMode.SetItemDisabled(i, map.Id != ArenaMapId.Bastion && choice != GameMode.Defense && !map.SupportsMode(choice));
            }
            if (!map.SupportsMode((GameMode)_practiceMode.GetSelectedId())) _practiceMode.Select(_practiceMode.GetItemIndex((int)GameMode.Warfare));
            firstWave.Visible = (GameMode)_practiceMode.GetSelectedId() == GameMode.Defense;
            practiceObjective.Text = MatchRules.Objective((GameMode)_practiceMode.GetSelectedId(),
                (GameMode)_practiceMode.GetSelectedId() == GameMode.Duel ? 5 : 15, 75, map.Id);
        }
        _practiceMode.ItemSelected += _ =>
        {
            if ((GameMode)_practiceMode.GetSelectedId() == GameMode.Defense) _practiceMap.Select(_practiceMap.GetItemIndex((int)ArenaMapId.Bastion));
            else if ((ArenaMapId)_practiceMap.GetSelectedId() == ArenaMapId.Bastion) _practiceMap.Select(0);
            Objective();
        };
        _practiceMap.ItemSelected += _ => Objective();
        var botRow = new HBoxContainer(); practice.AddChild(botRow);
        botRow.AddChild(Ui.Label("Bots", 16));
        var botControls = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        botControls.AddThemeConstantOverride("separation", 12);
        botRow.AddChild(botControls);
        var fewerBots = Ui.Button("−", () => _bots.Value = Math.Max(_bots.MinValue, _bots.Value - 1));
        fewerBots.CustomMinimumSize = new Vector2(Ui.IsMobile ? 88 : 64, Ui.IsMobile ? 70 : 56);
        botControls.AddChild(fewerBots);
        _bots = new SpinBox { MinValue = 1, MaxValue = 7, Step = 1, Value = 3,
            Rounded = true, CustomMinimumSize = new Vector2(90, Ui.IsMobile ? 70 : 56), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _bots.GetLineEdit().ContextMenuEnabled = false;
        _bots.GetLineEdit().Editable = false;
        _bots.GetLineEdit().MouseFilter = MouseFilterEnum.Ignore;
        _bots.GetLineEdit().FocusMode = FocusModeEnum.None;
        botControls.AddChild(_bots);
        var moreBots = Ui.Button("+", () => _bots.Value = Math.Min(_bots.MaxValue, _bots.Value + 1));
        moreBots.CustomMinimumSize = new Vector2(Ui.IsMobile ? 88 : 64, Ui.IsMobile ? 70 : 56);
        botControls.AddChild(moreBots);
        _practiceMap.ItemSelected += _ => { _bots.Editable = (ArenaMapId)_practiceMap.GetSelectedId() != ArenaMapId.Practice; };
        _bots.Editable = (ArenaMapId)_practiceMap.GetSelectedId() != ArenaMapId.Practice; Objective();
        practice.AddChild(Ui.Label("El duelo usa un bot. Defensa permite jugar solo o con hasta tres aliados IA.", 13, Ui.Muted));
        practice.AddChild(measureSurvival);
        _continueSurvival = Ui.Button("Continuar supervivencia guardada", _main.ContinueSurvival, true);
        _continueSurvival.Name = "ContinueSurvival";
        _continueSurvival.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        practice.AddChild(_continueSurvival); UpdateSurvivalSave();
        
        var hosting = TabBox("Alojar");
        _hostName = new LineEdit { Text = _config.GetValue("host", "name", "Partida local").AsString(),
            MaxLength = 32, PlaceholderText = "Nombre del servidor", CustomMinimumSize = new(0, 42) }; hosting.AddChild(_hostName);
        var ports = new HBoxContainer(); ports.AddThemeConstantOverride("separation", 10); hosting.AddChild(ports);
        ports.AddChild(Ui.Label("Juego TCP", 14, Ui.Muted));
        _hostPort = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = _config.GetValue("host", "port", Protocol.GamePort).AsInt32(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill }; ports.AddChild(_hostPort);
        ports.AddChild(Ui.Label("LAN UDP", 14, Ui.Muted));
        _hostDiscovery = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = _config.GetValue("host", "discovery", Protocol.DiscoveryPort).AsInt32(),
            SizeFlagsHorizontal = SizeFlags.ExpandFill }; ports.AddChild(_hostDiscovery);
        _hostInfo = Ui.Label("Inicia el servidor incluido y crea salas desde el juego.\nEl servidor permanece activo mientras el juego esté abierto.", 14, Ui.Muted);
        _hostInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart; hosting.AddChild(_hostInfo);
        var hostActions = new HBoxContainer(); hosting.AddChild(hostActions);
        _stopHost = Ui.Button("Detener servidor", StopHost); _stopHost.Disabled = true; _stopHost.SizeFlagsHorizontal = SizeFlags.ExpandFill; hostActions.AddChild(_stopHost);
        var log = Ui.Button("Ver registros", () => OS.ShellOpen(ProjectSettings.GlobalizePath("user://logs"))); log.SizeFlagsHorizontal = SizeFlags.ExpandFill; hostActions.AddChild(log);
        _firewall = Ui.Button("Permitir LAN", ConfigureLan); _firewall.SizeFlagsHorizontal = SizeFlags.ExpandFill; hostActions.AddChild(_firewall);
        _firewall.Disabled = !OperatingSystem.IsWindows();
        _firewall.TooltipText = "Windows pedirá permiso de administrador para permitir estos dos puertos al servidor en redes privadas.";
        form.AddChild(Ui.Label("Contraseña del servidor (si tiene)", 14, Ui.Muted));
        _password = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Opcional", CustomMinimumSize = new(0, 42) }; _playBody.AddChild(_password);
        _connect = Ui.Button("Ver salas", Connect, true); _playBody.AddChild(_connect);
        _status = Ui.Label("El servidor de PvP se selecciona automáticamente.", 14, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new(0, 45); _playBody.AddChild(_status);
        _tabs.TabChanged += tab =>
        {
            UpdateConnectLabel();
            _password.Editable = tab != 2 && !(tab == 3 && _host.Running);
            if (tab == 3 && _host.Running) _password.Text = _hostPassword;
            if (tab == 1 && _lan.Length == 0 && !_busy) StartSearch();
        };
        _host.UnexpectedExit += code => _main.Post(() =>
        {
            SetBusy(false); _hostPassword = ""; UpdateConnectLabel();
            _hostInfo.Text = $"El servidor terminó (código {code}). Puedes iniciarlo otra vez.";
            ShowError("El servidor local ha terminado inesperadamente. Consulta los registros.");
        });
        if (Ui.IsMobile)
        {
            foreach (var control in new Control[] { _team,
                _practiceMode, _practiceMap, _discoveryPort, _password, _hostName })
                Ui.TouchTarget(control);
            _tabs.SetTabHidden(3, true);
            _tabs.SetTabDisabled(3, true);
            _tabs.CurrentTab = 2;
            UpdateConnectLabel();
        }
        ApplyOptionALayout(margin, columns, rightColumn, rightScroll, panel, form,
            identity);
        OpenPage(0);
    }

    public void UpdateAccountStatus() => _optionAChrome?.RefreshAccount();

    internal void SetWizardProfile(string name, WizardType type)
    {
        PlayerName = name;
        SelectedWizardType = type;
        Remember();
    }

    private void OpenPvp()
    {
        if (_busy || !_main.CanStartRegularMatch()) return;
        CloseRooms(); OpenPage(1); _password.Text = ""; Remember();
        OpenRooms(PvpServer.Automatic, "");
    }

    private void OpenPage(int page)
    {
        _page = page;
        _homeMenu.Visible = page == 0;
        if (page == 0) UpdateAccountStatus();
        _playBody.Visible = page != 0;
        ((Control)_team.GetParent().GetParent()).Visible = page == 1;
        ((Control)_team.GetParent()).Visible = page == 1;
        _status.Visible = page == 1;

        _menuBack.Visible = page != 0;
        _tabs.Visible = page is 1 or 2;
        _connect.Visible = page is 1 or 2;
        _password.Visible = page == 1;
        _tabs.SetTabHidden(0, page != 1);
        _tabs.SetTabHidden(1, page != 1);
        _tabs.SetTabHidden(2, page != 2);
        _tabs.SetTabHidden(3, Ui.IsMobile || page != 1);
        if (page != 1 && _onlineSearch != null)
        {
            CancelOnlineSearch();
            SetBusy(false);
        }
        if (page == 0) _menuBack.Visible = false;
        if (page == 2) _tabs.CurrentTab = 2;
        // PvP siempre comienza en el servidor configurado, aunque se hubiera
        // seleccionado LAN o Alojar en una visita anterior.
        if (page == 1) _tabs.CurrentTab = 0;
        if (_mobileScroll != null) _mobileScroll.ScrollVertical = 0;
        UpdateOptionAPage(page);
        UpdateConnectLabel();
    }

    internal int PageForVerification => _page;
    internal void OpenMobileSmokePage(int page) => OpenPage(page);
    internal Button[] MobilePrimaryButtons => _optionAPrimaryActions?
        .GetChildren().OfType<Button>().Where(button => button.Visible).ToArray() ?? [];

    private VBoxContainer TabBox(string name)
    {
        var box = new VBoxContainer { Name = name, CustomMinimumSize = new(0, 226), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 7); _tabs.AddChild(box); return box;
    }
    public void SetBusy(bool busy, string message = "")
    {
        if (busy) CancelSearch();
        _busy = busy; _connect.Disabled = busy; _officialOnline.Disabled = busy;
        UpdateSurvivalSave();
        for (int i = 0; i < 4; i++) _tabs.SetTabDisabled(i, busy || Ui.IsMobile && i == 3);
        _team.Disabled = busy;
        _servers.Disabled = busy;
        _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = !busy && !_host.Running;
        _password.Editable = !busy && _tabs.CurrentTab != 2 && !(_tabs.CurrentTab == 3 && _host.Running);
        _stopHost.Disabled = busy || !_host.Running; _firewall.Disabled = busy || !OperatingSystem.IsWindows();
        if (GodotObject.IsInstanceValid(_lobby)) _lobby!.SetBusy(busy);
        if (message.Length > 0) { _status.Text = PvpServer.DisplayText(message); _status.AddThemeColorOverride("font_color", Ui.Muted); }
    }
    public void ShowError(string message)
    {
        message = PvpServer.DisplayText(message);
        SetBusy(false);
        if (GodotObject.IsInstanceValid(_lobby)) { _lobby!.ShowError(message); return; }
        _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Red);
    }
    public void UpdateSurvivalSave()
    {
        if (_continueSurvival == null) return;
        _continueSurvival.Visible = _main.HasSurvivalSave; _continueSurvival.Disabled = _busy;
    }
    public void ShowStatus(string message)
    { _status.Text = PvpServer.DisplayText(message); _status.AddThemeColorOverride("font_color", Ui.Gold); }
    private string _hostPassword = "";
    public void ShowTab(int tab) { if (tab is >= 0 and <= 3) _tabs.CurrentTab = tab; }
}
