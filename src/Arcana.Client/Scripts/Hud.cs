using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud : Control
{
    internal const float GameplayScale = 0.75f;
    private Label _name = null!, _status = null!, _match = null!, _feed = null!, _banner = null!, _server = null!;
    private ProgressBar _health = null!, _shield = null!, _energy = null!, _charge = null!;
    private Label _healthText = null!, _shieldText = null!, _energyText = null!;
    private Label _objective = null!, _affinity = null!, _buffs = null!;
    private Label _controls = null!;
    private Label _reward = null!;
    private float _rewardSeconds;
    private HBoxContainer _teams = null!;
    private Label _blueScore = null!, _redScore = null!;
    private StyleBoxFlat _shieldFill = null!, _shieldTrack = null!;
    private readonly List<Label> _selected = [], _magicks = [];
    private readonly List<ElementIcon> _selectedIcons = [];
    private readonly List<StyleBoxFlat> _selectedStyles = [];
    private PanelContainer _scorePanel = null!;
    private CenterContainer _scoreCenter = null!;
    private Label _seriesStatus = null!;
    private GridContainer _scoreRows = null!;
    private ScrollContainer _scoreScroll = null!;
    private MiniMap _map = null!;
    private PanelContainer _playerPanel = null!, _spellsPanel = null!;
    private PanelContainer _bannerBackdrop = null!, _rewardBackdrop = null!;
    private PanelContainer _mapPanel = null!;
    private string _lastRewardStatus = "";
    private long _lastEvent;
    private string _feedText = "";
    private float _feedTime, _scoreClock;
    private readonly List<string> _statuses = [];
    private readonly List<Label> _scoreCells = [];
    private WorldSnapshot? _lastSnapshot;
    private WizardSnapshot? _lastPlayer;
    private string? _lastServerName;
    private readonly int[] _magickSeconds = new int[4];
    private readonly float[] _magickCosts = new float[4];
    private readonly bool?[] _magickReady = new bool?[4];
    public bool ScoreOpen { get; set; }
    internal Rect2 ScoreboardRectForSmoke => _scorePanel.GetGlobalRect();
    public event Action? EquipmentRequested;
    public bool PointerOverPanel => _scorePanel.Visible && _scorePanel.GetGlobalRect().HasPoint(GetGlobalMousePosition());
    public void RewardNotice(string message) { _reward.Text = message; _rewardSeconds = 12; _reward.Visible = true; _rewardBackdrop.Visible = true; }

    public void Build()
    {
        Theme = Ui.Theme(); MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.TopLeft); Position = Vector2.Zero;
        Scale = Vector2.One * GameplayScale; ResizeLayout(); GetViewport().SizeChanged += ResizeLayout;
        _server = Ui.Label("", 15, Ui.Gold); _server.Position = new(28, 24);
        _server.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_server);
        var controls = Ui.Label("F1  Controles     TAB  Clasificación     ESC  Menú", 14, Ui.Muted);
        _controls = controls;
        controls.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); controls.Position = new(0, 24); controls.Size = new(420, 24); AddChild(controls);
        controls.Visible = !Ui.IsMobile;
        _match = Ui.Label("", 24); _match.HorizontalAlignment = HorizontalAlignment.Center;
        _match.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _match.Position = new(0, 18); _match.Size = new(480, 42); AddChild(_match);
        _teams = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _teams.AddThemeConstantOverride("separation", 18); _teams.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _teams.Position = new(0, 18); _teams.Size = new(480, 42); AddChild(_teams);
        _blueScore = Ui.Label("", 24, Ui.Blue); _redScore = Ui.Label("", 24, Ui.Red);
        _teams.AddChild(_blueScore); _teams.AddChild(Ui.Label("·", 24, Ui.Muted)); _teams.AddChild(_redScore);
        _objective = Ui.Label("", 14, Ui.Gold); _objective.Name = "MatchObjective"; _objective.HorizontalAlignment = HorizontalAlignment.Center;
        _objective.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _objective.Position = new(0, 60); _objective.Size = new(690, 46);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_objective);
        var playerPanel = new PanelContainer { Name = "PlayerStatusPanel", CustomMinimumSize = new(310, 254), MouseFilter = MouseFilterEnum.Ignore };
        playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        playerPanel.Position = new Vector2(24, 18) / GameplayScale; playerPanel.Scale = Vector2.One / GameplayScale; AddChild(playerPanel);
        _playerPanel = playerPanel;
        var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 7); playerPanel.AddChild(info);
        _name = Ui.Label("", 19); info.AddChild(_name);
        _affinity = Ui.Label("", 13); _affinity.Name = "WizardAffinity"; info.AddChild(_affinity);
        _health = Bar(Ui.Health, 1000, 33); _health.Name = "HealthBar";
        info.AddChild(Meter(_health, "HealthValue", out _healthText));
        _shield = Bar(Ui.Shield, 420, 28); _shield.Name = "ShieldBar";
        _shieldFill = (StyleBoxFlat)_shield.GetThemeStylebox("fill");
        _shieldTrack = (StyleBoxFlat)_shield.GetThemeStylebox("background");
        info.AddChild(Meter(_shield, "ShieldValue", out _shieldText));
        _energy = Bar(Ui.Energy, 100, Ui.IsMobile ? 26 : 20); info.AddChild(Meter(_energy, "EnergyValue", out _energyText, 12));
        _status = Ui.Label("", 13, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.AddChild(_status);
        var spells = new PanelContainer { Name = "SpellControlsPanel", CustomMinimumSize = new(690, 0), MouseFilter = MouseFilterEnum.Ignore };
        _buffs = Ui.Label("", 13, Ui.Gold); _buffs.Name = "ActiveBuffs"; _buffs.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buffs.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _buffs.Position = Vector2.Zero; _buffs.Size = new(690, 44); AddChild(_buffs);
        spells.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); spells.Position = Vector2.Zero; AddChild(spells);
        _spellsPanel = spells;
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
        hint.Visible = !Ui.IsMobile;
        _charge = Bar(Ui.Gold, 1, 5); _charge.Name = "ChargeBar"; stack.AddChild(_charge);
        var elements = new HBoxContainer(); elements.AddThemeConstantOverride("separation", 7); stack.AddChild(elements);
        foreach (var e in Enum.GetValues<Element>())
        {
            elements.AddChild(Ui.ElementTile(e, true));
        }
        elements.Visible = !Ui.IsMobile;
        var magicks = new HBoxContainer(); magicks.AddThemeConstantOverride("separation", 12); stack.AddChild(magicks);
        foreach (string text in SpellBalance.Magicks.Select((m, i) => $"{i + 1} {m.Name}"))
        {
            var label = Ui.Label(text, 13, Ui.Gold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; magicks.AddChild(label); _magicks.Add(label);
        }
        magicks.Visible = !Ui.IsMobile;
        _feed = Ui.Label("", 15); _feed.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _feed.Position = new(0, 245); _feed.Size = new(420, 100); _feed.HorizontalAlignment = HorizontalAlignment.Right; AddChild(_feed);
        var mapPanel = new PanelContainer { Name = "MiniMapPanel", CustomMinimumSize = new(205, 151), MouseFilter = MouseFilterEnum.Ignore };
        mapPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); mapPanel.Position = new(0, 72); AddChild(mapPanel);
        _mapPanel = mapPanel;
        mapPanel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.05f, 0.08f, 0.90f), 10));
        _map = new MiniMap { CustomMinimumSize = new(165, 119), MouseFilter = MouseFilterEnum.Ignore }; mapPanel.AddChild(_map);
        _bannerBackdrop = new PanelContainer { Name = "MatchBannerBackground", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore };
        _bannerBackdrop.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.045f, 0.075f, 0.90f), 14, Ui.Gold.Darkened(0.45f)));
        AddChild(_bannerBackdrop);
        _rewardBackdrop = new PanelContainer { Name = "MatchRewardBackground", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore };
        _rewardBackdrop.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.05f, 0.09f, 0.94f), 10));
        AddChild(_rewardBackdrop);
        _banner = Ui.Label("", 34, Ui.Gold); _banner.Name = "MatchBanner"; _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _banner.Position = Vector2.Zero; _banner.Size = new(800, 144);
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart; AddChild(_banner);
        _reward = Ui.Label("", 24, Ui.Gold); _reward.Name = "MatchRewardNotice"; _reward.HorizontalAlignment = HorizontalAlignment.Center;
        _reward.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _reward.Position = Vector2.Zero; _reward.Size = new(800, 60);
        _reward.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _reward.Visible = false; AddChild(_reward);
        // El CenterContainer ocupa el viewport escalado completo y centra la
        // tabla por su tamaño real; los anclajes anteriores podían dejarla
        // desplazada a la esquina superior izquierda al redimensionar.
        _scoreCenter = new CenterContainer { Name = "ScoreCenter", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_scoreCenter); Ui.Full(_scoreCenter);
        _scorePanel = new PanelContainer { Name = "ScorePanel", Visible = false,
            CustomMinimumSize = new(700, 200), MouseFilter = MouseFilterEnum.Stop };
        _scoreCenter.AddChild(_scorePanel);
        var scoreBox = new VBoxContainer { Name = "ScoreContent" }; scoreBox.AddThemeConstantOverride("separation", 10); _scorePanel.AddChild(scoreBox);
        _seriesStatus = Ui.Label("", 18, Ui.Gold);
        _seriesStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _seriesStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        scoreBox.AddChild(_seriesStatus);
        _scoreScroll = new ScrollContainer { CustomMinimumSize = new(0, 120), HorizontalScrollMode = ScrollContainer.ScrollMode.Auto };
        scoreBox.AddChild(_scoreScroll);
        _scoreRows = new GridContainer { Columns = 5 }; _scoreRows.AddThemeConstantOverride("h_separation", Ui.IsMobile ? 10 : 18); _scoreRows.AddThemeConstantOverride("v_separation", 12); _scoreScroll.AddChild(_scoreRows);
        foreach (string text in new[] { "MAGO", "EQUIPO", "BAJAS", "MUERTES", "VIDA" }) _scoreRows.AddChild(Ui.Label(text, 14, Ui.Gold));
        var equipmentButton = Ui.Button("Ver bonos de los magos", () => EquipmentRequested?.Invoke());
        equipmentButton.Name = "InspectEquipmentButton"; scoreBox.AddChild(equipmentButton);
        scoreBox.MoveChild(equipmentButton, 0);
        if (Ui.IsMobile)
        {
            _server.Visible = _status.Visible = _affinity.Visible = false;
            playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            playerPanel.Position = new Vector2(22, 18) / GameplayScale;
            playerPanel.CustomMinimumSize = new(310, 176); playerPanel.Size = new(310, 176);
            spells.CustomMinimumSize = new(320, 0);
            _feed.Visible = false;
        }
        IgnoreChildren(this);
        ResizeLayout();
    }
    private static void IgnoreChildren(Node root)
    {
        foreach (var child in root.GetChildren())
        { if (child is Control c && child is not (Button or ScrollContainer or ScrollBar)) c.MouseFilter = MouseFilterEnum.Ignore; IgnoreChildren(child); }
    }
    private void ResizeLayout()
    {
        Size = GetViewportRect().Size / GameplayScale;
        // Los avisos y la clasificación deben seguir centrados al cambiar la ventana.
        if (_scorePanel == null) return; // Build invoca ResizeLayout antes de crear los hijos.
        float available = Math.Max(240, Size.X - 36);
        float tableWidth = Math.Min(760, available);
        _scorePanel.CustomMinimumSize = new(tableWidth, 0);
        _scorePanel.Size = new Vector2(tableWidth, Math.Max(150, Math.Min(430, Size.Y - 60)));
        // FullRect ajusta el centrador cuando cambia el viewport; no forzar Size.
        _scoreScroll.CustomMinimumSize = new(0, Math.Max(72, Math.Min(300, Size.Y - 145)));
        float messageWidth = Math.Min(800, available);
        float bannerHeight = Math.Min(180, Math.Max(90, Size.Y * 0.42f));
        // Todos estos controles tienen anclaje arriba-izquierda: las posiciones
        // son absolutas en el HUD escalado y se mantienen dentro del viewport.
        float centerX = (Size.X - messageWidth) / 2;
        float centerY = (Size.Y - bannerHeight) / 2;
        _banner.Position = new(centerX, centerY);
        _banner.Size = new(messageWidth, bannerHeight);
        _bannerBackdrop.Position = _banner.Position;
        _bannerBackdrop.Size = _banner.Size;
        _reward.Position = new(centerX, centerY + bannerHeight + 8);
        _reward.Size = new(messageWidth, Math.Min(80, Math.Max(40, Size.Y / 5)));
        _rewardBackdrop.Position = _reward.Position;
        _rewardBackdrop.Size = _reward.Size;
        float topWidth = Math.Min(480, available);
        _match.Position = new((Size.X - topWidth) / 2, 18);
        _match.Size = new(topWidth, 42);
        _teams.Position = new((Size.X - topWidth) / 2, 18);
        _teams.Size = new(topWidth, 42);
        float objectiveWidth = Math.Min(690, available);
        _objective.Position = new((Size.X - objectiveWidth) / 2, 62);
        _objective.Size = new(objectiveWidth, Size.X < 800 ? 76 : 46);
        _server.Size = new(Math.Max(70, Math.Min(300, (Size.X - 60) / 3)), 58);
        _controls.Visible = !Ui.IsMobile && Size.X >= 1250;
        _controls.Position = new(Size.X - 440, 24);
        float feedWidth = Math.Min(420, available);
        _feed.Position = new(Size.X - feedWidth - 30, Math.Min(245, Math.Max(80, Size.Y - 110)));
        _feed.Size = new(feedWidth, 100);
        _mapPanel.Visible = Size.X >= (Ui.IsMobile ? 850 : 700);
        _mapPanel.Position = Ui.IsMobile ? new(Size.X - 235, 136) : new(Size.X - 235, 72);
        float spellsWidth = Math.Min(Ui.IsMobile ? 320 : 690, Math.Max(100, Size.X - 36));
        float spellsHeight = Math.Max(54, _spellsPanel.GetCombinedMinimumSize().Y);
        float spellsBottom = Ui.IsMobile ? 150 / GameplayScale : 26 / GameplayScale;
        float spellsLeft = (Size.X - spellsWidth) / 2;
        if (!Ui.IsMobile && Size.X >= 1250)
            spellsLeft = Math.Max(spellsLeft, 345 / GameplayScale);
        float spellsTop = Math.Max(120, Size.Y - spellsHeight - spellsBottom);
        _spellsPanel.Position = new(spellsLeft, spellsTop);
        _spellsPanel.Size = new(spellsWidth, spellsHeight);
        float buffsWidth = Math.Min(690, available);
        _buffs.Position = new((Size.X - buffsWidth) / 2, Math.Max(100, spellsTop - 52));
        _buffs.Size = new(buffsWidth, 44);
        if (!Ui.IsMobile)
            _playerPanel.Position = Size.X < 1250
                ? new Vector2(24, 145) / GameplayScale
                : new Vector2(24, Size.Y * GameplayScale - 282) / GameplayScale;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeLayout;
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
        _rewardSeconds = Math.Max(0, _rewardSeconds - (float)delta); _reward.Visible = _rewardSeconds > 0;
        _rewardBackdrop.Visible = _reward.Visible;
        _feedTime -= (float)delta; _feed.Text = _feedTime > 0 ? _feedText : "";
        bool showScore = snapshot.Map != ArenaMapId.Practice && (ScoreOpen || Input.IsPhysicalKeyPressed(Key.Tab));
        bool opened = !_scorePanel.Visible && showScore;
        _scorePanel.Visible = showScore; _scoreClock += (float)delta;
        if (_scorePanel.Visible && (opened || _scoreClock >= 0.25f))
        { _scoreClock = 0; UpdateScore(snapshot, id); }
        if (ReferenceEquals(snapshot, _lastSnapshot)) return;
        var previous = _lastSnapshot;
        _lastSnapshot = snapshot;
        var player = snapshot.Players.FirstOrDefault(p => p.Id == id); if (player == null) return;
        var old = _lastPlayer; _lastPlayer = player;
        if (_lastServerName != serverName || previous?.Mode != snapshot.Mode || previous.Map != snapshot.Map)
        {
            _lastServerName = serverName;
            _server.Text = serverName.ToUpperInvariant() + (snapshot.Map == ArenaMapId.Practice ? "" : "  /  " + Ui.ModeName(snapshot.Mode).ToUpperInvariant());
        }
        if (old == null || old.Name != player.Name || old.Team != player.Team)
        {
            _name.Text = player.Name + (player.Team == 0 ? "  /  AZUL" : "  /  ROJO");
            _name.AddThemeColorOverride("font_color", player.Team == 0 ? Ui.Blue : Ui.Red);
        }
        if (old == null || old.WizardType != player.WizardType)
        {
            _affinity.Text = WizardTypes.Name(player.WizardType);
            _affinity.TooltipText = WizardTypes.Description(player.WizardType);
            _affinity.AddThemeColorOverride("font_color", Ui.WizardColor(player.WizardType));
        }
        if (old == null || !(old.Buffs ?? []).AsSpan().SequenceEqual(player.Buffs ?? []))
        {
            _buffs.Text = RelicBuffs.Summary(player.Buffs); _buffs.Visible = (player.Buffs?.Length ?? 0) > 0;
            _buffs.TooltipText = string.Join("\n", (player.Buffs ?? []).Select(b => RelicBuffs.Name(b.Kind) + " ×" + b.Stacks + ": " + RelicBuffs.Description(b.Kind)));
        }
        _objective.Text = MatchRules.Objective(snapshot.Mode, snapshot.ScoreLimit, snapshot.StartingTickets, snapshot.Map);
        _controls.Text = snapshot.Map == ArenaMapId.Practice ? "F1  Controles     ESC  Menú"
            : "F1  Controles     TAB  Clasificación     ESC  Menú";
        bool hold = snapshot.Mode == GameMode.Warfare && snapshot.Map == ArenaMapId.Citadel;
        bool conquest = snapshot.Map == ArenaMapId.Frontline;
        if (hold) _objective.Text += snapshot.HoldingTeam >= 0
            ? $"\n{(snapshot.HoldingTeam == 0 ? "AZUL" : "ROJO")} · {snapshot.HoldSeconds:0.0} / 15 s"
            : "\nControl total: 0 / 15 s";
        _health.MaxValue = player.MaxHealth; _health.Value = player.Health;
        _healthText.Text = $"VIDA  {Math.Ceiling(player.Health):0} / {player.MaxHealth:0}";
        if (snapshot.Map == ArenaMapId.Ascension) _healthText.Text += $"  (+{player.MaxHealth - Wizard.BaseMaxHealth - 100 * (player.Buffs ?? []).Where(b => b.Kind == BuffKind.Vitality).Sum(b => b.Stacks):0})";
        _shield.MaxValue = Ui.ShieldMaximum(player); _shield.Value = player.Shield;
        bool shielded = player.Alive && player.Shield > 0;
        if (old == null || shielded != (old.Alive && old.Shield > 0))
        {
            _shieldFill.BgColor = shielded ? Ui.Shield : Ui.Shield.Darkened(0.65f);
            _shieldTrack.BorderColor = shielded ? Ui.Shield : Ui.Muted.Darkened(0.5f);
            _shieldText.AddThemeColorOverride("font_color", shielded ? Ui.Text : Ui.Muted);
        }
        _shieldText.Text = shielded ? $"ESCUDO  {Math.Ceiling(player.Shield):0} / {_shield.MaxValue:0}"
            + (player.ShieldSeconds > 0 ? $"  ·  {player.ShieldSeconds:0.0} s" : "") : "ESCUDO  0  /  INACTIVO";
        _energy.Value = player.Energy; _charge.Value = player.Casting ? player.Charge : 0; _energyText.Text = $"ENERGÍA  {Math.Floor(player.Energy):0} / 100";
        _statuses.Clear(); var statuses = _statuses;
        if (player.Wet > 0) statuses.Add("Mojado"); if (player.Burning > 0) statuses.Add("Quemándose");
        if (player.Chill > 20) statuses.Add("Frío"); if (player.Frozen > 0) statuses.Add("Congelado"); if (player.Haste > 0) statuses.Add("Celeridad");
        if (player.Ward.Length > 0) statuses.Add("Guarda: " + string.Join(", ", player.Ward.Select(Ui.ElementName)));
        if (player.RegenerationRate > 0) statuses.Add($"Regeneración +{player.RegenerationRate:0} vida/s");
        _status.Text = statuses.Count > 0 ? string.Join(" · ", statuses) : "Sin estados alterados";
        for (int i = 0; i < 3; i++)
        {
            bool selected = i < player.DisplayElements.Length;
            Element? value = selected ? player.DisplayElements[i] : null;
            if (old != null && _selectedIcons[i].Value == value) continue;
            _selected[i].Text = selected ? Ui.ElementKey(player.DisplayElements[i]) : "—";
            _selected[i].AddThemeColorOverride("font_color", selected ? Ui.Text : Ui.Muted);
            _selectedIcons[i].Value = value;
            _selectedStyles[i].BorderColor = selected ? Ui.ElementColor(player.DisplayElements[i]) : new Color("405372");
        }
        for (int i = 0; i < 4; i++)
        {
            var magick = SpellBalance.Magicks[i];
            float cost = i == 3 ? player.ReanimationCost : magick.Cost;
            int seconds = (int)Math.Ceiling(player.Cooldowns[i]);
            bool ready = player.Cooldowns[i] <= 0 && player.Energy >= cost;
            if (_magickReady[i] == null || _magickSeconds[i] != seconds || _magickCosts[i] != cost)
            {
                _magickSeconds[i] = seconds; _magickCosts[i] = cost;
                _magicks[i].Text = $"{i + 1} {magick.Name} " + (seconds > 0 ? $"{seconds}s" : $"{cost:0}");
            }
            if (_magickReady[i] != ready)
            { _magickReady[i] = ready; _magicks[i].AddThemeColorOverride("font_color", ready ? Ui.Gold : Ui.Muted); }
        }
        _match.Visible = snapshot.Mode == GameMode.Arena; _teams.Visible = !_match.Visible;
        if (snapshot.Map == ArenaMapId.Practice) _match.Visible = _teams.Visible = false;
        if (_match.Visible)
            _match.Text = "ARENA  ·  " + snapshot.Players.Max(p => p.Kills) + " BAJAS";
        else
        {
            _blueScore.Text = $"AZUL  {(hold || conquest ? snapshot.Points.Count(p => p.Owner == 0) + "/" + snapshot.Points.Length : snapshot.Scores[0].ToString())}";
            _redScore.Text = $"{(hold || conquest ? snapshot.Points.Count(p => p.Owner == 1) + "/" + snapshot.Points.Length : snapshot.Scores[1].ToString())}  ROJO";
        }
        _banner.Text = !player.Alive ? "HAS CAÍDO\nReapareces en " +
            (snapshot.Map == ArenaMapId.Ascension ? $"{player.Respawn:0.0}" : $"{Math.Ceiling(player.Respawn):0}") + " s" : "";
        if (snapshot.MatchEnded)
        {
            string winner = snapshot.Mode == GameMode.Arena ? snapshot.Players.FirstOrDefault(p => p.Id == snapshot.Winner)?.Name ?? "Mago" : snapshot.Winner == 0 ? "Equipo azul" : "Equipo rojo";
            string summary = snapshot.BestOf > 1 && snapshot.SeriesWins is { Length: >= 2 } wins
                ? $"\nSerie al mejor de {snapshot.BestOf}: {wins[0]}–{wins[1]}" : "";
            bool seriesCompleted = snapshot.BestOf > 1 && snapshot.SeriesWins is { Length: >= 2 } score &&
                (score[0] >= MatchSeries.WinsNeeded(snapshot.BestOf) || score[1] >= MatchSeries.WinsNeeded(snapshot.BestOf));
            if (seriesCompleted)
            {
                int champion = snapshot.SeriesWins![0] >= MatchSeries.WinsNeeded(snapshot.BestOf) ? 0 : 1;
                _banner.Text = (player.Team == champion ? "HAS GANADO LA SERIE" : "HAS PERDIDO LA SERIE") +
                    $"\n{winner} vence {snapshot.SeriesWins[0]}–{snapshot.SeriesWins[1]}" +
                    $"\nPreparación de la siguiente serie en {Math.Ceiling(snapshot.RestartIn)} s";
            }
            else
                _banner.Text = winner + $" vence{summary}\nSiguiente ronda en {Math.Ceiling(snapshot.RestartIn)} s";
        }
        _bannerBackdrop.Visible = _banner.Text.Length > 0;
        if (snapshot.RewardStatus != _lastRewardStatus)
        {
            _lastRewardStatus = snapshot.RewardStatus;
            if (!string.IsNullOrWhiteSpace(snapshot.RewardStatus)) RewardNotice(snapshot.RewardStatus);
        }
        foreach (var e in snapshot.Events)
        {
            if (e.Id <= _lastEvent) continue;
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
        _scoreScroll.CustomMinimumSize = new(0, Math.Clamp((snapshot.Players.Length + 1) * 33, 65,
            Math.Max(100, (int)Size.Y - 145)));
        _seriesStatus.Text = snapshot.BestOf > 1 && snapshot.SeriesWins is { Length: >= 2 } wins
            ? $"{(wins[0] >= MatchSeries.WinsNeeded(snapshot.BestOf) || wins[1] >= MatchSeries.WinsNeeded(snapshot.BestOf) ? "SERIE FINALIZADA" : $"MEJOR DE {snapshot.BestOf}")} · AZUL {wins[0]} — {wins[1]} ROJO"
            : "CLASIFICACIÓN DE LA PARTIDA";
        int cell = 0;
        foreach (var p in snapshot.Players.OrderByDescending(p => p.Kills))
        {
            var color = p.Team == 0 ? Ui.Blue : Ui.Red;
            foreach (var text in new[] { p.Name + (p.Id == id ? " (tú)" : ""), p.Team == 0 ? "Azul" : "Rojo", p.Kills.ToString(), p.Deaths.ToString(), p.Alive
                ? snapshot.Map is ArenaMapId.Ascension or ArenaMapId.Relics ? $"{Math.Ceiling(p.Health):0} / {p.MaxHealth:0}" : Math.Ceiling(p.Health).ToString() : "Caído" })
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
