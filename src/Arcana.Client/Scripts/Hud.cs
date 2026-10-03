using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud : Control
{
    private Label _name = null!, _status = null!, _match = null!, _feed = null!, _banner = null!, _server = null!;
    private ProgressBar _health = null!, _shield = null!, _energy = null!, _charge = null!;
    private readonly List<Label> _selected = [], _magicks = [];
    private PanelContainer _scorePanel = null!;
    private GridContainer _scoreRows = null!;
    private MiniMap _map = null!;
    private long _lastEvent;
    private string _feedText = "";
    private float _feedTime, _scoreClock;
    private readonly List<Label> _elementLabels = [];

    public void Build()
    {
        Theme = Ui.Theme(); MouseFilter = MouseFilterEnum.Ignore; Ui.Full(this);
        _server = Ui.Label("", 15, Ui.Gold); _server.Position = new(28, 24); AddChild(_server);
        var controls = Ui.Label("F1  Controles     TAB  Clasificación     ESC  Menú", 14, Ui.Muted);
        controls.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight); controls.Position = new(-440, 24); controls.Size = new(420, 24); AddChild(controls);
        _match = Ui.Label("", 24); _match.HorizontalAlignment = HorizontalAlignment.Center;
        _match.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop); _match.Position = new(-240, 18); _match.Size = new(480, 42); AddChild(_match);
        var playerPanel = new PanelContainer { CustomMinimumSize = new(270, 185), MouseFilter = MouseFilterEnum.Ignore };
        playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft); playerPanel.Position = new(24, -216); AddChild(playerPanel);
        var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 7); playerPanel.AddChild(info);
        _name = Ui.Label("", 20, Ui.Gold); info.AddChild(_name);
        _health = Bar(new("86d9a4"), 1000, 19); info.AddChild(_health);
        _shield = Bar(Ui.Gold, 650, 6); info.AddChild(_shield);
        _energy = Bar(new("b790eb"), 100, 6); info.AddChild(_energy);
        _status = Ui.Label("", 13, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.AddChild(_status);
        var spells = new PanelContainer { CustomMinimumSize = new(690, 0), MouseFilter = MouseFilterEnum.Ignore };
        spells.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom); spells.Position = new(-282, -225); AddChild(spells);
        var stack = new VBoxContainer(); stack.AddThemeConstantOverride("separation", 9); spells.AddChild(stack);
        var first = new HBoxContainer(); first.AddThemeConstantOverride("separation", 14); stack.AddChild(first);
        var queue = new HBoxContainer(); queue.AddThemeConstantOverride("separation", 8); first.AddChild(queue);
        for (int i = 0; i < 3; i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new(75, 46), MouseFilter = MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", Ui.Panel(new("142439"), 6)); queue.AddChild(panel);
            var label = Ui.Label("—", 20, Ui.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; panel.AddChild(label); _selected.Add(label);
        }
        var hint = Ui.Label("Derecho: lanzar\nCentral: sobre ti · Mayús + derecho: área", 14, Ui.Muted); first.AddChild(hint);
        _charge = Bar(Ui.Gold, 1, 5); stack.AddChild(_charge);
        var elements = new HBoxContainer(); elements.AddThemeConstantOverride("separation", 11); stack.AddChild(elements);
        foreach (var e in Enum.GetValues<Element>())
        {
            var label = Ui.Label(Ui.ElementKey(e) + "\n" + Ui.ElementName(e), 15, Ui.ElementColor(e));
            label.HorizontalAlignment = HorizontalAlignment.Center; label.CustomMinimumSize = new(65, 38); label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            elements.AddChild(label); _elementLabels.Add(label);
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
        IgnoreChildren(this);
    }
    private static void IgnoreChildren(Node root)
    {
        foreach (var child in root.GetChildren()) { if (child is Control c) c.MouseFilter = MouseFilterEnum.Ignore; IgnoreChildren(child); }
    }
    private static ProgressBar Bar(Color color, float max, int height)
    {
        var bar = new ProgressBar { MaxValue = max, ShowPercentage = false, CustomMinimumSize = new(0, height) };
        bar.AddThemeStyleboxOverride("background", Ui.Panel(new("1d2b3b"), 3));
        bar.AddThemeStyleboxOverride("fill", Ui.Panel(color, 3, color)); return bar;
    }
    public void Update(WorldSnapshot snapshot, int id, string serverName, double delta)
    {
        var player = snapshot.Players.FirstOrDefault(p => p.Id == id); if (player == null) return;
        _server.Text = serverName.ToUpperInvariant() + "  /  " + Ui.ModeName(snapshot.Mode).ToUpperInvariant();
        _name.Text = player.Name + $"  {Math.Ceiling(player.Health)}/1000";
        _health.Value = player.Health; _shield.Value = player.Shield; _energy.Value = player.Energy; _charge.Value = player.Charge;
        var statuses = new List<string>();
        if (player.Wet > 0) statuses.Add("Mojado"); if (player.Burning > 0) statuses.Add("Quemándose");
        if (player.Chill > 20) statuses.Add("Frío"); if (player.Frozen > 0) statuses.Add("Congelado"); if (player.Haste > 0) statuses.Add("Celeridad");
        if (player.Ward.Length > 0) statuses.Add("Guarda: " + string.Join(", ", player.Ward.Select(Ui.ElementName)));
        _status.Text = (statuses.Count > 0 ? string.Join(" · ", statuses) + "\n" : "") + $"Escudo {Math.Ceiling(player.Shield)} · Energía {Math.Floor(player.Energy)}";
        for (int i = 0; i < 3; i++)
        {
            bool selected = i < player.Selected.Length;
            _selected[i].Text = selected ? Ui.ElementKey(player.Selected[i]) : "—";
            _selected[i].AddThemeColorOverride("font_color", selected ? Ui.ElementColor(player.Selected[i]) : Ui.Muted);
        }
        float[] costs = [25, 35, 75, 60];
        string[] names = ["Celeridad", "Traslación", "Meteoro", "Reanimar"];
        for (int i = 0; i < 4; i++)
        {
            _magicks[i].Text = $"{i + 1} {names[i]} " + (player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" : $"{costs[i]:0}");
            _magicks[i].AddThemeColorOverride("font_color", player.Cooldowns[i] > 0 || player.Energy < costs[i] ? Ui.Muted : Ui.Gold);
        }
        if (snapshot.Mode == GameMode.Arena)
            _match.Text = "ARENA  ·  " + snapshot.Players.Max(p => p.Kills) + " BAJAS";
        else _match.Text = $"AZUL  {snapshot.Scores[0]}     ·     {snapshot.Scores[1]}  ROJO";
        _banner.Text = !player.Alive ? $"HAS CAÍDO\nReapareces en {Math.Ceiling(player.Respawn)} s" : "";
        if (snapshot.MatchEnded)
        {
            string winner = snapshot.Mode == GameMode.Arena ? snapshot.Players.FirstOrDefault(p => p.Id == snapshot.Winner)?.Name ?? "Mago" : snapshot.Winner == 0 ? "Equipo azul" : "Equipo rojo";
            _banner.Text = winner + $" vence\nNueva partida en {Math.Ceiling(snapshot.RestartIn)} s";
        }
        _feedTime -= (float)delta;
        foreach (var e in snapshot.Events.Where(e => e.Id > _lastEvent))
        {
            _lastEvent = Math.Max(_lastEvent, e.Id);
            if (e.Text.Length == 0) continue;
            var lines = (_feedText + "\n" + e.Text).Split('\n').Where(t => t.Length > 0).TakeLast(4);
            _feedText = string.Join("\n", lines); _feedTime = 7;
        }
        _feed.Text = _feedTime > 0 ? _feedText : "";
        _map.State = snapshot; _map.LocalId = id; _map.QueueRedraw();
        _scorePanel.Visible = Input.IsPhysicalKeyPressed(Key.Tab);
        _scoreClock += (float)delta;
        if (_scorePanel.Visible && _scoreClock > 0.25f)
        {
            _scoreClock = 0;
            foreach (var child in _scoreRows.GetChildren()) { _scoreRows.RemoveChild(child); child.QueueFree(); }
            foreach (var text in new[] { "MAGO", "EQUIPO", "BAJAS", "MUERTES", "VIDA" }) _scoreRows.AddChild(Ui.Label(text, 14, Ui.Gold));
            foreach (var p in snapshot.Players.OrderByDescending(p => p.Kills))
            {
                var color = p.Team == 0 ? Ui.Blue : Ui.Red;
                foreach (var text in new[] { p.Name + (p.Id == id ? " (tú)" : ""), p.Team == 0 ? "Azul" : "Rojo", p.Kills.ToString(), p.Deaths.ToString(), p.Alive ? Math.Ceiling(p.Health).ToString() : "Caído" })
                    _scoreRows.AddChild(Ui.Label(text, 17, color));
            }
        }
    }
}
