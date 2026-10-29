using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Antes de combatir, cada jugador elige equipo y confirma que está preparado.</summary>
public partial class RoomWaitingPanel : Control
{
    private PanelContainer _panel = null!;
    private GridContainer _teams = null!, _actions = null!;
    private VBoxContainer _blue = null!, _red = null!;
    private ElementTextLabel _headline = null!, _details = null!, _status = null!, _prizes = null!;
    private ElementTextLabel _blueCount = null!, _redCount = null!, _blueEmpty = null!, _redEmpty = null!;
    private Button _blueJoin = null!, _redJoin = null!, _ready = null!, _start = null!, _exit = null!;
    private readonly Dictionary<int, RoomWaitingPlayerCard> _players = [];
    private Action<ClientMessage>? _submit;

    public void Build(Action<ClientMessage> submit, Action exit)
    {
        _submit = submit;
        Name = "PvPWaitingRoom";
        Theme = Ui.Theme();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = Ui.ModalDim, MouseFilter = MouseFilterEnum.Stop };
        AddChild(shade); Ui.Full(shade);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center); Ui.Full(center);
        _panel = new PanelContainer { Name = "WaitingRoomPanel" };
        _panel.AddThemeStyleboxOverride("panel", Ui.ModalPanel());
        center.AddChild(_panel);
        var content = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(content);
        var scroll = new ContentScrollContainer { Name = "WaitingRoomScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(scroll);
        Ui.VisibleVerticalScroll(scroll);
        var body = new VBoxContainer { Name = "WaitingRoomBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10); scroll.AddChild(body);
        _headline = Ui.Label("SALA DE ESPERA", 24, Ui.Gold);
        _headline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _headline.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_headline);
        _details = Ui.Label("Elige equipo y confirma que estás preparado.", 16, Ui.Muted);
        _details.HorizontalAlignment = HorizontalAlignment.Center;
        _details.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(_details);
        _teams = new GridContainer { Name = "WaitingRoomTeams", Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _teams.AddThemeConstantOverride("h_separation", 12);
        _teams.AddThemeConstantOverride("v_separation", 10);
        body.AddChild(_teams);
        _blue = TeamList("EQUIPO AZUL", Ui.Blue, 0, out _blueCount, out _blueEmpty, out _blueJoin);
        _red = TeamList("EQUIPO ROJO", Ui.Red, 1, out _redCount, out _redEmpty, out _redJoin);
        _prizes = Ui.Label("", 14, Ui.Muted); _prizes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _prizes.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_prizes);

        // Preparación, comienzo y salida no se desplazan con el listado de jugadores.
        _status = Ui.Label("", 16, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.HorizontalAlignment = HorizontalAlignment.Center; content.AddChild(_status);
        _actions = new GridContainer { Columns = 2 };
        _actions.AddThemeConstantOverride("v_separation", 8);
        _actions.AddThemeConstantOverride("h_separation", 12); content.AddChild(_actions);
        _ready = Ui.Button("Estoy preparado", () => _submit?.Invoke(new ClientMessage { Type = "lobby-ready" }), true);
        _ready.Name = "WaitingRoomReady";
        _start = Ui.Button("Comenzar partida", () => _submit?.Invoke(new ClientMessage { Type = "lobby-start" }), true);
        _start.Name = "WaitingRoomStart";
        _actions.AddChild(_ready); _actions.AddChild(_start);
        _exit = Ui.Button("Salir de la sala", exit);
        _exit.Name = "WaitingRoomExit";
        _actions.AddChild(_exit);
        foreach (var button in new[] { _blueJoin, _redJoin, _ready, _start, _exit })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new(0, Ui.IsMobile ? 52 : 46);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (Ui.IsMobile) button.AddThemeFontSizeOverride("font_size", 18);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            {
                if (button.GetThemeStylebox(state) is not StyleBoxFlat source) continue;
                var style = (StyleBoxFlat)source.Duplicate();
                style.ContentMarginLeft = style.ContentMarginRight = 10;
                style.ContentMarginTop = style.ContentMarginBottom = 8;
                button.AddThemeStyleboxOverride(state, style);
            }
        }
        GetViewport().SizeChanged += Reflow;
        Reflow();
    }

    private VBoxContainer TeamList(string name, Color color, int team,
        out ElementTextLabel count, out ElementTextLabel empty, out Button join)
    {
        var panel = new PanelContainer { Name = team == 0 ? "WaitingBlueTeam" : "WaitingRedTeam",
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = Ui.Panel(new Color(team == 0 ? "0e2035" : "271b30"), 10, new Color(color, .55f));
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", style);
        _teams.AddChild(panel);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6); box.AddChild(header);
        var title = Ui.Label(name, 18, color);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(title);
        count = Ui.Label("0/0 listos", 14, Ui.Muted);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.TooltipText = "Jugadores preparados / jugadores del equipo";
        header.AddChild(count);
        var members = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        members.AddThemeConstantOverride("separation", 6); box.AddChild(members);
        empty = Ui.Label("Esperando jugadores", 15, Ui.Muted);
        empty.CustomMinimumSize = new(0, 128);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.AutowrapMode = TextServer.AutowrapMode.WordSmart; members.AddChild(empty);
        join = Ui.Button(team == 0 ? "Elegir equipo azul" : "Elegir equipo rojo",
            () => _submit?.Invoke(new ClientMessage { Type = "lobby-team", Team = team }));
        join.Name = team == 0 ? "WaitingJoinBlue" : "WaitingJoinRed";
        box.AddChild(join);
        return members;
    }

    private void UpdatePlayers(WorldSnapshot snapshot, int localId, int[] ready)
    {
        var ids = snapshot.Players.Select(p => p.Id).ToHashSet();
        foreach (int id in _players.Keys.Where(id => !ids.Contains(id)).ToArray()) RemovePlayer(id);
        foreach (var player in snapshot.Players.OrderBy(p => p.Id))
        {
            var parent = player.Team == 0 ? _blue : _red;
            // Cambiar de equipo sustituye solo esa tarjeta; los demás halos siguen animándose.
            if (_players.TryGetValue(player.Id, out var old) && old.GetParent() != parent) RemovePlayer(player.Id);
            if (!_players.TryGetValue(player.Id, out var card))
            {
                card = new RoomWaitingPlayerCard(); parent.AddChild(card); card.Build(player);
                _players.Add(player.Id, card);
            }
            card.Update(player, localId, snapshot.LobbyHostId, ready.Contains(player.Id));
        }
        foreach (int team in new[] { 0, 1 })
        {
            var members = snapshot.Players.Where(p => p.Team == team).OrderBy(p => p.Id).ToArray();
            var list = team == 0 ? _blue : _red;
            for (int i = 0; i < members.Length; i++)
            {
                var card = _players[members[i].Id];
                if (card.GetIndex() != i + 1) list.MoveChild(card, i + 1);
            }
            (team == 0 ? _blueEmpty : _redEmpty).Visible = members.Length == 0;
            (team == 0 ? _blueCount : _redCount).Text =
                $"{members.Count(p => p.Bot || ready.Contains(p.Id))}/{members.Length} listos";
        }
    }

    private void RemovePlayer(int id)
    {
        var card = _players[id];
        card.GetParent().RemoveChild(card); card.QueueFree(); _players.Remove(id);
    }

    public void Update(WorldSnapshot snapshot, int localId, bool awardsConfigured)
    {
        Visible = snapshot.WaitingForPlayers;
        if (!Visible) return;
        var ready = snapshot.LobbyReadyPlayerIds ?? [];
        UpdatePlayers(snapshot, localId, ready);
        var own = snapshot.Players.FirstOrDefault(p => p.Id == localId);
        bool ownReady = ready.Contains(localId);
        bool host = snapshot.LobbyHostId == localId;
        bool enough = snapshot.Players.Length >= 2 && snapshot.Players.All(p => p.Bot || ready.Contains(p.Id));
        bool teams = snapshot.Mode == GameMode.Arena ||
            snapshot.Players.Any(p => p.Team == 0) && snapshot.Players.Any(p => p.Team == 1);
        _blueJoin.Disabled = own?.Team == 0; _redJoin.Disabled = own?.Team == 1;
        _blueJoin.Text = own?.Team == 0 ? "Tu equipo · Azul" : "Elegir equipo azul";
        _redJoin.Text = own?.Team == 1 ? "Tu equipo · Rojo" : "Elegir equipo rojo";
        _ready.Text = ownReady ? "Cancelar preparado" : "Estoy preparado";
        _ready.Disabled = own == null || own.Bot;
        _start.Visible = host;
        _actions.Columns = ActionColumns(_panel.CustomMinimumSize.X);
        _start.Disabled = own == null || !enough || !teams;
        _headline.Text = snapshot.AwaitingRematch ? $"NUEVA SERIE · MEJOR DE {snapshot.BestOf}" :
            snapshot.BestOf > 1 ? $"SALA DE ESPERA · MEJOR DE {snapshot.BestOf}" : "SALA DE ESPERA";
        _status.Text = snapshot.Players.Length < 2 ? "Falta otro jugador para comenzar." :
            !teams ? "Debe haber jugadores en los dos equipos." :
            !enough ? "Esperando a que todos estén preparados." :
            host ? "Todos preparados: puedes comenzar." : "Esperando a que el anfitrión inicie la partida.";
        _prizes.Text = !string.IsNullOrWhiteSpace(snapshot.RewardStatus)
            ? snapshot.RewardStatus
            : awardsConfigured
                ? "Al finalizar la serie: 50 oro al ganar, 25 al perder, más los bonos de oro equipados. Botín para el ganador. Requiere Google, equipos humanos equilibrados y actividad suficiente."
                : "SIN RECOMPENSAS: este servidor no tiene habilitado el servicio de cuentas Firebase.";
    }

    private void Reflow()
    {
        if (_panel == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float width = Math.Max(0, Math.Min(950, size.X - 28));
        float height = Math.Max(0, Math.Min(650, size.Y - 28));
        _panel.CustomMinimumSize = new(width, height);
        _details.Visible = size.Y >= 540;
        _teams.Columns = width < 600 ? 1 : 2;
        _actions.Columns = ActionColumns(width);
    }

    private int ActionColumns(float width) => width >= 600 ? _start.Visible ? 3 : 2 : width >= 380 ? 2 : 1;

    public override void _ExitTree() => GetViewport().SizeChanged -= Reflow;
}
