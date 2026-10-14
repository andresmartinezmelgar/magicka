using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Antes de combatir, cada jugador elige equipo y confirma que está preparado.</summary>
public partial class RoomWaitingPanel : Control
{
    private PanelContainer _panel = null!;
    private GridContainer _teams = null!;
    private VBoxContainer _blue = null!, _red = null!;
    private Label _headline = null!, _status = null!, _prizes = null!;
    private Button _ready = null!, _start = null!, _exit = null!;
    private string _lastRoster = "";
    private Action<ClientMessage>? _submit;

    public void Build(Action<ClientMessage> submit, Action exit)
    {
        _submit = submit;
        Name = "PvPWaitingRoom";
        Theme = Ui.Theme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.01f, 0.025f, 0.05f, 0.94f),
            MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); Ui.Full(shade);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center); Ui.Full(center);
        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10243b"), 12, new Color("3975aa")));
        center.AddChild(_panel);
        var content = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(content);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(scroll);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        _headline = Ui.Label("SALA DE ESPERA", 24, Ui.Gold);
        _headline.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_headline);
        var details = Ui.Label("Selecciona tu equipo y pulsa Preparado antes de comenzar.", 16, Ui.Muted);
        details.HorizontalAlignment = HorizontalAlignment.Center;
        details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(details);
        _teams = new GridContainer { Columns = 2 };
        _teams.AddThemeConstantOverride("h_separation", 14);
        _teams.AddThemeConstantOverride("v_separation", 10);
        body.AddChild(_teams);
        _blue = TeamList("EQUIPO AZUL", Ui.Blue);
        _red = TeamList("EQUIPO ROJO", Ui.Red);
        _teams.AddChild(_blue);
        _teams.AddChild(_red);
        var choose = new GridContainer { Columns = 2 };
        choose.AddThemeConstantOverride("h_separation", 12); body.AddChild(choose);
        var blue = Ui.Button("Elegir equipo azul", () => _submit?.Invoke(new ClientMessage { Type = "lobby-team", Team = 0 }));
        var red = Ui.Button("Elegir equipo rojo", () => _submit?.Invoke(new ClientMessage { Type = "lobby-team", Team = 1 }));
        choose.AddChild(blue); choose.AddChild(red);
        var actions = new GridContainer { Columns = 2 };
        actions.AddThemeConstantOverride("h_separation", 12); body.AddChild(actions);
        _ready = Ui.Button("Estoy preparado", () => _submit?.Invoke(new ClientMessage { Type = "lobby-ready" }), true);
        _start = Ui.Button("Comenzar partida", () => _submit?.Invoke(new ClientMessage { Type = "lobby-start" }), true);
        actions.AddChild(_ready); actions.AddChild(_start);
        _status = Ui.Label("", 16, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_status);
        _prizes = Ui.Label("", 15, Ui.Muted); _prizes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _prizes.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_prizes);
        // Siempre visible, incluso cuando en Android el listado necesita scroll.
        _exit = Ui.Button("Salir de la sala", exit);
        _exit.Name = "WaitingRoomExit";
        _exit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _exit.CustomMinimumSize = new Vector2(0, Ui.IsMobile ? 58 : 48);
        content.AddChild(_exit);
        GetViewport().SizeChanged += Reflow;
        Reflow();
    }

    private static VBoxContainer TeamList(string name, Color color)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 5);
        var title = Ui.Label(name, 19, color); title.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(title);
        return box;
    }

    private static void RenderTeam(VBoxContainer container, string title, Color color,
        IEnumerable<WizardSnapshot> members, int[] ready)
    {
        foreach (var child in container.GetChildren().Skip(1).ToArray()) child.QueueFree();
        int count = 0;
        foreach (var player in members)
        {
            string suffix = player.Bot ? " [IA]" : ready.Contains(player.Id) ? " ✓" : " · pendiente";
            var label = Ui.Label(player.Name + suffix, 15, color);
            label.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
            container.AddChild(label); count++;
        }
        if (count == 0) container.AddChild(Ui.Label("— Sin jugadores —", 14, Ui.Muted));
    }

    public void Update(WorldSnapshot snapshot, int localId, bool awardsConfigured)
    {
        Visible = snapshot.WaitingForPlayers;
        if (!Visible) return;
        var ready = snapshot.LobbyReadyPlayerIds ?? [];
        string roster = string.Join("|", snapshot.Players.Select(p => $"{p.Id}:{p.Team}:{p.Name}:{p.Bot}:{ready.Contains(p.Id)}"));
        if (roster != _lastRoster)
        {
            _lastRoster = roster;
            RenderTeam(_blue, "AZUL", Ui.Blue, snapshot.Players.Where(p => p.Team == 0), ready);
            RenderTeam(_red, "ROJO", Ui.Red, snapshot.Players.Where(p => p.Team == 1), ready);
        }
        bool ownReady = ready.Contains(localId);
        bool host = snapshot.LobbyHostId == localId;
        bool enough = snapshot.Players.Length >= 2 && snapshot.Players.All(p => p.Bot || ready.Contains(p.Id));
        bool teams = snapshot.Mode == GameMode.Arena ||
            snapshot.Players.Any(p => p.Team == 0) && snapshot.Players.Any(p => p.Team == 1);
        _ready.Text = ownReady ? "Cancelar preparado" : "Estoy preparado";
        _start.Visible = host;
        _start.Disabled = !enough || !teams;
        _headline.Text = snapshot.BestOf > 1 ? $"SALA DE ESPERA · MEJOR DE {snapshot.BestOf}" : "SALA DE ESPERA";
        _status.Text = !teams ? "Debe haber jugadores en los dos equipos." :
            !enough ? "Esperando al menos dos jugadores y que todos estén preparados." :
            host ? "Todos preparados: puedes comenzar." : "Esperando a que el anfitrión inicie la partida.";
        _prizes.Text = awardsConfigured
            ? "Recompensas: se conceden al ganar la serie, solo con Google en el servidor, equipos humanos equilibrados y actividad suficiente."
            : "SIN RECOMPENSAS: este servidor no tiene habilitado el servicio de cuentas Firebase.";
    }

    private void Reflow()
    {
        if (_panel == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(260, Math.Min(950, size.X - 28));
        float height = Math.Max(200, Math.Min(650, size.Y - 28));
        _panel.CustomMinimumSize = new Vector2(width, height);
        _teams.Columns = width < 610 ? 1 : 2;
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= Reflow;
}
