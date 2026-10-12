using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher : Control
{
    private Main _main = null!;
    private LineEdit _name = null!, _address = null!, _password = null!, _hostName = null!;
    private OptionButton _team = null!, _practiceMode = null!, _practiceMap = null!, _wizardType = null!;
    private SpinBox _bots = null!, _discoveryPort = null!, _hostPort = null!, _hostDiscovery = null!;
    private Label _status = null!;
    private ItemList _servers = null!;
    private Button _connect = null!, _refresh = null!, _stopHost = null!, _firewall = null!;
    private Label _hostInfo = null!;
    private RoomLobby? _lobby;
    private readonly LocalServerHost _host = new();
    private TabContainer _tabs = null!;
    private LanServer[] _lan = [];
    private CancellationTokenSource? _search;
    private readonly ConfigFile _config = new();
    private bool _busy;
    private VBoxContainer _homeMenu = null!;
    private VBoxContainer _playBody = null!;
    private Button _menuBack = null!;
    private int _page;
    private Label _profileSummary = null!;

    public void Build(Main main)
    {
        _main = main; Theme = Ui.Theme(); Ui.Full(this);
        _config.Load("user://launcher.cfg");
        var shade = new ColorRect { Color = new Color(0.025f, 0.045f, 0.075f, 0.64f), MouseFilter = MouseFilterEnum.Ignore }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 8 : 32);
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 52); margin.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(410, 0) };
        left.AddThemeConstantOverride("separation", 20); columns.AddChild(left);
        left.AddChild(Ui.Label("ARCANA ARENA  /  " + BuildInfo.Version, 16, Ui.Gold));
        left.AddChild(new Control { CustomMinimumSize = new(0, 80) });
        left.AddChild(Ui.Label("ARCANA\nARENA", 68));
        left.AddChild(Ui.Label("EL COMBATE SE ESCRIBE CON ELEMENTOS", 16, Ui.Gold));
        var description = Ui.Label("Combina tres elementos. Lanza, protégete y cambia de magia en pleno combate.\n\nDuelo, todos contra todos y guerra por el control de la arena.", 20, Ui.Text);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart; left.AddChild(description);
        left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var keyboard = new GridContainer { Columns = 4 }; keyboard.AddThemeConstantOverride("h_separation", 10); keyboard.AddThemeConstantOverride("v_separation", 10); left.AddChild(keyboard);
        foreach (var e in Enum.GetValues<Element>())
        {
            keyboard.AddChild(Ui.ElementTile(e));
        }
        left.AddChild(Ui.Label("F1 · CONTROLES, OBJETIVOS Y TIPOS DE MAGO", 13, Ui.Muted));
        left.Visible = !Ui.IsMobile;
        var rightScroll = new ScrollContainer { CustomMinimumSize = new(Ui.IsMobile ? 0 : 570, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = Ui.IsMobile ? ScrollContainer.ScrollMode.Disabled : ScrollContainer.ScrollMode.Auto }; columns.AddChild(rightScroll);
        var rightCenter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        rightScroll.AddChild(rightCenter);
        var panel = new PanelContainer { CustomMinimumSize = new(Ui.IsMobile ? 0 : 560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; rightCenter.AddChild(panel);
        var formStyle = Ui.Panel(new Color(0.035f, 0.057f, 0.093f, 0.97f), 16);
        formStyle.ContentMarginTop = formStyle.ContentMarginBottom = 12; panel.AddThemeStyleboxOverride("panel", formStyle);
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", 6); panel.AddChild(form);
        form.AddChild(Ui.Label(Ui.IsMobile ? "ARCANA ARENA  /  " + BuildInfo.Version : "ENTRAR EN LA ARENA", 27, Ui.Gold));
        if (!Ui.IsMobile) form.AddChild(Ui.Label("Elige PvP, Versus IA o Perfil.", 16, Ui.Muted));
        _homeMenu = new VBoxContainer();
        _homeMenu.AddThemeConstantOverride("separation", 10);
        form.AddChild(_homeMenu);
        _homeMenu.AddChild(Ui.Button("PvP", () => OpenPage(1), true));
        _homeMenu.AddChild(Ui.Button("Versus IA", () => OpenPage(2), true));
        _homeMenu.AddChild(Ui.Button("Perfil", () => OpenPage(3)));
        _profileSummary = Ui.Label("", 15, Ui.Muted); _homeMenu.AddChild(_profileSummary);
        _playBody = new VBoxContainer(); _playBody.AddThemeConstantOverride("separation", 6); form.AddChild(_playBody);
        _menuBack = Ui.Button("Volver", () => OpenPage(0)); form.AddChild(_menuBack);
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); _playBody.AddChild(identity);
        var nameColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; identity.AddChild(nameColumn);
        nameColumn.AddChild(Ui.Label("Nombre del mago", 14, Ui.Muted));
        _name = new LineEdit { Text = _config.GetValue("player", "name", "Mago").AsString(), MaxLength = 24, CustomMinimumSize = new(0, 45) }; nameColumn.AddChild(_name);
        var accountButtons = new VBoxContainer(); accountButtons.AddThemeConstantOverride("separation", 8); nameColumn.AddChild(accountButtons);
        accountButtons.AddChild(Ui.Button("Inventario", () => _main.OpenEquipment("automatic")));
        accountButtons.AddChild(Ui.Button("Entrar o registrarse con Google", () => _main.OpenEquipment("automatic", googleLogin: true)));
        accountButtons.AddChild(Ui.Button("Jugar online", () => _ = OpenPublicRooms()));
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
        _wizardType.ItemSelected += _ => typeInfo.Text = WizardTypes.Description((WizardType)_wizardType.GetSelectedId());
        _tabs = new TabContainer(); _playBody.AddChild(_tabs);
        var ip = TabBox("Por IP");
        ip.AddChild(Ui.Label("IP o nombre del servidor", 14, Ui.Muted));
        _address = new LineEdit { Text = _config.GetValue("network", "address", "127.0.0.1:27015").AsString(),
            PlaceholderText = "192.168.1.20:27015", CustomMinimumSize = new(0, 45) }; ip.AddChild(_address);
        ip.AddChild(Ui.Label("También admite ws:// y wss:// con ruta personalizada.", 14, Ui.Muted));
        var lan = TabBox("LAN");
        var lanActions = new HBoxContainer(); lanActions.AddThemeConstantOverride("separation", 10); lan.AddChild(lanActions);
        _refresh = Ui.Button("Buscar servidores", StartSearch); _refresh.SizeFlagsHorizontal = SizeFlags.ExpandFill; lanActions.AddChild(_refresh);
        _discoveryPort = new SpinBox { MinValue = 1024, MaxValue = 65535, Value = Protocol.DiscoveryPort, CustomMinimumSize = new(120, 45) }; lanActions.AddChild(_discoveryPort);
        _servers = new ItemList { CustomMinimumSize = new(0, 126), SelectMode = ItemList.SelectModeEnum.Single }; lan.AddChild(_servers);
        _servers.ItemActivated += _ => Connect();
        lan.AddChild(Ui.Label("Misma red local · Puerto de búsqueda UDP a la derecha", 13, Ui.Muted));
        var practice = TabBox("Práctica");
        practice.AddChild(Ui.Label("Simulación local: las mismas reglas del servidor.", 15, Ui.Muted));
        _practiceMode = new OptionButton { CustomMinimumSize = new(0, 45) };
        _practiceMode.AddItem("Guerra de puntos", (int)GameMode.Warfare);
        _practiceMode.AddItem("Todos contra todos", (int)GameMode.Arena);
        _practiceMode.AddItem("Duelo", (int)GameMode.Duel); practice.AddChild(_practiceMode);
        practice.AddChild(Ui.Label("Mapa", 14, Ui.Muted));
        _practiceMap = new OptionButton { Name = "PracticeMapChoice", CustomMinimumSize = new(0, 42) };
        foreach (var map in Enum.GetValues<ArenaMapId>()) _practiceMap.AddItem(ArenaMap.Get(map).Name + (map == ArenaMapId.Citadel ? " · 4×" : ""), (int)map);
        int savedMap = _config.GetValue("practice", "map", 0).AsInt32();
        _practiceMap.Select(Enum.IsDefined((ArenaMapId)savedMap) ? savedMap : 0); practice.AddChild(_practiceMap);
        var practiceObjective = Ui.Label("", 14, Ui.Gold); practiceObjective.AutowrapMode = TextServer.AutowrapMode.WordSmart; practice.AddChild(practiceObjective);
        void Objective()
        {
            var map = ArenaMap.Get((ArenaMapId)_practiceMap.GetSelectedId());
            _practiceMode.Disabled = map.IsPractice;
            if (map.IsPractice) _practiceMode.Select(_practiceMode.GetItemIndex((int)GameMode.Warfare));
            for (int i = 0; i < _practiceMode.ItemCount; i++) _practiceMode.SetItemDisabled(i, !map.SupportsMode((GameMode)_practiceMode.GetItemId(i)));
            if (!map.SupportsMode((GameMode)_practiceMode.GetSelectedId())) _practiceMode.Select(_practiceMode.GetItemIndex((int)GameMode.Warfare));
            practiceObjective.Text = MatchRules.Objective((GameMode)_practiceMode.GetSelectedId(),
                (GameMode)_practiceMode.GetSelectedId() == GameMode.Duel ? 5 : 15, 75, map.Id);
        }
        _practiceMode.ItemSelected += _ => Objective(); _practiceMap.ItemSelected += _ => Objective();
        var botRow = new HBoxContainer(); practice.AddChild(botRow);
        botRow.AddChild(Ui.Label("Bots", 16));
        var botControls = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        botControls.AddThemeConstantOverride("separation", 12);
        botRow.AddChild(botControls);
        var fewerBots = Ui.Button("−", () => _bots.Value = Math.Max(_bots.MinValue, _bots.Value - 1));
        fewerBots.CustomMinimumSize = new Vector2(64, 56);
        botControls.AddChild(fewerBots);
        _bots = new SpinBox { MinValue = 1, MaxValue = 7, Step = 1, Value = 3,
            Rounded = true, CustomMinimumSize = new Vector2(85, 56), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        botControls.AddChild(_bots);
        var moreBots = Ui.Button("+", () => _bots.Value = Math.Min(_bots.MaxValue, _bots.Value + 1));
        moreBots.CustomMinimumSize = new Vector2(64, 56);
        botControls.AddChild(moreBots);
        _practiceMap.ItemSelected += _ => { _bots.Editable = (ArenaMapId)_practiceMap.GetSelectedId() != ArenaMapId.Practice; };
        _bots.Editable = (ArenaMapId)_practiceMap.GetSelectedId() != ArenaMapId.Practice; Objective();
        var laboratory = Ui.Button("Entrar en laboratorio de hechizos", () =>
            _main.StartPractice(_name.Text, 0, GameMode.Warfare, 0, (WizardType)_wizardType.GetSelectedId(), ArenaMapId.Practice));
        laboratory.Name = "PracticeLaboratoryButton"; practice.AddChild(laboratory);
        practice.AddChild(Ui.Label("El duelo usa un bot. No requiere iniciar un servidor.", 13, Ui.Muted));
        practice.AddChild(Ui.Button("Medir rendimiento gráfico", () => _main.StartRenderProfile((ArenaMapId)_practiceMap.GetSelectedId())));
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
        var firebaseSetup = Ui.Button("Configurar Firebase gratis (archivos privados)", OpenLocalFirebaseFolder);
        firebaseSetup.Disabled = !OperatingSystem.IsWindows();
        firebaseSetup.TooltipText = "Prepara los archivos de Firebase fuera del paquete del juego; sin cuenta de pago.";
        hosting.AddChild(firebaseSetup);
        form.AddChild(Ui.Label("Contraseña del servidor (si tiene)", 14, Ui.Muted));
        _password = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Opcional", CustomMinimumSize = new(0, 42) }; _playBody.AddChild(_password);
        _connect = Ui.Button("Ver salas por IP", Connect, true); _playBody.AddChild(_connect);
        _status = Ui.Label("El servidor usa TCP 27015. LAN usa UDP 27016.", 14, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new(0, 45); _playBody.AddChild(_status);
        var footer = new HBoxContainer(); _playBody.AddChild(footer);
        var help = Ui.Button("Cómo jugar", () => _main.ToggleHelp()); help.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(help);
        var diagnostic = Ui.Button("Diagnóstico", _main.ExportDiagnostics); diagnostic.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        diagnostic.TooltipText = "Exporta registros y contexto de la partida a un ZIP para investigar errores."; footer.AddChild(diagnostic);
        var exit = Ui.Button("Salir", () => GetTree().Quit()); exit.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(exit);
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
            _hostInfo.Text = $"El servidor terminó (código {code}). Puedes iniciarlo otra vez y exportar el diagnóstico.";
            ShowError("El servidor local ha terminado inesperadamente. Consulta los registros o exporta el diagnóstico.");
        });
        if (Ui.IsMobile) { _tabs.SetTabHidden(3, true); _tabs.SetTabDisabled(3, true); _tabs.CurrentTab = 2; UpdateConnectLabel(); }
        OpenPage(0);
    }

    private void OpenPage(int page)
    {
        _page = page;
        _homeMenu.Visible = page == 0;
        if (page == 0)
        {
            var profile = _main.Accounts.Profile;
            _profileSummary.Text = profile == null ? _name.Text + " · Sin iniciar sesión"
                : $"{_name.Text} · {profile.Wins} victorias · {profile.Losses} derrotas · {profile.Gold} oro";
        }
        _playBody.Visible = page != 0;
        _name.GetParent().Visible = page == 3;
        _name.GetParent().GetParent().Visible = page is 1 or 3;
        _team.GetParent().Visible = page == 1;
        _wizardType.GetParent().Visible = page == 3;
        _wizardType.GetParent().GetParent().GetChild<Label>(2).Visible = page == 3;
        _status.Visible = page is 1 or 2;

        _menuBack.Visible = page != 0;
        _tabs.Visible = page is 1 or 2;
        _connect.Visible = page is 1 or 2;
        _password.Visible = page == 1;
        _tabs.SetTabHidden(0, page != 1);
        _tabs.SetTabHidden(1, page != 1);
        _tabs.SetTabHidden(2, page != 2);
        _tabs.SetTabHidden(3, Ui.IsMobile || page != 1);
        if (page == 2) _tabs.CurrentTab = 2;
        if (page == 1 && _tabs.CurrentTab == 2) _tabs.CurrentTab = 1;
        if (page == 3) Remember();
        UpdateConnectLabel();
    }

    private VBoxContainer TabBox(string name)
    {
        var box = new VBoxContainer { Name = name, CustomMinimumSize = new(0, 226) };
        box.AddThemeConstantOverride("separation", 7); _tabs.AddChild(box); return box;
    }
    public void SetBusy(bool busy, string message = "")
    {
        if (busy) CancelSearch();
        _busy = busy; _connect.Disabled = busy; for (int i = 0; i < 4; i++) _tabs.SetTabDisabled(i, busy || Ui.IsMobile && i == 3);
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
                (WizardType)_wizardType.GetSelectedId(), (ArenaMapId)_practiceMap.GetSelectedId()); return;
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
    private async Task OpenPublicRooms()
    {
        if (_busy) return;
        SetBusy(true, "Buscando el servidor…");
        try
        {
            using var settings = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://account-server.json"));
            string address = await PublicAccountServer.Resolve(settings.RootElement.GetProperty("projectId").GetString() ?? "");
            _main.Post(() => { if (!IsInsideTree()) return; SetBusy(false); _address.Text = address; Remember(); OpenRooms(address, _password.Text); });
        }
        catch (Exception error) { _main.Post(() => { if (IsInsideTree()) ShowError(error.Message); }); }
    }

    private void OpenRooms(string address, string password)
    {
        CancelSearch(); _main.CancelPendingConnection(); CloseRooms();
        _lobby = new RoomLobby(); AddChild(_lobby); _lobby.Build(_main, address, password, _name.Text, Ui.SelectedTeam(_team), (WizardType)_wizardType.GetSelectedId());
    }
    public void CloseRooms()
    { if (GodotObject.IsInstanceValid(_lobby)) _lobby!.QueueFree(); _lobby = null; }
    public bool HandleBack()
    {
        if (!GodotObject.IsInstanceValid(_lobby)) { if (_page == 0) return false; OpenPage(0); return true; }
        _main.CancelPendingConnection(); CloseRooms(); SetBusy(false); return true;
    }
    private string _hostPassword = "";
    private async Task Host()
    {
        if (_host.Running) { OpenRooms(LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://")) ?? _host.Address, _hostPassword); return; }
        var options = new LocalServerOptions(_hostName.Text.Trim(), (int)_hostPort.Value, (int)_hostDiscovery.Value, _password.Text);
        if (options.Name.Length == 0) { ShowError("Escribe un nombre para el servidor."); return; }
        string? firebaseGameAddress = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://"));
        if (firebaseGameAddress?.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) == true &&
            string.IsNullOrWhiteSpace(options.Password))
        {
            ShowError("Para exponer tu servidor mediante un túnel HTTPS, establece una contraseña en Alojar.");
            return;
        }
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
                _hostInfo.Text = $"Servidor activo · Juego TCP {options.Port} · LAN UDP {options.DiscoveryPort}\n{addresses}"
                    + (System.IO.File.Exists(System.IO.Path.Combine(LocalFirebaseHosting.DirectoryPath(ProjectSettings.GlobalizePath("user://")), LocalFirebaseHosting.ConfigFileName))
                        ? "\nFirebase local: configuración detectada (usa Entrar con Google para comprobarla)." : "\nFirebase: desactivado; las partidas LAN siguen disponibles.");
                string roomAddress = LocalFirebaseHosting.PublicGameAddress(ProjectSettings.GlobalizePath("user://")) ?? _host.Address;
                _address.Text = roomAddress;
                UpdateConnectLabel(); OpenRooms(roomAddress, _hostPassword);
            });
        }
        catch (Exception e)
        {
            Diagnostics.Warn("launcher.host_failed", "No se pudo iniciar el servidor incluido.", e);
            _main.Post(() => ShowError(e is InvalidOperationException or ArgumentException or IOException ? e.Message : "No se pudo iniciar el servidor incluido. Exporta el diagnóstico para revisar el error."));
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
    private void OpenLocalFirebaseFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            string directory = LocalFirebaseHosting.DirectoryPath(ProjectSettings.GlobalizePath("user://"));
            System.IO.Directory.CreateDirectory(directory);
            string template = System.IO.Path.Combine(directory, "server.example.json");
            if (!System.IO.File.Exists(template))
                System.IO.File.WriteAllText(template, Protocol.Serialize(new
                {
                    projectId = "ID-DE-TU-PROYECTO",
                    apiKey = "API-KEY-PUBLICA-DE-TU-APP-WEB",
                    authDomain = "ID-DE-TU-PROYECTO.firebaseapp.com",
                    publicUrl = "http://localhost:" + (int)_hostPort.Value,
                    autoPublicHosting = true
                }));
            OS.ShellOpen(directory);
            ShowStatus("Configuración inicial: copia server.example.json como server.local.json, completa el proyecto y coloca service-account.json aquí. Alojar preparará HTTPS automáticamente.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ShowError("No se pudo preparar la carpeta privada de Firebase: " + error.Message); }
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
    public override void _ExitTree() { _search?.Cancel(); _search?.Dispose(); _host.Dispose(); }
}
