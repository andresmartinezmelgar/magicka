using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Hud : Control
{
    internal const float GameplayScale = 0.75f;
    internal bool MobileLayout { get; set; } = Ui.IsMobile;
    private ElementTextLabel _name = null!, _status = null!, _match = null!, _feed = null!, _banner = null!, _server = null!;
    private ProgressBar _health = null!, _shield = null!, _energy = null!, _charge = null!;
    private ElementTextLabel _healthText = null!, _shieldText = null!, _energyText = null!;
    private ElementTextLabel _objective = null!, _affinity = null!, _buffs = null!;
    private ElementTextLabel _controls = null!;
    private ElementTextLabel _reward = null!;
    private float _rewardSeconds;
    private bool _rewardNoticeBlocked;
    private Rect2? _rewardStatusBounds;
    private ElementTextLabel _actionNotice = null!;
    private Panel _actionNoticeBackdrop = null!;
    private float _actionNoticeSeconds;
    internal string ActionNoticeForSmoke => _actionNotice.Text;
    public void ActionNotice(string text)
    {
        _actionNotice.Text = text; _actionNotice.TooltipText = text; _actionNoticeSeconds = 4; _actionNotice.Visible = true;
        _actionNotice.AddThemeColorOverride("font_color", Ui.Gold);
        _actionNoticeBackdrop.Visible = false;
    }
    private HBoxContainer _teams = null!;
    private ElementTextLabel _blueScore = null!, _redScore = null!;
    private StyleBoxFlat _shieldFill = null!, _shieldTrack = null!;
    private readonly List<ElementTextLabel> _selected = [], _magicks = [];
    private readonly List<ElementIcon> _selectedIcons = [];
    private readonly List<StyleBoxFlat> _selectedStyles = [];
    private PanelContainer _scorePanel = null!;
    private CenterContainer _scoreCenter = null!;
    private ElementTextLabel _seriesStatus = null!;
    private GridContainer _scoreRows = null!;
    private ScrollContainer _scoreScroll = null!;
    private MiniMap _map = null!;
    private PanelContainer _playerPanel = null!, _spellsPanel = null!;
    private PanelContainer _bannerBackdrop = null!;
    private Button _rewardBackdrop = null!;
    private CenterContainer _noticeCenter = null!;
    private VBoxContainer _noticeStack = null!, _topScore = null!;
    private PanelContainer _mapPanel = null!;
    private string _lastRewardStatus = "";
    private long _lastEvent;
    private string _feedText = "";
    private float _feedTime, _scoreClock;
    private readonly List<string> _statuses = [];
    private readonly List<ElementTextLabel> _scoreCells = [];
    private WorldSnapshot? _lastSnapshot;
    private WizardSnapshot? _lastPlayer;
    private string? _lastServerName;
    private readonly int[] _magickSeconds = new int[4];
    private readonly float[] _magickCosts = new float[4];
    private readonly bool?[] _magickReady = new bool?[4];
    public bool ScoreOpen { get; set; }
    internal Rect2 ScoreboardRectForSmoke => _scorePanel.GetGlobalRect();
    internal string RoundAnnouncementForSmoke => _banner.Text;
    internal bool RoundAnnouncementVisibleForSmoke => _banner.IsVisibleInTree() && _bannerBackdrop.IsVisibleInTree();
    public event Action? EquipmentRequested;
    public event Action? PersonalDamageRequested;
    public event Action? AutoMeteorRequested;
    internal bool RewardNoticeContains(Vector2 point) => _rewardBackdrop.IsVisibleInTree() && _rewardBackdrop.GetGlobalRect().HasPoint(point);
    public bool PointerOverPanel => _scorePanel.Visible && _scorePanel.GetGlobalRect().HasPoint(GetGlobalMousePosition()) ||
        RewardNoticeContains(GetGlobalMousePosition()) ||
        _magicks.Count > 2 && _magicks[2].IsVisibleInTree() && _magicks[2].GetGlobalRect().HasPoint(GetGlobalMousePosition());
    public void RewardNotice(string message)
    {
        _reward.Text = message; _reward.TooltipText = message; _rewardSeconds = 2.2f;
        _reward.Visible = _rewardBackdrop.Visible = !_rewardNoticeBlocked;
        _rewardBackdrop.Modulate = Colors.White;
        PlaceRewardNotice();
    }
    internal void SetRewardNoticeBlocked(bool blocked)
    {
        _rewardNoticeBlocked = blocked;
        _reward.Visible = _rewardBackdrop.Visible = !blocked && _rewardSeconds > 0;
    }
    internal void SetRewardStatusBounds(Rect2? bounds)
    {
        if (_rewardStatusBounds == bounds) return;
        _rewardStatusBounds = bounds;
        if (_rewardSeconds > 0) PlaceRewardNotice();
    }
    private void DismissRewardNotice()
    {
        _rewardSeconds = 0;
        _reward.Visible = _rewardBackdrop.Visible = false;
    }

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
        controls.Visible = !MobileLayout;
        var topCenter = new HBoxContainer { Name = "HudTopCenter", MouseFilter = MouseFilterEnum.Ignore };
        topCenter.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        topCenter.OffsetTop = 12;
        topCenter.OffsetBottom = 120;
        AddChild(topCenter);
        topCenter.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _topScore = new VBoxContainer { Name = "HudTopScore", CustomMinimumSize = new Vector2(480, 0),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        _topScore.AddThemeConstantOverride("separation", 0);
        topCenter.AddChild(_topScore);
        topCenter.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        _match = Ui.Label("", 24); _match.HorizontalAlignment = HorizontalAlignment.Center;
        _match.CustomMinimumSize = new Vector2(0, 42);
        _match.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _topScore.AddChild(_match);
        _teams = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(0, 42) };
        _teams.AddThemeConstantOverride("separation", 18);
        _topScore.AddChild(_teams);
        _blueScore = Ui.Label("", 24, Ui.Blue); _redScore = Ui.Label("", 24, Ui.Red);
        _teams.AddChild(_blueScore); _teams.AddChild(Ui.Label("·", 24, Ui.Muted)); _teams.AddChild(_redScore);
        _objective = Ui.Label("", 14, Ui.Gold); _objective.Name = "MatchObjective";
        _objective.HorizontalAlignment = HorizontalAlignment.Center;
        _objective.CustomMinimumSize = new Vector2(0, 46);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _topScore.AddChild(_objective);
        var playerPanel = new PanelContainer { Name = "PlayerStatusPanel", CustomMinimumSize = new(310, 254), MouseFilter = MouseFilterEnum.Ignore };
        playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        playerPanel.Position = new Vector2(24, 18) / GameplayScale; playerPanel.Scale = Vector2.One / GameplayScale; AddChild(playerPanel);
        _playerPanel = playerPanel;
        var info = new VBoxContainer(); info.AddThemeConstantOverride("separation", 7); playerPanel.AddChild(info);
        _name = Ui.Label("", 19); _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; info.AddChild(_name);
        _affinity = Ui.Label("", 13); _affinity.Name = "WizardAffinity"; info.AddChild(_affinity);
        _health = Bar(Ui.Health, 1000, 33); _health.Name = "HealthBar";
        info.AddChild(Meter(_health, "HealthValue", out _healthText));
        _shield = Bar(Ui.Shield, 420, 28); _shield.Name = "ShieldBar";
        _shieldFill = (StyleBoxFlat)_shield.GetThemeStylebox("fill");
        _shieldTrack = (StyleBoxFlat)_shield.GetThemeStylebox("background");
        info.AddChild(Meter(_shield, "ShieldValue", out _shieldText));
        _energy = Bar(Ui.Energy, 100, MobileLayout ? 26 : 20); info.AddChild(Meter(_energy, "EnergyValue", out _energyText, 12));
        _status = Ui.Label("", 13, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; info.AddChild(_status);
        var spells = new PanelContainer { Name = "SpellControlsPanel", CustomMinimumSize = new(690, 0), MouseFilter = MouseFilterEnum.Ignore };
        _buffs = Ui.Label("", 13, Ui.Gold); _buffs.Name = "ActiveBuffs"; _buffs.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buffs.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); _buffs.Position = Vector2.Zero; _buffs.Size = new(690, 44); AddChild(_buffs);
        spells.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); spells.Position = Vector2.Zero; AddChild(spells);
        _spellsPanel = spells;
        _actionNotice = Ui.Label("", 18, Ui.Gold); _actionNotice.Name = "MagickFeedback";
        _actionNotice.HorizontalAlignment = HorizontalAlignment.Center;
        _actionNotice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _actionNotice.Visible = false; AddChild(_actionNotice);
        _actionNoticeBackdrop = new Panel { Name = "SpellInterruptionBackdrop", Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        var interruptionStyle = Ui.Panel(new Color("101827"), 7, Ui.Gold);
        interruptionStyle.BorderWidthLeft = interruptionStyle.BorderWidthRight = interruptionStyle.BorderWidthTop = interruptionStyle.BorderWidthBottom = 2;
        _actionNoticeBackdrop.AddThemeStyleboxOverride("panel", interruptionStyle);
        _actionNotice.AddChild(_actionNoticeBackdrop); Ui.Full(_actionNoticeBackdrop); _actionNotice.MoveChild(_actionNoticeBackdrop, 0);
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
        hint.Visible = !MobileLayout;
        _charge = Bar(Ui.Gold, 1, 5); _charge.Name = "ChargeBar"; stack.AddChild(_charge);
        var elements = new HBoxContainer(); elements.AddThemeConstantOverride("separation", 7); stack.AddChild(elements);
        foreach (var e in Enum.GetValues<Element>())
        {
            elements.AddChild(Ui.ElementTile(e, true));
        }
        elements.Visible = !MobileLayout;
        var magicks = new HBoxContainer(); magicks.AddThemeConstantOverride("separation", 12); stack.AddChild(magicks);
        foreach (string text in SpellBalance.Magicks.Select((m, i) => $"{i + 1} {m.Name}"))
        {
            var label = Ui.Label(text, 13, Ui.Gold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; magicks.AddChild(label); _magicks.Add(label);
            if (_magicks.Count == 3)
            {
                label.Name = "DesktopMeteorAction";
                label.TooltipText = "Doble clic: Meteoro sobre el grupo más grande de enemigos al alcance.";
                label.GuiInput += input =>
                {
                    if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse) return;
                    label.AcceptEvent();
                    if (mouse.Pressed && mouse.DoubleClick) AutoMeteorRequested?.Invoke();
                };
            }
        }
        magicks.Visible = !MobileLayout;
        _feed = Ui.Label("", 15); _feed.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _feed.Position = new(0, 245); _feed.Size = new(420, 100); _feed.HorizontalAlignment = HorizontalAlignment.Right; AddChild(_feed);
        var mapPanel = new PanelContainer { Name = "MiniMapPanel", CustomMinimumSize = new(205, 151), MouseFilter = MouseFilterEnum.Ignore };
        mapPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft); mapPanel.Position = new(0, 72); AddChild(mapPanel);
        _mapPanel = mapPanel;
        mapPanel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.05f, 0.08f, 0.90f), 10));
        _map = new MiniMap { CustomMinimumSize = new(165, 119), MouseFilter = MouseFilterEnum.Ignore }; mapPanel.AddChild(_map);
        // Solo el resultado de la partida ocupa el centro. El botín usa un aviso lateral.
        _noticeCenter = new CenterContainer { Name = "HudCenteredNotices",
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_noticeCenter);
        Ui.Full(_noticeCenter);
        _noticeStack = new VBoxContainer { Name = "HudNoticeStack",
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _noticeStack.AddThemeConstantOverride("separation", 8);
        _noticeCenter.AddChild(_noticeStack);
        _bannerBackdrop = new PanelContainer { Name = "MatchBannerBackground", Visible = false,
            MouseFilter = MouseFilterEnum.Ignore };
        _bannerBackdrop.AddThemeStyleboxOverride("panel", Ui.Panel(new Color(0.025f, 0.045f, 0.075f, 0.90f), 14, Ui.Gold.Darkened(0.45f)));
        _noticeStack.AddChild(_bannerBackdrop);
        // Toda la tarjeta responde al clic/toque. El botón consume el gesto al soltarlo,
        // mientras la etiqueta deja pasar el evento y el mundo no recibe el contacto.
        _rewardBackdrop = new Button { Name = "MatchRewardBackground", Visible = false, ZIndex = 1,
            FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand, TooltipText = "Cerrar aviso" };
        var rewardStyle = Ui.Panel(new Color(0.025f, 0.05f, 0.09f, 0.94f), 10);
        foreach (string state in new[] { "normal", "hover", "pressed" })
            _rewardBackdrop.AddThemeStyleboxOverride(state, rewardStyle);
        _rewardBackdrop.Pressed += DismissRewardNotice;
        AddChild(_rewardBackdrop);
        _banner = Ui.Label("", 34, Ui.Gold); _banner.Name = "MatchBanner";
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.VerticalAlignment = VerticalAlignment.Center;
        _banner.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bannerBackdrop.AddChild(_banner);
        _reward = Ui.Label("", 18, Ui.Gold); _reward.Name = "MatchRewardNotice";
        _reward.HorizontalAlignment = HorizontalAlignment.Left;
        _reward.VerticalAlignment = VerticalAlignment.Center;
        _reward.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _reward.MaxLinesVisible = 2;
        _reward.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _reward.Visible = false;
        _rewardBackdrop.AddChild(_reward);
        Ui.Full(_reward);
        _reward.OffsetLeft = 12; _reward.OffsetRight = -12;
        _reward.OffsetTop = 8; _reward.OffsetBottom = -8;
        // El CenterContainer ocupa el viewport escalado completo y centra la
        // tabla por su tamaño real; los anclajes anteriores podían dejarla
        // desplazada a la esquina superior izquierda al redimensionar.
        _scoreCenter = new CenterContainer { Name = "ScoreCenter", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_scoreCenter); Ui.Full(_scoreCenter);
        _scorePanel = new PanelContainer { Name = "ScorePanel", Visible = false,
            CustomMinimumSize = new(700, 200), MouseFilter = MouseFilterEnum.Stop };
        _scoreCenter.AddChild(_scorePanel);
        // La clasificación queda delante de los avisos de muerte y fin de partida.
        var scoreBox = new VBoxContainer { Name = "ScoreContent" }; scoreBox.AddThemeConstantOverride("separation", 10); _scorePanel.AddChild(scoreBox);
        _seriesStatus = Ui.Label("", 18, Ui.Gold);
        _seriesStatus.HorizontalAlignment = HorizontalAlignment.Center;
        _seriesStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        scoreBox.AddChild(_seriesStatus);
        _scoreScroll = new ContentScrollContainer { CustomMinimumSize = new(0, 150),
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto };
        scoreBox.AddChild(_scoreScroll);
        Ui.VisibleVerticalScroll(_scoreScroll);
        _scoreRows = Ui.Table(5, "ScoreTable"); _scoreScroll.AddChild(_scoreRows);
        foreach (string text in new[] { "MAGO", "EQUIPO", "BAJAS", "MUERTES", "VIDA" }) Ui.TableCell(_scoreRows, text, header: true, size: 14);
        var equipmentButton = Ui.Button("Ver bonos de los magos", () => EquipmentRequested?.Invoke());
        var scoreActions = new HBoxContainer(); scoreBox.AddChild(scoreActions); scoreBox.MoveChild(scoreActions, 0);
        equipmentButton.Name = "InspectEquipmentButton"; equipmentButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; scoreActions.AddChild(equipmentButton);
        var damageButton = Ui.Button("Ver mi daño de esta partida", () => PersonalDamageRequested?.Invoke());
        damageButton.Name = "PersonalDamageButton"; damageButton.SizeFlagsHorizontal = SizeFlags.ExpandFill; scoreActions.AddChild(damageButton);
        if (MobileLayout)
        {
            _server.Visible = _status.Visible = _affinity.Visible = false;
            playerPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            playerPanel.Position = new Vector2(22, 18) / GameplayScale;
            playerPanel.CustomMinimumSize = new(310, 176); playerPanel.Size = new(310, 176);
            spells.CustomMinimumSize = new(320, 0);
            _feed.Visible = false;
        }
        IgnoreChildren(this);
        _magicks[2].MouseFilter = MouseFilterEnum.Stop;
        ResizeLayout();
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizeLayout;
}
