using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Entrada multitáctil: cada dedo conserva su control hasta soltarse, incluso fuera del área.</summary>
public partial class MobileControls : Control
{
    private sealed record TouchAction(PanelContainer Panel, ElementTextLabel Text, ClientMessage? Message, Action? Run);
    private readonly List<TouchAction> _actions = [];
    private readonly Dictionary<int, TouchAction> _pressed = [];
    private readonly List<TouchAction> _magicks = [];
    private readonly List<TouchAction> _utilities = [];
    private TouchAction _self = null!, _nova = null!;
    private int _moveFinger = -1, _aimFinger = -1;
    private Vector2 _move, _aim = Vector2.Right, _aimKnob;
    private Vector2 _moveCenter, _moveHome, _aimCenter, _aimHome;
    private readonly PredictedElementSelection _selected = new();
    private bool _directionalAimPending;
    private SpellShape _aimedSpellShape;
    private Element[]? _aimedProjectile;
    private bool _rockChargeObserved;
    private const float Radius = 82;
    private int _pendingMagick = -1;
    private ulong? _meteorTapAt;
    private float _magickAimAmount = 1;
    public TouchSettings Settings { get; private set; } = new();
    public bool EditorMode { get; set; }
    private string _editingControl = "";
    private int _editFinger = -1;
    private Vector2 _editOffset;
    public event Action? LayoutEdited;
    public void Configure(TouchSettings settings) { Reset(); Settings = settings.Normalize(); Layout(); }
    public Func<int, bool>? MagickAvailable { get; set; }
    public int PendingMagick => _pendingMagick;
    public float AimRange => _pendingMagick == 1 ? MagickRules.BlinkRange : MagickRules.MeteorRange;
    public float AimStrength => _pendingMagick >= 0 ? _magickAimAmount : 1;
    public Func<Vector2, bool>? PointerBlocked { get; set; }
    private float BaseScale => Math.Min(1, Size.Y / 720f);
    private float ControlScale => BaseScale * Settings.Size * _fitScale;
    private float StickRadius(string name) => Radius * ControlScale * Settings.ControlScale(name);
    private float MoveRadius => StickRadius("Move");
    private float AimRadius => StickRadius("Aim");
    internal Rect2[] ButtonRectanglesForVerification => _actions.Where(a => a.Panel.Visible).Select(a => a.Panel.GetGlobalRect()).ToArray();
    internal Vector2[] StickCentersForVerification => [_moveHome + GlobalPosition, _aimHome + GlobalPosition];
    internal Vector2[] ActiveStickCentersForVerification => [_moveCenter + GlobalPosition, _aimCenter + GlobalPosition];
    internal float StickRadiusForVerification => MoveRadius;
    internal float[] StickRadiiForVerification => [MoveRadius, AimRadius];
    internal bool BarrierAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Barrier;
    internal bool IceAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Shards;
    internal Element[]? ProjectileAimElements => _aimFinger >= 0 && _pendingMagick < 0 ? _aimedProjectile : null;
    public Vector2 Movement => _move;
    public Vector2 AimDirection => _aim;
    internal bool Aiming => _aimFinger >= 0 && !_aimKnob.IsZeroApprox();
    public event Action<ClientMessage>? ActionRequested;
    public event Action? AimChanged;
    public event Action? MenuRequested, HelpRequested, ScoreRequested;
    public event Action? AutoMeteorRequested;

    public override void _Ready()
    {
        Name = "MobileControls"; Theme = Ui.Theme(); MouseFilter = MouseFilterEnum.Ignore; Ui.Full(this);
        foreach (var element in Enum.GetValues<Element>())
        {
            var action = AddButton(Ui.ElementName(element), new() { Type = "element", Element = element }, color: Ui.ElementColor(element), element: element);
            action.Panel.Name = "TouchElement" + element;
            action.Text.AddThemeFontSizeOverride("font_size", 22);
        }
        BuildSpellShortcuts();
        _self = AddButton("Sobre mí", new() { Type = "self" }, color: Ui.Health); _self.Panel.Name = "TouchSelf";
        _nova = AddButton("En área", new() { Type = "nova" }, color: Ui.Gold); _nova.Panel.Name = "TouchNova";
        for (int i = 0; i < SpellBalance.Magicks.Count; i++)
        {
            var action = AddButton(SpellBalance.Magicks[i].Name, new() { Type = "magick", Slot = i }, color: Ui.Gold);
            action.Text.ClipText = true;
            action.Text.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            action.Panel.Name = "TouchMagick" + i; _magicks.Add(action);
        }
        _utilities.Add(AddButton("Ayuda", run: () => HelpRequested?.Invoke()));
        _utilities.Add(AddButton("Tabla", run: () => ScoreRequested?.Invoke()));
        _utilities.Add(AddButton("Menú", run: () => MenuRequested?.Invoke()));
        for (int i = 0; i < _utilities.Count; i++) _utilities[i].Panel.Name = "TouchUtility" + i;
        BuildEditorOverlay();
        Resized += Layout;
        ConfigureSpellShortcuts(new() { Enabled = AutomaticSpells });
    }
    private TouchAction AddButton(string title, ClientMessage? message = null, Action? run = null, Color? color = null, Element? element = null)
    {
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        var style = Ui.Panel(new Color(0.035f, 0.055f, 0.09f, 0.97f), 10, color ?? Ui.Muted);
        if (element != null) style.BorderWidthBottom = style.BorderWidthTop = style.BorderWidthLeft = style.BorderWidthRight = 3;
        style.ContentMarginLeft = style.ContentMarginRight = 4;
        style.ContentMarginTop = style.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", style); AddChild(panel);
        var label = Ui.Label(title, 22, color ?? Ui.Text);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        if (element != null)
        {
            var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            content.AddThemeConstantOverride("separation", 1); panel.AddChild(content);
            content.AddChild(new ElementIcon { Value = element, CustomMinimumSize = new(25, 25), SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
            content.AddChild(label);
        }
        else panel.AddChild(label);
        var action = new TouchAction(panel, label, message, run); _actions.Add(action); return action;
    }
    public void Reset()
    {
        _moveFinger = _aimFinger = -1; _move = _aimKnob = Vector2.Zero; _moveCenter = _moveHome; _aimCenter = _aimHome;
        _directionalAimPending = false;
        _aimedProjectile = null; _rockChargeObserved = false;
        CancelMagickAim();
        _selected.Reset();
        ResetSpellShortcuts();
        ClearEditorSelection();
        foreach (var action in _pressed.Values) action.Panel.Modulate = Colors.White;
        _pressed.Clear(); QueueRedraw();
    }
    public void Update(WizardSnapshot? player, WorldSnapshot? world = null)
    {
        UpdateSpellShortcuts(player, world);
        if (player == null) return;
        _selected.Confirm(player.Selected, player.SelectionSequence);
        if (!_selected.Pending && _selected.Elements.Count == 0) _preparedShortcut = null;
        if (_aimedProjectile != null && _aimedSpellShape == SpellShape.Rock)
        {
            bool charging = player.Casting && player.CastingElements?.Contains(Element.Earth) == true;
            if (charging) _rockChargeObserved = true;
            else if (_rockChargeObserved || !_selected.Pending && player.Selected.Length == 0)
                _aimedProjectile = null; // La carga completa también dispara sin soltar el dedo.
        }
        for (int i = 0; i < _magicks.Count; i++)
        {
            var magick = SpellBalance.Magicks[i];
            float cost = i == 3 ? player.ReanimationCost : magick.Cost;
            bool missingAlly = i == 3 && world != null && MagickRules.ReanimationTarget(world, player) == null;
            bool available = player.Alive && player.Frozen <= 0 && !player.Stunned && player.Cooldowns[i] <= 0 && player.Energy >= cost && !missingAlly;
            if (_pendingMagick == i && !available) CancelMagickAim();
            string caption = magick.Name + "\n" + (_pendingMagick == i ? "Apuntando" :
                !player.Alive ? "Caído" : player.Frozen > 0 ? "Congelado" : player.Stunned ? "Aturdido" : player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" :
                missingAlly ? "Sin aliado" : player.Energy < cost ? $"Faltan {Math.Ceiling(cost - player.Energy)}" : $"{cost:0}");
            if (_magicks[i].Text.Text != caption)
            {
                _magicks[i].Text.Text = caption;
                FitActionCaption(_magicks[i]);
            }
            _magicks[i].Text.AddThemeColorOverride("font_color", available ? Ui.Gold : Ui.Muted);
        }
    }

    private static void FitActionCaption(TouchAction action)
    {
        var available = action.Panel.Size - action.Panel.GetThemeStylebox("panel").GetMinimumSize();
        if (available.X <= 0 || available.Y <= 0) return;
        var font = action.Text.GetThemeFont("font");
        string[] lines = action.Text.Text.Split('\n');
        int fontSize = 22;
        while (fontSize > 12 && (lines.Any(line => font.GetStringSize(line, fontSize: fontSize).X > available.X) ||
            font.GetHeight(fontSize) * lines.Length + action.Text.GetThemeConstant("line_spacing") * (lines.Length - 1) > available.Y)) fontSize--;
        if (action.Text.GetThemeFontSize("font_size") != fontSize) action.Text.AddThemeFontSizeOverride("font_size", fontSize);
    }
}
