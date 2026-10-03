using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Launcher : Control
{
    private Main _main = null!;
    private LineEdit _name = null!, _address = null!, _password = null!;
    private OptionButton _team = null!, _practiceMode = null!;
    private SpinBox _bots = null!, _discoveryPort = null!;
    private Label _status = null!;
    private ItemList _servers = null!;
    private Button _connect = null!, _refresh = null!;
    private TabContainer _tabs = null!;
    private LanServer[] _lan = [];
    private CancellationTokenSource? _search;
    private readonly ConfigFile _config = new();
    private bool _busy;

    public void Build(Main main)
    {
        _main = main; Theme = Ui.Theme(); Ui.Full(this);
        _config.Load("user://launcher.cfg");
        var shade = new ColorRect { Color = new Color(0.025f, 0.045f, 0.075f, 0.64f), MouseFilter = MouseFilterEnum.Ignore }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 48);
        var columns = new HBoxContainer(); columns.AddThemeConstantOverride("separation", 52); margin.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(410, 0) };
        left.AddThemeConstantOverride("separation", 20); columns.AddChild(left);
        left.AddChild(Ui.Label("ARCANA ARENA  /  0.1.0", 16, Ui.Gold));
        left.AddChild(new Control { CustomMinimumSize = new(0, 80) });
        left.AddChild(Ui.Label("ARCANA\nARENA", 68));
        left.AddChild(Ui.Label("EL COMBATE SE ESCRIBE CON ELEMENTOS", 16, Ui.Gold));
        var description = Ui.Label("Combina tres elementos. Lanza, protégete y cambia de magia en pleno combate.\n\nDuelo, todos contra todos y guerra por el control de la arena.", 20, Ui.Text);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart; left.AddChild(description);
        left.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var keyboard = new GridContainer { Columns = 4 }; keyboard.AddThemeConstantOverride("h_separation", 10); keyboard.AddThemeConstantOverride("v_separation", 10); left.AddChild(keyboard);
        foreach (var e in Enum.GetValues<Element>())
        {
            var tile = new PanelContainer { CustomMinimumSize = new(96, 62), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tile.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.05f, 0.09f, 0.14f, 0.86f), 8));
            var text = Ui.Label(Ui.ElementKey(e) + "  " + Ui.ElementName(e), 16, Ui.ElementColor(e));
            text.HorizontalAlignment = HorizontalAlignment.Center; tile.AddChild(text); keyboard.AddChild(tile);
        }
        left.AddChild(Ui.Label("C# · GODOT .NET · SERVIDOR AUTORITATIVO", 13, Ui.Muted));
        var rightCenter = new CenterContainer { CustomMinimumSize = new(570, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(rightCenter);
        var panel = new PanelContainer { CustomMinimumSize = new(560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }; rightCenter.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.035f, 0.057f, 0.093f, 0.97f), 16));
        var form = new VBoxContainer(); form.AddThemeConstantOverride("separation", 14); panel.AddChild(form);
        form.AddChild(Ui.Label("ENTRAR EN LA ARENA", 27, Ui.Gold));
        form.AddChild(Ui.Label("Conexión directa, red local o práctica con bots.", 16, Ui.Muted));
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); form.AddChild(identity);
        var nameColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; identity.AddChild(nameColumn);
        nameColumn.AddChild(Ui.Label("Nombre del mago", 14, Ui.Muted));
        _name = new LineEdit { Text = _config.GetValue("player", "name", "Mago").AsString(), MaxLength = 24, CustomMinimumSize = new(0, 45) }; nameColumn.AddChild(_name);
        var teamColumn = new VBoxContainer { CustomMinimumSize = new(172, 0) }; identity.AddChild(teamColumn);
        teamColumn.AddChild(Ui.Label("Equipo preferido", 14, Ui.Muted));
        _team = new OptionButton { CustomMinimumSize = new(0, 45) };
        _team.AddItem("Automático", -1); _team.AddItem("Azul", 0); _team.AddItem("Rojo", 1);
        teamColumn.AddChild(_team);
        _tabs = new TabContainer(); form.AddChild(_tabs);
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
        var botRow = new HBoxContainer(); practice.AddChild(botRow);
        botRow.AddChild(Ui.Label("Bots", 16));
        _bots = new SpinBox { MinValue = 1, MaxValue = 7, Value = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; botRow.AddChild(_bots);
        practice.AddChild(Ui.Label("El duelo usa un bot. No requiere iniciar un servidor.", 13, Ui.Muted));
        form.AddChild(Ui.Label("Contraseña del servidor (si tiene)", 14, Ui.Muted));
        _password = new LineEdit { Secret = true, MaxLength = 128, PlaceholderText = "Opcional", CustomMinimumSize = new(0, 42) }; form.AddChild(_password);
        _connect = Ui.Button("Conectar por IP", Connect, true); form.AddChild(_connect);
        _status = Ui.Label("El servidor usa TCP 27015. LAN usa UDP 27016.", 14, Ui.Muted);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new(0, 45); form.AddChild(_status);
        var footer = new HBoxContainer(); form.AddChild(footer);
        var help = Ui.Button("Controles", () => _main.ToggleHelp()); help.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(help);
        var exit = Ui.Button("Salir", () => GetTree().Quit()); exit.SizeFlagsHorizontal = SizeFlags.ExpandFill; footer.AddChild(exit);
        _tabs.TabChanged += tab =>
        {
            _connect.Text = tab == 0 ? "Conectar por IP" : tab == 1 ? "Conectar al servidor LAN" : "Entrenar con bots";
            _password.Editable = tab != 2;
            if (tab == 1 && _lan.Length == 0 && !_busy) StartSearch();
        };
    }

    private VBoxContainer TabBox(string name)
    {
        var box = new VBoxContainer { Name = name, CustomMinimumSize = new(0, 226) };
        box.AddThemeConstantOverride("separation", 10); _tabs.AddChild(box); return box;
    }
    public void SetBusy(bool busy, string message = "")
    {
        _busy = busy; _connect.Disabled = busy; _tabs.SetTabDisabled(0, busy); _tabs.SetTabDisabled(1, busy); _tabs.SetTabDisabled(2, busy);
        if (message.Length > 0) { _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Muted); }
    }
    public void ShowError(string message) { SetBusy(false); _status.Text = message; _status.AddThemeColorOverride("font_color", Ui.Red); }
    private void Remember()
    {
        _config.SetValue("player", "name", _name.Text); _config.SetValue("network", "address", _address.Text); _config.Save("user://launcher.cfg");
    }
    private void Connect()
    {
        if (_busy) return;
        Remember();
        int team = _team.GetSelectedId();
        if (_tabs.CurrentTab == 2)
        {
            _main.StartPractice(_name.Text, team, (GameMode)_practiceMode.GetSelectedId(), (int)_bots.Value); return;
        }
        string address = _address.Text;
        if (_tabs.CurrentTab == 1)
        {
            var selected = _servers.GetSelectedItems();
            if (selected.Length == 0 || selected[0] >= _lan.Length) { ShowError("Selecciona un servidor de la lista LAN."); return; }
            address = _lan[selected[0]].Address;
        }
        SetBusy(true, "Conectando con " + address + "…");
        _ = _main.ConnectOnline(address, _name.Text, team, _password.Text);
    }
    private async void StartSearch()
    {
        if (_busy || _refresh.Disabled) return;
        _search?.Cancel(); _search?.Dispose(); _search = new();
        _refresh.Disabled = true; _status.Text = "Buscando partidas en la red local…";
        int port = (int)_discoveryPort.Value;
        try
        {
            var servers = await LanDiscovery.FindAsync(port, _search.Token);
            _main.Post(() =>
            {
                _lan = servers; _servers.Clear();
                foreach (var s in servers)
                    _servers.AddItem($"{s.Info.Name}   {s.Info.Players}/{s.Info.MaxPlayers}   {Ui.ModeName(s.Info.Mode)}\n{s.Address}");
                if (servers.Length > 0) _servers.Select(0);
                _status.Text = servers.Length > 0 ? $"{servers.Length} servidor(es) encontrado(s)." : "No se encontraron servidores. También puedes usar su IP local en la pestaña Por IP.";
                _refresh.Disabled = false;
            });
        }
        catch (OperationCanceledException) { _main.Post(() => _refresh.Disabled = false); }
        catch (Exception) { _main.Post(() => { _refresh.Disabled = false; ShowError("No se ha podido buscar en LAN. Comprueba el puerto UDP y el cortafuegos."); }); }
    }
    public override void _ExitTree() { _search?.Cancel(); _search?.Dispose(); }
}
