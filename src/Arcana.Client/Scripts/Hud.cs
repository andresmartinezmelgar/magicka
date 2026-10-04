using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud : Control
{
    private Label _name = null!, _status = null!, _match = null!, _feed = null!, _banner = null!, _server = null!;
    private ProgressBar _health = null!, _shield = null!, _energy = null!, _charge = null!;
    private Label _healthText = null!, _shieldText = null!, _energyText = null!;
    private Label _objective = null!, _affinity = null!;
    private HBoxContainer _teams = null!;
    private Label _blueScore = null!, _redScore = null!;
    private StyleBoxFlat _shieldFill = null!, _shieldTrack = null!;
    private readonly List<Label> _selected = [], _magicks = [];
    private readonly List<ElementIcon> _selectedIcons = [];
    private readonly List<StyleBoxFlat> _selectedStyles = [];
    private PanelContainer _scorePanel = null!;
    private GridContainer _scoreRows = null!;
    private MiniMap _map = null!;
    private long _lastEvent;
    private string _feedText = "";
    private float _feedTime, _scoreClock;
    private readonly List<string> _statuses = [];
    private readonly List<Label> _scoreCells = [];
    private WorldSnapshot? _lastSnapshot;
    private static readonly float[] Costs = [25, 35, 75, 60];
    private static readonly string[] Names = ["Celeridad", "Traslación", "Meteoro", "Reanimar"];

    public void Build()
    {
        Theme = Ui.Theme(); MouseFilter = MouseFilterEnum.Ignore; Ui.Full(this);
        _server = Ui.Label("", 15, Ui.Gold); _server.Position = new(28, 24); AddChild(_server);
        var controls = Ui.Label("F1  Controles     TAB  Clasificación     ESC  Menú", 14, Ui.Muted);
        controls.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight); controls.Position = new(-440, 24); controls.Size = new(420, 24); AddChild(controls);
        _match = Ui.Label("", 24); _match.HorizontalAlignment = HorizontalAlignment.Center;
        _match.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop); _match.Position = new(-240, 18); _match.Size = new(480, 42); AddChild(_match);
        _teams = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _teams.AddThemeConstantOverride("separation", 18); _teams.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _teams.Position = new(-240, 18); _teams.Size = new(480, 42); AddChild(_teams);
        _blueScore = Ui.Label("", 24, Ui.Blue); _redScore = Ui.Label("", 24, Ui.Red);
        _teams.AddChild(_blueScore); _teams.AddChild(Ui.Label("·", 24, Ui.Muted)); _teams.AddChild(_redScore);
        _objective = Ui.Label("", 14, Ui.Gold); _objective.Name = "MatchObjective"; _objective.HorizontalAlignment = HorizontalAlignment.Center;
        _objective.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop); _objective.Position = new(-345, 60); _objective.Size = new(690, 46);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_objective);
        var playerPanel = new PanelContainer { CustomMinimumSize = new(310, 254), MouseFilter = MouseFilterEnum.Ignore };
        playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft); playerPanel.Position = new(24, -282); AddChild(playerPanel);
        var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 7); playerPanel.AddChild(info);
        _name = Ui.Label("", 19); info.AddChild(_name);
        _affinity = Ui.Label("", 13); _affinity.Name = "WizardAffinity"; info.AddChild(_affinity);
        _health = Bar(Ui.Health, 1000, 33); _health.Name = "HealthBar";
        info.AddChild(Meter(_health, "HealthValue", out _healthText));
        _shield = Bar(Ui.Shield, 420, 28); _shield.Name = "ShieldBar";
        _shieldFill = (StyleBoxFlat)_shield.GetThemeStylebox("fill");
        _shieldTrack = (StyleBoxFlat)_shield.GetThemeStylebox("background");
        info.AddChild(Meter(_shield, "ShieldValue", out _shieldText));
        _energy = Bar(Ui.Energy, 100, 20); info.AddChild(Meter(_energy, "EnergyValue", out _energyText, 12));
        _status = Ui.Label("", 13, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.AddChild(_status);
        var spells = new PanelContainer { CustomMinimumSize = new(690, 0), MouseFilter = MouseFilterEnum.Ignore };
        spells.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom); spells.Position = new(-282, -225); AddChild(spells);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 9); spells.AddChild(stack);
        var first = new HBoxContainer(); first.AddThemeConstantOverride("separation", 14); stack.AddChild(first);
        var queue = new HBoxContainer(); queue.AddThemeConstantOverride("separation", 8); first.AddChild(queue);
        for (int i = 0; i < 3; i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new(76, 51), MouseFilter = MouseFilterEnum.Ignore };
            var style = Ui.Panel(new("0b1526"), 6); style.ContentMarginLeft = style.ContentMarginRight = 8;
            style.ContentMarginTop = style.ContentMarginBottom = 6; style.BorderWidthBottom = 3;
            panel.AddThemeStyleboxOverride("panel", style); queue.AddChild(panel); _selectedStyles.Add(style);
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 7); panel.AddChild(row);
            var icon = new ElementIcon(); row.AddChild(icon); _selectedIcons.Add(icon);
            var label = Ui.Label("—", 21, Ui.Muted); row.AddChild(label); _selected.Add(label);
        }
        var hint = Ui.Label("Derecho: lanzar\nCentral: sobre ti · Mayús + derecho: área", 14, Ui.Muted); first.AddChild(hint);
        _charge = Bar(Ui.Gold, 1, 5); stack.AddChild(_charge);
        var elements = new HBoxContainer(); elements.AddThemeConstantOverride("separation", 7); stack.AddChild(elements);
        foreach (var e in Enum.GetValues<Element>())
        {
            elements.AddChild(Ui.ElementTile(e, true));
        }
        var magicks = new HBoxContainer(); magicks.AddThemeConstantOverride("separation", 12); stack.AddChild(magicks);
        foreach (string text in new[] { "1 Celeridad", "2 Traslación", "3 Meteoro", "4 Reanimar" })
        {
            var label = Ui.Label(text, 13, Ui.Gold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; magicks.AddChild(label); _magicks.Add(label);
        }
        _feed = Ui.Label("", 15); _feed.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _feed.Position = new(-450, 245); _feed.Size = new(420, 100); _feed.HorizontalAlignment = HorizontalAlignment.Right; AddChild(_feed);
        var mapPanel = new PanelContainer { CustomMinimumSize = new(205, 151), MouseFilter = MouseFilterEnum.Ignore };
        mapPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight); mapPanel.Position = new(-235, 72); AddChild(mapPanel);
        mapPanel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.05f, 0.08f, 0.90f), 10));
        _map = new MiniMap { CustomMinimumSize = new(165, 119), MouseFilter = MouseFilterEnum.Ignore }; mapPanel.AddChild(_map);
        _banner = Ui.Label("", 32, Ui.Gold); _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.SetAnchorsAndOffsetsPreset(LayoutPreset.Center); _banner.Position = new(-400, -72); _banner.Size = new(800, 144); AddChild(_banner);
        _scorePanel = new PanelContainer { Visible = false, CustomMinimumSize = new(700, 200), MouseFilter = MouseFilterEnum.Ignore };
        _scorePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center); _scorePanel.Position = new(-350, -160); AddChild(_scorePanel);
        _scoreRows = new GridContainer { Columns = 5 }; _scoreRows.AddThemeConstantOverride("h_separation", 30); _scoreRows.AddThemeConstantOverride("v_separation", 12); _scorePanel.AddChild(_scoreRows);
        foreach (string text in new[] { "MAGO", "EQUIPO", "BAJAS", "MUERTES", "VIDA" }) _scoreRows.AddChild(Ui.Label(text, 14, Ui.Gold));
        IgnoreChildren(this);
    }
    private static void IgnoreChildren(Node root)
    {
        foreach (var child in root.GetChildren()) { if (child is Control c) c.MouseFilter = MouseFilterEnum.Ignore; IgnoreChildren(child); }
    }
    private static ProgressBar Bar(Color color, float max, int height)
    {
        var bar = new ProgressBar { MaxValue = max, ShowPercentage = false, CustomMinimumSize = new(0, height) };
        var track = Ui.Panel(new("101a2b"), 4, color.Darkened(0.45f));
        var fill = Ui.Panel(color, 4, color.Lightened(0.18f));
        foreach (var style in new[] { track, fill })
            style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 0;
        bar.AddThemeStyleboxOverride("background", track); bar.AddThemeStyleboxOverride("fill", fill); return bar;
    }
    private static Control Meter(ProgressBar bar, string textName, out Label label, int fontSize = 14)
    {
        var root = new Control { CustomMinimumSize = bar.CustomMinimumSize, MouseFilter = MouseFilterEnum.Ignore };
        root.AddChild(bar); Ui.Full(bar);
        label = Ui.Label("", fontSize); label.Name = textName;
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeConstantOverride("outline_size", 4); root.AddChild(label); Ui.Full(label);
        return root;
    }
    public void Update(WorldSnapshot snapshot, int id, string serverName, double delta)
    {
        _feedTime -= (float)delta; _feed.Text = _feedTime > 0 ? _feedText : "";
        bool opened = !_scorePanel.Visible && Input.IsPhysicalKeyPressed(Key.Tab);
        _scorePanel.Visible = Input.IsPhysicalKeyPressed(Key.Tab); _scoreClock += (float)delta;
        if (_scorePanel.Visible && (opened || _scoreClock >= 0.25f))
        { _scoreClock = 0; UpdateScore(snapshot, id); }
        if (ReferenceEquals(snapshot, _lastSnapshot)) return;
        _lastSnapshot = snapshot;
        var player = snapshot.Players.FirstOrDefault(p => p.Id == id); if (player == null) return;
        _server.Text = serverName.ToUpperInvariant() + "  /  " + Ui.ModeName(snapshot.Mode).ToUpperInvariant();
        _name.Text = player.Name + (player.Team == 0 ? "  /  AZUL" : "  /  ROJO");
        _name.AddThemeColorOverride("font_color", player.Team == 0 ? Ui.Blue : Ui.Red);
        _affinity.Text = WizardTypes.Name(player.WizardType);
        _affinity.TooltipText = WizardTypes.Description(player.WizardType);
        _affinity.AddThemeColorOverride("font_color", Ui.WizardColor(player.WizardType));
        _objective.Text = MatchRules.Objective(snapshot.Mode, snapshot.ScoreLimit, snapshot.StartingTickets);
        _health.MaxValue = player.MaxHealth; _health.Value = player.Health;
        _healthText.Text = $"VIDA  {Math.Ceiling(player.Health):0} / {player.MaxHealth:0}";
        _shield.MaxValue = Ui.ShieldMaximum(player); _shield.Value = player.Shield;
        bool shielded = player.Alive && player.Shield > 0;
        _shieldFill.BgColor = shielded ? Ui.Shield : Ui.Shield.Darkened(0.65f);
        _shieldTrack.BorderColor = shielded ? Ui.Shield : Ui.Muted.Darkened(0.5f);
        _shieldText.Text = shielded ? $"ESCUDO  {Math.Ceiling(player.Shield):0} / {_shield.MaxValue:0}"
            + (player.ShieldSeconds > 0 ? $"  ·  {player.ShieldSeconds:0.0} s" : "") : "ESCUDO  0  /  INACTIVO";
        _shieldText.AddThemeColorOverride("font_color", shielded ? Ui.Text : Ui.Muted);
        _energy.Value = player.Energy; _charge.Value = player.Charge; _energyText.Text = $"ENERGÍA  {Math.Floor(player.Energy):0} / 100";
        _statuses.Clear(); var statuses = _statuses;
        if (player.Wet > 0) statuses.Add("Mojado"); if (player.Burning > 0) statuses.Add("Quemándose");
        if (player.Chill > 20) statuses.Add("Frío"); if (player.Frozen > 0) statuses.Add("Congelado"); if (player.Haste > 0) statuses.Add("Celeridad");
        if (player.Ward.Length > 0) statuses.Add("Guarda: " + string.Join(", ", player.Ward.Select(Ui.ElementName)));
        _status.Text = statuses.Count > 0 ? string.Join(" · ", statuses) : "Sin estados alterados";
        for (int i = 0; i < 3; i++)
        {
            bool selected = i < player.Selected.Length;
            _selected[i].Text = selected ? Ui.ElementKey(player.Selected[i]) : "—";
            _selected[i].AddThemeColorOverride("font_color", selected ? Ui.Text : Ui.Muted);
            _selectedIcons[i].Value = selected ? player.Selected[i] : null;
            _selectedStyles[i].BorderColor = selected ? Ui.ElementColor(player.Selected[i]) : new Color("405372");
        }
        for (int i = 0; i < 4; i++)
        {
            _magicks[i].Text = $"{i + 1} {Names[i]} " + (player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" : $"{Costs[i]:0}");
            _magicks[i].AddThemeColorOverride("font_color", player.Cooldowns[i] > 0 || player.Energy < Costs[i] ? Ui.Muted : Ui.Gold);
        }
        _match.Visible = snapshot.Mode == GameMode.Arena; _teams.Visible = !_match.Visible;
        if (_match.Visible)
            _match.Text = "ARENA  ·  " + snapshot.Players.Max(p => p.Kills) + " BAJAS";
        else { _blueScore.Text = $"AZUL  {snapshot.Scores[0]}"; _redScore.Text = $"{snapshot.Scores[1]}  ROJO"; }
        _banner.Text = !player.Alive ? $"HAS CAÍDO\nReapareces en {Math.Ceiling(player.Respawn)} s" : "";
        if (snapshot.MatchEnded)
        {
            string winner = snapshot.Mode == GameMode.Arena ? snapshot.Players.FirstOrDefault(p => p.Id == snapshot.Winner)?.Name ?? "Mago" : snapshot.Winner == 0 ? "Equipo azul" : "Equipo rojo";
            _banner.Text = winner + $" vence\nNueva partida en {Math.Ceiling(snapshot.RestartIn)} s";
        }
        foreach (var e in snapshot.Events.Where(e => e.Id > _lastEvent))
        {
            _lastEvent = Math.Max(_lastEvent, e.Id);
            if (e.Text.Length == 0) continue;
            var lines = (_feedText + "\n" + e.Text).Split('\n').Where(t => t.Length > 0).TakeLast(4);
            _feedText = string.Join("\n", lines); _feedTime = 7;
        }
        _feed.Text = _feedTime > 0 ? _feedText : "";
        _map.State = snapshot; _map.LocalId = id; _map.QueueRedraw();
    }
    private void UpdateScore(WorldSnapshot snapshot, int id)
    {
        int cell = 0;
        foreach (var p in snapshot.Players.OrderByDescending(p => p.Kills))
        {
            var color = p.Team == 0 ? Ui.Blue : Ui.Red;
            foreach (var text in new[] { p.Name + (p.Id == id ? " (tú)" : ""), p.Team == 0 ? "Azul" : "Rojo", p.Kills.ToString(), p.Deaths.ToString(), p.Alive ? Math.Ceiling(p.Health).ToString() : "Caído" })
            {
                if (cell == _scoreCells.Count)
                {
                    var label = Ui.Label("", 17, color); label.MouseFilter = MouseFilterEnum.Ignore;
                    _scoreRows.AddChild(label); _scoreCells.Add(label);
                }
                var current = _scoreCells[cell++]; current.Text = text; current.Visible = true;
                current.AddThemeColorOverride("font_color", color);
            }
        }
        for (int i = cell; i < _scoreCells.Count; i++) _scoreCells[i].Visible = false;
    }
}
