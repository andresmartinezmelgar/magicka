using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher : Control
{
    private OptionButton _practiceDefenseFlow = null!;
    private Main _main = null!;
    private LineEdit _name = null!, _address = null!, _password = null!, _hostName = null!;
    private OptionButton _team = null!, _practiceMode = null!, _practiceMap = null!, _wizardType = null!;
    private SpinBox _bots = null!, _discoveryPort = null!, _hostPort = null!, _hostDiscovery = null!;
    private Label _status = null!;
    private ItemList _servers = null!;
    private Button _connect = null!, _refresh = null!, _stopHost = null!, _firewall = null!, _officialOnline = null!;
    private Label _hostInfo = null!;
    private RoomLobby? _lobby;
    private readonly LocalServerHost _host = new();
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
    private Label _wizardDescription = null!;
    private Button _accountButton = null!;
    private HBoxContainer? _mobilePrimaryActions;
    private ScrollContainer? _mobileScroll;

    public void Build(Main main)
    {
        _main = main; Theme = Ui.Theme(); Ui.Full(this);
        _config.Load("user://launcher.cfg");
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
        if (Ui.IsMobile) _mobileScroll = rightScroll;
        var rightCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = Ui.IsMobile ? SizeFlags.ShrinkBegin : SizeFlags.ExpandFill };
        rightScroll.AddChild(rightCenter);
        var panel = new PanelContainer { CustomMinimumSize = new(Ui.IsMobile ? 0 : 560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; rightCenter.AddChild(panel);
        var formStyle = Ui.Panel(new Color(0.035f, 0.057f, 0.093f, 0.97f), 16);
        formStyle.ContentMarginTop = formStyle.ContentMarginBottom = Ui.IsMobile ? 5 : 12;
        if (Ui.IsMobile) formStyle.ContentMarginLeft = formStyle.ContentMarginRight = 7; panel.AddThemeStyleboxOverride("panel", formStyle);
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", Ui.IsMobile ? 3 : 6); panel.AddChild(form);
        form.AddChild(Ui.Label(Ui.IsMobile ? "ARCANA ARENA" : "ENTRAR EN LA ARENA", Ui.IsMobile ? 22 : 27, Ui.Gold));
        form.AddChild(Ui.Label("Elige PvP, Versus IA o Perfil.", Ui.IsMobile ? 13 : 16, Ui.Muted));
        _homeMenu = new VBoxContainer();
        _homeMenu.AddThemeConstantOverride("separation", Ui.IsMobile ? 5 : 10);
        form.AddChild(_homeMenu);
        _homeMenu.AddChild(Ui.Button("PvP", () => OpenPage(1), true));
        _homeMenu.AddChild(Ui.Button("Versus IA", () => OpenPage(2), true));
        _homeMenu.AddChild(Ui.Button("Perfil", () => OpenPage(3)));
         _playBody = new VBoxContainer(); _playBody.AddThemeConstantOverride("separation", 6); form.AddChild(_playBody);
        _menuBack = Ui.Button("Volver", () => { if (_page == 3) Remember(); OpenPage(0); }); form.AddChild(_menuBack);
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); _playBody.AddChild(identity);
        var nameColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; identity.AddChild(nameColumn);
        nameColumn.AddChild(Ui.Label("Nombre del mago", 14, Ui.Muted));
        _name = new LineEdit { Text = _config.GetValue("player", "name", "Mago").AsString(), MaxLength = 24, CustomMinimumSize = new(0, 45) }; nameColumn.AddChild(_name);
        var accountButtons = new VBoxContainer(); accountButtons.AddThemeConstantOverride("separation", 8); nameColumn.AddChild(accountButtons);
        accountButtons.AddChild(Ui.Button("Inventario", () => _main.OpenEquipment("automatic")));
        _accountButton = Ui.Button("Mi cuenta de Google", () => _main.OpenAccountProfile());
        accountButtons.AddChild(_accountButton);
        var teamColumn = new VBoxContainer { CustomMinimumSize = new(172, 0) }; identity.AddChild(teamColumn);
        teamColumn.AddChild(Ui.Label("Equipo", 14, Ui.Muted));
        _team = Ui.TeamChoice(_config.GetValue("player", "team", -1).AsInt32());
        _team.Name = "LauncherTeamChoice";
        teamColumn.AddChild(_team);
        var typeRow = new HBoxContainer(); typeRow.AddThemeConstantOverride("separation", 12); _playBody.AddChild(typeRow);
        typeRow.AddChild(Ui.Label("Tipo de mago", 14, Ui.Muted));
        _wizardType = new OptionButton { Name = "WizardTypeChoice", CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var type in Enum.GetValues<WizardType>()) _wizardType.AddItem(WizardTypes.Name(type), (int)type);
        int savedType = _config.GetValue("player", "wizard_type", 0).AsInt32();
        _wizardType.Select(Enum.IsDefined((WizardType)savedType) ? savedType : 0); typeRow.AddChild(_wizardType);
        var typeInfo = Ui.Label(WizardTypes.Description((WizardType)_wizardType.GetSelectedId()), 14, Ui.Gold);
        typeInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart; _playBody.AddChild(typeInfo);
        _wizardDescription = typeInfo;
        _wizardType.ItemSelected += _ => typeInfo.Text = WizardTypes.Description((WizardType)_wizardType.GetSelectedId());
        _tabs = new TabContainer(); _playBody.AddChild(_tabs);
        var ip = TabBox("Por IP");
        // La pestaña IP no debe ensanchar el formulario cuando cambia la selección.
        ip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ip.AddChild(Ui.Label("IP o nombre del servidor", 14, Ui.Muted));
        _address = new LineEdit { Text = DefaultServerAddress(),
            PlaceholderText = "192.168.1.20:27015", CustomMinimumSize = new(0, 45) }; ip.AddChild(_address);
        var ipHelp = Ui.Label("IP local del ordenador anfitrión (ej.: 192.168.1.20:27015). También admite wss://.", 14, Ui.Muted);
        ipHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ip.AddChild(ipHelp);
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
        _servers = new ItemList { CustomMinimumSize = new(0, 126), SelectMode = ItemList.SelectModeEnum.Single }; lan.AddChild(_servers);
        _servers.ItemActivated += _ => Connect();
        lan.AddChild(Ui.Label("Misma red local · Puerto de búsqueda UDP a la derecha", 13, Ui.Muted));
        var practice = TabBox("Práctica");
        practice.AddChild(Ui.Label("Simulación local: las mismas reglas del servidor.", 15, Ui.Muted));
        _practiceMode = new OptionButton { CustomMinimumSize = new(0, 45) };
        _practiceMode.AddItem("Guerra de puntos", (int)GameMode.Warfare);
        _practiceMode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _practiceMode.AddItem("Duelo", (int)GameMode.Duel);
        _practiceMode.AddItem("Defensa del bastión", (int)GameMode.Defense); practice.AddChild(_practiceMode);
        practice.AddChild(Ui.Label("Mapa", 14, Ui.Muted));
        _practiceMap = new OptionButton { Name = "PracticeMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>()) _practiceMap.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        int savedMap = _config.GetValue("practice", "map", 0).AsInt32();
        _practiceMap.Select(Enum.IsDefined((ArenaMapId)savedMap) ? savedMap : 0); practice.AddChild(_practiceMap);
        var practiceObjective = Ui.Label("", 14, Ui.Gold); practiceObjective.AutowrapMode = TextServer.AutowrapMode.WordSmart; practice.AddChild(practiceObjective);
        _practiceDefenseFlow = new OptionButton { Name = "PracticeDefenseFlow", CustomMinimumSize = new(0, 45), Visible = false };
        _practiceDefenseFlow.AddItem("Defensa por oleadas", (int)DefenseFlow.Waves);
        _practiceDefenseFlow.AddItem("Defensa continua", (int)DefenseFlow.Continuous);
        void Objective()
        {
            var map = ArenaMap.Get((ArenaMapId)_practiceMap.GetSelectedId());
            _practiceMode.Disabled = map.IsPractice;
            _practiceDefenseFlow.Visible = map.Id == ArenaMapId.Bastion;
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
        var laboratory = Ui.Button("Entrar en laboratorio de hechizos", () =>
            _main.StartPractice(_name.Text, 0, GameMode.Warfare, 0, (WizardType)_wizardType.GetSelectedId(), ArenaMapId.Practice));
        laboratory.Name = "PracticeLaboratoryButton"; practice.AddChild(laboratory);
        practice.AddChild(Ui.Label("El duelo usa un bot. Defensa permite jugar solo o con hasta tres aliados IA.", 13, Ui.Muted));
        practice.AddChild(_practiceDefenseFlow);
        
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
        _connect = Ui.Button("Ver salas por IP", Connect, true); _playBody.AddChild(_connect);
        _status = Ui.Label("El servidor usa TCP 27015. LAN usa UDP 27016.", 14, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new(0, 45); _playBody.AddChild(_status);
        var footer = new HBoxContainer(); _playBody.AddChild(footer);
        var help = Ui.Button("Cómo jugar", () => _main.ToggleHelp()); help.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(help);
        var exit = Ui.Button("Salir", () => GetTree().Quit()); exit.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(exit);
        if (Ui.IsMobile)
        {
            // Volver, jugar y las utilidades NO se esconden al desplazarse el formulario.
            var bar = new PanelContainer();
            bar.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0c192c"), 9, new Color("426488")));
            rightColumn.AddChild(bar);
            var buttonRows = new VBoxContainer();
            buttonRows.AddThemeConstantOverride("separation", 7);
            bar.AddChild(buttonRows);
            _mobilePrimaryActions = new HBoxContainer();
            _mobilePrimaryActions.AddThemeConstantOverride("separation", 9);
            buttonRows.AddChild(_mobilePrimaryActions);
            _menuBack.Reparent(_mobilePrimaryActions);
            _connect.Reparent(_mobilePrimaryActions);
            _menuBack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _connect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var utilities = new HBoxContainer();
            utilities.AddThemeConstantOverride("separation", 9);
            buttonRows.AddChild(utilities);
            help.Reparent(utilities);
            exit.Reparent(utilities);
            footer.QueueFree();
        }
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
            foreach (var control in new Control[] { _name, _address, _team, _wizardType,
                _practiceMode, _practiceMap, _discoveryPort, _password, _hostName })
                Ui.TouchTarget(control);
            _tabs.SetTabHidden(3, true);
            _tabs.SetTabDisabled(3, true);
            _tabs.CurrentTab = 2;
            UpdateConnectLabel();
        }
        ApplyOptionALayout(margin, columns, rightColumn, rightScroll, rightCenter, panel, form,
            identity, typeRow, typeInfo, footer, help, exit);
        OpenPage(0);
    }

    public void UpdateAccountStatus()
    {
        _accountButton.Text = _main.Accounts.IsDirectFirebaseSession
            ? "Mi cuenta de Google · Conectado" : "Entrar o registrarse con Google";
        _optionAChrome?.RefreshAccount();
    }

    private void OpenPage(int page)
    {
        _page = page;
        _homeMenu.Visible = page == 0;
        if (page == 0) UpdateAccountStatus();
        _playBody.Visible = page != 0;
        ((Control)_name.GetParent()).Visible = page == 3;
        ((Control)_name.GetParent().GetParent()).Visible = page is 1 or 3;
        ((Control)_team.GetParent()).Visible = page == 1;
        ((Control)_wizardType.GetParent()).Visible = page == 3;
        _wizardDescription.Visible = page == 3;
        _status.Visible = page == 1;

        _menuBack.Visible = page != 0;
        _menuBack.Text = page == 3 ? "Guardar y volver" : "Volver";
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
        // Godot puede conservar una pestaña oculta al volver de práctica. En Android,
        // nunca se debe ejecutar la acción Alojar desde la pantalla PvP.
        if (page == 1 && (_tabs.CurrentTab == 2 ||
            (Ui.IsMobile && _tabs.CurrentTab is not (0 or 1))))
            _tabs.CurrentTab = 1;
        if (page == 3) Remember();
        if (_mobilePrimaryActions != null) _mobilePrimaryActions.Visible = page != 0;
        if (_mobileScroll != null) _mobileScroll.ScrollVertical = 0;
        UpdateOptionAPage(page);
        UpdateConnectLabel();
    }

    internal void OpenMobileSmokePage(int page) => OpenPage(page);
    internal Button[] MobilePrimaryButtons => (_optionAPrimaryActions ?? _mobilePrimaryActions)?
        .GetChildren().OfType<Button>().Where(button => button.Visible).ToArray() ?? [];

    private string DefaultServerAddress()
    {
        string saved = _config.GetValue("network", "address", "127.0.0.1:27015").AsString();
        // Los subdominios trycloudflare.com son temporales; no reutilizarlos
        // automáticamente en una nueva sesión como si fueran el servidor local.
        return saved.Contains(".trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
            saved.Contains(".traycloudfire.com", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1:27015" : saved;
    }

    private VBoxContainer TabBox(string name)
    {
        var box = new VBoxContainer { Name = name, CustomMinimumSize = new(0, 226), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 7); _tabs.AddChild(box); return box;
    }
    public void SetBusy(bool busy, string message = "")
    {
        if (busy) CancelSearch();
        _busy = busy; _connect.Disabled = busy; _officialOnline.Disabled = busy;
        for (int i = 0; i < 4; i++) _tabs.SetTabDisabled(i, busy || Ui.IsMobile && i == 3);
        _name.Editable = _address.Editable = !busy; _team.Disabled = _wizardType.Disabled = busy;
        _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = !busy && !_host.Running;
        _password.Editable = !busy && _tabs.CurrentTab != 2 && !(_tabs.CurrentTab == 3 && _host.Running);
        _stopHost.Disabled = busy || !_host.Running; _firewall.Disabled = busy || !OperatingSystem.IsWindows();
        if (GodotObject.IsInstanceValid(_lobby)) _lobby!.SetBusy(busy);
        if (message.Length > 0) { _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Muted); }
    }
    public void ShowError(string message)
    {
        SetBusy(false);
        if (GodotObject.IsInstanceValid(_lobby)) { _lobby!.ShowError(message); return; }
        _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Red);
    }
    public void ShowStatus(string message)
    { _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Gold); }
    private void Remember()
    {
        _config.SetValue("player", "name", _name.Text); _config.SetValue("network", "address", _address.Text);
        _config.SetValue("player", "wizard_type", _wizardType.GetSelectedId());
        _config.SetValue("player", "team", Ui.SelectedTeam(_team));
        _config.SetValue("practice", "map", _practiceMap.GetSelectedId());
        _config.SetValue("host", "name", _hostName.Text); _config.SetValue("host", "port", (int)_hostPort.Value);
        _config.SetValue("host", "discovery", (int)_hostDiscovery.Value);
        var saved = _config.Save("user://launcher.cfg");
        if (saved != Error.Ok) Diagnostics.Warn("launcher.config_failed", "No se pudo guardar la configuración.", context:
            new Dictionary<string, object?> { ["error"] = saved });
    }
    private void Connect()
    {
        if (_busy) return;
        Remember();
        int team = Ui.SelectedTeam(_team);
        if (_tabs.CurrentTab == 2)
        {
            _main.StartPractice(_name.Text, team, (GameMode)_practiceMode.GetSelectedId(), (int)_bots.Value,
                (WizardType)_wizardType.GetSelectedId(), (ArenaMapId)_practiceMap.GetSelectedId(), (DefenseFlow)_practiceDefenseFlow.GetSelectedId()); return;
        }
        if (_tabs.CurrentTab == 3) { _ = Host(); return; }
        string address = _address.Text;
        if (_tabs.CurrentTab == 1)
        {
            var selected = _servers.GetSelectedItems();
            if (selected.Length == 0 || selected[0] >= _lan.Length) { ShowError("Selecciona un servidor de la lista LAN."); return; }
            address = _lan[selected[0]].Address;
        }
        OpenRooms(address, _password.Text);
    }
    /// <summary>
    /// La dirección pública cambia al reiniciar Cloudflare, pero el anuncio
    /// de Firestore se renueva. Se valida el anuncio y se comprueba que el
    /// servidor real responde antes de abrir las salas. No se guardan tokens
    /// Google ni se entregan credenciales a direcciones no verificadas.
    /// </summary>
    private async void ConnectOfficialOnline()
    {
        if (_busy || _page != 1) return;
        if (!_main.Accounts.IsDirectFirebaseSession)
        {
            ShowError("Inicia sesión con Google en Perfil antes de entrar al servidor online con recompensas.");
            return;
        }
        CancelOnlineSearch();
        var pending = new CancellationTokenSource();
        _onlineSearch = pending;
        SetBusy(true, "Buscando el servidor seguro anunciado en Firebase…");
        try
        {
            string address = await PublicAccountServer.Resolve(FirebaseProjectId, pending.Token);
            RoomDirectory rooms = await RoomBrowser.ListAsync(address, pending.Token);
            if (!rooms.Server.RewardsAvailable)
                throw new InvalidOperationException("El servidor online no tiene las recompensas activadas.");
            _main.Post(() =>
            {
                if (_onlineSearch != pending || pending.IsCancellationRequested || _page != 1) return;
                _onlineSearch = null;
                SetBusy(false);
                _address.Text = address;
                Remember();
                OpenRooms(address, "");
            });
        }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.Warn("launcher.online_discovery_failed", "No se pudo encontrar el servidor online.", error);
            _main.Post(() =>
            {
                if (_onlineSearch != pending || pending.IsCancellationRequested || _page != 1) return;
                _onlineSearch = null;
                ShowError(error is InvalidOperationException or IOException or InvalidDataException
                    ? error.Message
                    : "No se pudo abrir el servidor online. Comprueba Internet y que el anfitrión tenga Firebase y Cloudflare activos.");
            });
        }
        finally { pending.Dispose(); }
    }
    private void CancelOnlineSearch()
    {
        var pending = _onlineSearch;
        _onlineSearch = null;
        if (pending == null) return;
        // La tarea de búsqueda puede haber terminado y dispuesto su CTS
        // mientras el resultado espera a ser procesado en el hilo de Godot.
        try { pending.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void OpenRooms(string address, string password)
    {
        CancelOnlineSearch(); CancelSearch(); _main.CancelPendingConnection(); CloseRooms();
        _lobby = new RoomLobby(); AddChild(_lobby); _lobby.Build(_main, address, password, _name.Text, Ui.SelectedTeam(_team), (WizardType)_wizardType.GetSelectedId());
    }
    public void CloseRooms()
    { if (GodotObject.IsInstanceValid(_lobby)) _lobby!.QueueFree(); _lobby = null; }
    public bool HandleBack()
    {
        if (!GodotObject.IsInstanceValid(_lobby)) { if (_page == 0) return false; OpenPage(0); return true; }
        CancelOnlineSearch(); _main.CancelPendingConnection(); CloseRooms(); SetBusy(false); return true;
    }
    private string _hostPassword = "";
    private async Task Host()
    {
        if (_host.Running) { OpenRooms(_host.Address, _hostPassword); return; }
        var options = new LocalServerOptions(_hostName.Text.Trim(), (int)_hostPort.Value, (int)_hostDiscovery.Value, _password.Text);
        if (options.Name.Length == 0) { ShowError("Escribe un nombre para el servidor."); return; }
        // Alojar inicia siempre el servidor local. No exigir contraseña por
        // una publicUrl antigua: protegerlo será decisión explícita del usuario.
        // Una URL WSS pública no debe activarse implícitamente desde este botón.
        SetBusy(true, "Iniciando el servidor incluido…");
        try
        {
            await _host.StartAsync(PackageDirectory(), ProjectSettings.GlobalizePath("user://"), options);
            _main.Post(() =>
            {
                _hostPassword = options.Password; SetBusy(false); _stopHost.Disabled = false; _password.Editable = false;
                _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = false;
                string addresses = "Busca el servidor en LAN";
                try
                {
                    var ips = System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()).Where(ip =>
                        ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip)).Take(3);
                    string list = string.Join(" · ", ips.Select(ip => ip + ":" + options.Port));
                    if (list.Length > 0) addresses = "IP para compartir: " + list;
                }
                catch (System.Net.Sockets.SocketException) { }
                string firebaseDir = LocalFirebaseHosting.DirectoryPath(ProjectSettings.GlobalizePath("user://"));
                bool firebasePresent = System.IO.File.Exists(System.IO.Path.Combine(firebaseDir, LocalFirebaseHosting.ConfigFileName));
                string? publicGame = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://"));
                string firebaseStatus = !firebasePresent
                    ? "\nFirebase: desactivado; las partidas LAN siguen disponibles."
                    : publicGame?.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) == true
                        ? "\nFirebase configurado. Para jugar con recompensas desde el móvil, usa Por IP con:\n" + publicGame
                        : "\nFirebase local configurado, pero falta la dirección WSS pública para el móvil.";
                _hostInfo.Text = $"Servidor activo · Juego TCP {options.Port} · LAN UDP {options.DiscoveryPort}\n{addresses}" + firebaseStatus;
                // El anfitrión siempre gestiona sus propias salas por loopback.
                // Un túnel trycloudflare es temporal y su publicUrl puede quedar caducado.
                // La dirección pública se comparte por separado con jugadores externos.
                string roomAddress = _host.Address;
                _address.Text = roomAddress;
                UpdateConnectLabel(); OpenRooms(roomAddress, _hostPassword);
            });
        }
        catch (Exception e)
        {
            Diagnostics.Warn("launcher.host_failed", "No se pudo iniciar el servidor incluido.", e);
            _main.Post(() => ShowError(e is InvalidOperationException or ArgumentException or IOException ? e.Message : "No se pudo iniciar el servidor incluido. Consulta los registros."));
        }
    }
    public static string PackageDirectory() => OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://../../artifacts/windows")
        : System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
    public void BrowseAddress(string address, bool create = false)
    { OpenRooms(address, _password.Text); if (create) _lobby!.OpenCreation(); }
    public void ShowTab(int tab) { if (tab is >= 0 and <= 3) _tabs.CurrentTab = tab; }
    public async Task<RoomInfo> StartSmokeRoom()
    {
        await _host.StartAsync(PackageDirectory(), ProjectSettings.GlobalizePath("user://"), new("Prueba de launcher", 29025, 29026, ""));
        return await RoomBrowser.CreateAsync(_host.Address, new() { Name = "Sala del launcher", Bots = 1 });
    }
    private async void ConfigureLan()
    {
        if (_busy || !OperatingSystem.IsWindows()) return;
        string script = System.IO.Path.Combine(PackageDirectory(), "Configurar-LAN.ps1");
        string server = System.IO.Path.Combine(PackageDirectory(), "server", "Arcana.Server.exe");
        if (!File.Exists(script) || !File.Exists(server)) { ShowError("Extrae el paquete Windows completo para configurar LAN."); return; }
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = true, Verb = "runas", WorkingDirectory = PackageDirectory() };
        foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-ServerPath", server,
            "-GamePort", ((int)_hostPort.Value).ToString(), "-DiscoveryPort", ((int)_hostDiscovery.Value).ToString() }) start.ArgumentList.Add(arg);
        SetBusy(true, "Windows solicitará permiso para permitir el servidor en redes privadas…");
        try
        {
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("No se abrió la configuración de Windows.");
            await process.WaitForExitAsync();
            int code = process.ExitCode;
            _main.Post(() => { if (code == 0) SetBusy(false, "Acceso LAN configurado para estos puertos en redes privadas.");
                else ShowError("Windows no pudo configurar el acceso. Comprueba el permiso de administrador."); });
        }
        catch (Exception e)
        {
            Diagnostics.Warn("launcher.firewall_failed", "La configuración LAN no se completó.", e);
            _main.Post(() => ShowError(e is System.ComponentModel.Win32Exception ? "La configuración de LAN se canceló en Windows." : e.Message));
        }
    }
    private void StopHost()
    {
        _host.Stop(); _hostPassword = ""; _stopHost.Disabled = true; _password.Editable = true;
        _hostName.Editable = _hostPort.Editable = _hostDiscovery.Editable = true;
        _hostInfo.Text = "Servidor detenido. Puedes cambiar sus opciones e iniciarlo otra vez.";
        _status.Text = "El servidor y sus salas se han cerrado."; UpdateConnectLabel();
    }
    private void UpdateConnectLabel() => _connect.Text = _tabs.CurrentTab switch
    { 0 => "Ver salas por IP", 1 => "Ver salas del servidor LAN", 2 => "Entrenar con bots", _ => _host.Running ? "Ver mis salas" : "Iniciar servidor y ver salas" };
    private async void StartSearch()
    {
        if (_busy || _refresh.Disabled) return;
        CancelSearch(); var search = new CancellationTokenSource(); _search = search;
        _refresh.Disabled = true; _status.Text = "Buscando partidas en la red local…";
        int port = (int)_discoveryPort.Value;
        try
        {
            var servers = await LanDiscovery.FindAsync(port, search.Token);
            _main.Post(() =>
            {
                if (_search != search || search.IsCancellationRequested) return;
                _lan = servers; _servers.Clear();
                foreach (var s in servers)
                    _servers.AddItem($"{s.Info.Name}  ·  {s.Address}  ·  {s.Info.Players}/{s.Info.MaxPlayers} plazas");
                if (servers.Length > 0) _servers.Select(0);
                _status.Text = servers.Length > 0 ? $"{servers.Length} servidor(es) encontrado(s)." : "No se encontraron servidores. También puedes usar su IP local en la pestaña Por IP.";
                _refresh.Disabled = false;
            });
        }
        catch (OperationCanceledException) { _main.Post(() => { if (_search == search) _refresh.Disabled = false; }); }
        catch (Exception e)
        {
            if (search.IsCancellationRequested) return;
            Diagnostics.Warn("launcher.discovery_failed", "No se pudo buscar en LAN.", e);
            _main.Post(() => { if (_search != search) return; _refresh.Disabled = false; ShowError("No se ha podido buscar en LAN. Comprueba el puerto UDP y el cortafuegos."); });
        }
    }
    private void CancelSearch()
    { var search = _search; _search = null; search?.Cancel(); search?.Dispose(); _refresh.Disabled = false; }
    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateResponsiveMenu; CancelOnlineSearch(); _search?.Cancel(); _search?.Dispose(); _host.Dispose(); }
}
