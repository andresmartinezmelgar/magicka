using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Entrada multitáctil: cada dedo conserva su control hasta soltarse, incluso fuera del área.</summary>
public partial class MobileControls : Control
{
    private sealed record TouchAction(PanelContainer Panel, Label Text, ClientMessage? Message, Action? Run);
    private readonly List<TouchAction> _actions = [];
    private readonly Dictionary<int, TouchAction> _pressed = [];
    private readonly List<TouchAction> _magicks = [];
    private readonly List<TouchAction> _utilities = [];
    private TouchAction _self = null!, _nova = null!;
    private int _moveFinger = -1, _aimFinger = -1;
    private Vector2 _move, _aim = Vector2.Right, _aimKnob;
    private Vector2 _moveCenter, _moveHome, _aimCenter;
    private readonly ElementBuffer _selected = new();
    private bool _awaitingSelectionEcho, _directionalAimPending;
    private SpellShape _aimedSpellShape;
    private Element[]? _aimedProjectile;
    private bool _rockChargeObserved;
    private int _selectionEchoFrames;
    private const float Radius = 82;
    private int _pendingMagick = -1;
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
    private float ControlScale => BaseScale * Settings.Size;
    private float StickRadius => Radius * ControlScale;
    internal Rect2[] ButtonRectanglesForVerification => _actions.Select(a => a.Panel.GetGlobalRect()).ToArray();
    internal Vector2[] StickCentersForVerification => [_moveHome + GlobalPosition, _aimCenter + GlobalPosition];
    internal bool BarrierAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Barrier;
    internal bool IceAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Shards;
    internal Element[]? ProjectileAimElements => _aimFinger >= 0 && _pendingMagick < 0 ? _aimedProjectile : null;
    public Vector2 Movement => _move;
    public Vector2 AimDirection => _aim;
    public event Action<ClientMessage>? ActionRequested;
    public event Action? AimChanged;
    public event Action? MenuRequested, HelpRequested, ScoreRequested;

    public override void _Ready()
    {
        Name = "MobileControls"; Theme = Ui.Theme(); MouseFilter = MouseFilterEnum.Ignore; Ui.Full(this);
        foreach (var element in Enum.GetValues<Element>())
        {
            var action = AddButton(Ui.ElementName(element), new() { Type = "element", Element = element }, color: Ui.ElementColor(element), element: element);
            action.Panel.Name = "TouchElement" + element;
            action.Text.AddThemeFontSizeOverride("font_size", 22);
        }
        _self = AddButton("Sobre mí", new() { Type = "self" }, color: Ui.Health); _self.Panel.Name = "TouchSelf";
        _nova = AddButton("En área", new() { Type = "nova" }, color: Ui.Gold); _nova.Panel.Name = "TouchNova";
        for (int i = 0; i < SpellBalance.Magicks.Count; i++)
        {
            var action = AddButton(SpellBalance.Magicks[i].Name, new() { Type = "magick", Slot = i }, color: Ui.Gold);
            action.Panel.Name = "TouchMagick" + i; _magicks.Add(action);
        }
        _utilities.Add(AddButton("Ayuda", run: () => HelpRequested?.Invoke()));
        _utilities.Add(AddButton("Tabla", run: () => ScoreRequested?.Invoke()));
        _utilities.Add(AddButton("Menú", run: () => MenuRequested?.Invoke()));
        for (int i = 0; i < _utilities.Count; i++) _utilities[i].Panel.Name = "TouchUtility" + i;
        Resized += Layout; Layout();
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
    private void Layout()
    {
        if (_actions.Count == 0) return;
        float width = Size.X, height = Size.Y;
        float s = BaseScale;
        foreach (var action in _actions) action.Text.AddThemeFontSizeOverride("font_size", Math.Max(14, (int)(22 * ControlScale)));
        foreach (var icon in _actions.SelectMany(a => a.Panel.GetChildren()).OfType<Container>().SelectMany(c => c.GetChildren()).OfType<ElementIcon>())
            icon.CustomMinimumSize = new(25 * s, 25 * s);
        _moveHome = new(142 * s, height - 125 * s);
        _moveHome = ControlCenter("Move", _moveHome, new(StickRadius, StickRadius));
        if (_moveFinger < 0) _moveCenter = _moveHome;
        _aimCenter = new(width - 142 * s, height - 125 * s);
        _aimCenter = ControlCenter("Aim", _aimCenter, new(StickRadius, StickRadius));
        for (int i = 0; i < 8; i++) Place(i, new(width - (246 - i % 2 * 116) * s, height - (572 - i / 2 * 80) * s), new(108 * s, 72 * s));
        Place(_self, new(width - 382 * s, height - 452 * s), new(120 * s, 62 * s));
        Place(_nova, new(width - 382 * s, height - 382 * s), new(120 * s, 62 * s));
        for (int i = 0; i < _magicks.Count; i++)
            Place(_magicks[i], new(width / 2 - (244 - i * 124) * s, height - 90 * s), new(120 * s, 72 * s));
        for (int i = 0; i < _utilities.Count; i++) Place(_utilities[i], new(width - (382 - i * 124) * s, 22 * s), new(118 * s, 62 * s));
        QueueRedraw();
    }
    private void Place(int index, Vector2 position, Vector2 size)
        => Place(_actions[index], position, size);
    private void Place(TouchAction action, Vector2 position, Vector2 size)
    {
        var pivot = _utilities.Contains(action) ? new Vector2(Size.X - 8 * BaseScale, 8 * BaseScale) :
            _magicks.Contains(action) ? new Vector2(Size.X / 2, Size.Y - 8 * BaseScale) : new Vector2(Size.X - 8 * BaseScale, Size.Y - 238 * BaseScale);
        float spacing = Settings.Spacing < 1 ? 1 + (Settings.Spacing - 1) * .1f : Settings.Spacing;
        var center = pivot + (position + size / 2 - pivot) * Settings.Size * spacing;
        size *= Settings.Size;
        action.Panel.Size = size;
        action.Panel.Position = ControlCenter(action.Panel.Name, center, size / 2) - size / 2;
    }
    private Vector2 ControlCenter(string name, Vector2 center, Vector2 halfSize)
    {
        if (Settings.LeftHanded) center.X = Size.X - center.X;
        if (Settings.Positions.TryGetValue(name, out var saved)) center = new(saved.X * Size.X, saved.Y * Size.Y);
        return new(Math.Clamp(center.X, halfSize.X + 4, Math.Max(halfSize.X + 4, Size.X - halfSize.X - 4)),
            Math.Clamp(center.Y, halfSize.Y + 4, Math.Max(halfSize.Y + 4, Size.Y - halfSize.Y - 4)));
    }
    public void Reset()
    {
        _moveFinger = _aimFinger = -1; _move = _aimKnob = Vector2.Zero; _moveCenter = _moveHome;
        _directionalAimPending = false;
        _aimedProjectile = null; _rockChargeObserved = false;
        CancelMagickAim();
        _awaitingSelectionEcho = false;
        foreach (var action in _pressed.Values) action.Panel.Modulate = Colors.White;
        _pressed.Clear(); QueueRedraw();
    }
    public void Update(WizardSnapshot? player, WorldSnapshot? world = null)
    {
        if (player == null) return;
        // Local element taps are immediate, whereas a PvP snapshot can arrive a frame later.
        // Keep the optimistic selection until the server echoes it, then resynchronize.
        if (_awaitingSelectionEcho && player.Selected.SequenceEqual(_selected.Elements))
            _awaitingSelectionEcho = false;
        if (!_awaitingSelectionEcho || ++_selectionEchoFrames > 120)
        {
            _selected.Clear();
            foreach (var element in player.Selected) _selected.Add(element);
            _awaitingSelectionEcho = false;
            _selectionEchoFrames = 0;
        }
        if (_aimedProjectile != null && _aimedSpellShape == SpellShape.Rock)
        {
            bool charging = player.Casting && player.CastingElements?.Contains(Element.Earth) == true;
            if (charging) _rockChargeObserved = true;
            else if (_rockChargeObserved || !_awaitingSelectionEcho && player.Selected.Length == 0)
                _aimedProjectile = null; // La carga completa también dispara sin soltar el dedo.
        }
        for (int i = 0; i < _magicks.Count; i++)
        {
            var magick = SpellBalance.Magicks[i];
            float cost = i == 3 ? player.ReanimationCost : magick.Cost;
            bool missingAlly = i == 3 && world != null && MagickRules.ReanimationTarget(world, player) == null;
            bool available = player.Alive && player.Frozen <= 0 && player.Cooldowns[i] <= 0 && player.Energy >= cost && !missingAlly;
            if (_pendingMagick == i && !available) CancelMagickAim();
            _magicks[i].Text.Text = magick.Name + "\n" + (_pendingMagick == i ? "Apuntando" :
                !player.Alive ? "Caído" : player.Frozen > 0 ? "Congelado" : player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" :
                missingAlly ? "Sin aliado" : player.Energy < cost ? $"Faltan {Math.Ceiling(cost - player.Energy)}" : $"{cost:0}");
            _magicks[i].Text.AddThemeColorOverride("font_color", available ? Ui.Gold : Ui.Muted);
        }
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
        if (EditorMode)
        {
            bool edited = input switch
            {
                InputEventScreenTouch touch => EditControl(touch.Index, touch.Position, touch.Pressed && !touch.Canceled),
                InputEventScreenDrag drag => DragControl(drag.Index, drag.Position),
                InputEventMouseButton { ButtonIndex: MouseButton.Left, Device: not -1 } mouse => EditControl(-2, mouse.Position, mouse.Pressed),
                InputEventMouseMotion { Device: not -1 } mouse => DragControl(-2, mouse.Position),
                _ => false
            };
            if (edited) GetViewport().SetInputAsHandled();
            return;
        }
        bool used = input switch
        {
            InputEventScreenTouch touch => Touch(touch.Index, touch.Position, touch.Pressed, touch.Canceled),
            InputEventScreenDrag drag => Drag(drag.Index, drag.Position),
            _ => false
        };
        if (used) GetViewport().SetInputAsHandled();
    }
    internal bool Touch(int finger, Vector2 point, bool down, bool canceled = false)
    {
        if (!IsVisibleInTree()) return false;
        point -= GlobalPosition;
        if (!down || canceled)
        {
            if (finger == _moveFinger) { _moveFinger = -1; _move = Vector2.Zero; _moveCenter = _moveHome; QueueRedraw(); return true; }
            if (finger == _aimFinger)
            {
                _aimFinger = -1; _aimKnob = Vector2.Zero;
                _aimedProjectile = null;
                bool castOnRelease = _directionalAimPending && !canceled;
                _directionalAimPending = false; QueueRedraw();
                if (_pendingMagick >= 0)
                {
                    if (!canceled)
                    {
                        AimChanged?.Invoke();
                        ActionRequested?.Invoke(new() { Type = "magick", Slot = _pendingMagick });
                    }
                    CancelMagickAim();
                    Request(new() { Type = "release" }); return true;
                }
                // Escudos y fragmentos de hielo son instantáneos: el lanzamiento
                // espera la dirección FINAL del gesto, nunca el primer contacto.
                // Un toque cancelado no debe gastar los elementos.
                if (castOnRelease)
                {
                    AimChanged?.Invoke();
                    Request(new() { Type = "cast" });
                }
                Request(new() { Type = "release" }); return true;
            }
            if (_pressed.Remove(finger, out var previous)) { previous.Panel.Modulate = Colors.White; return true; }
            return false;
        }
        if (finger == _moveFinger || finger == _aimFinger || _pressed.ContainsKey(finger)) return true;
        if (PointerBlocked?.Invoke(point + GlobalPosition) == true) return false;
        var action = _actions.FirstOrDefault(a => a.Panel.GetRect().HasPoint(point));
        if (action != null)
        {
            _pressed[finger] = action; action.Panel.Modulate = new Color(1.2f, 1.2f, 1.2f);
            if (action.Message != null)
                Request(new() { Type = action.Message.Type, Element = action.Message.Element, Slot = action.Message.Slot });
            action.Run?.Invoke(); return true;
        }
        // El primer contacto fija el origen. Un dedo ya asignado conserva su función
        // al cruzar botones o la mitad derecha, y un toque nuevo no lo sustituye.
        if (_moveFinger < 0 && new Rect2(new(Settings.LeftHanded ? Size.X / 2 : 0, 0), new(Size.X / 2, Size.Y)).HasPoint(point))
        { _moveFinger = finger; _moveCenter = point; _move = Vector2.Zero; QueueRedraw(); return true; }
        if (_aimFinger < 0 && point.DistanceTo(_aimCenter) <= StickRadius + 20)
        {
            _aimFinger = finger;
            var spell = new SpellRecipe(_selected.Elements);
            _aimedSpellShape = spell.Shape;
            _aimedProjectile = spell.Shape is SpellShape.Rock or SpellShape.Shards ? spell.Elements : null;
            _rockChargeObserved = false;
            _directionalAimPending = _pendingMagick < 0 && (_aimedSpellShape is SpellShape.Barrier or SpellShape.Shards);
            Aim(point); AimChanged?.Invoke();
            if (!_directionalAimPending && _pendingMagick < 0) Request(new() { Type = "cast" });
            QueueRedraw();
            return true;
        }
        return false;
    }
    internal bool Drag(int finger, Vector2 point)
    {
        if (!IsVisibleInTree()) return false;
        point -= GlobalPosition;
        if (finger == _moveFinger) { Move(point); return true; }
        if (finger == _aimFinger) { Aim(point); AimChanged?.Invoke(); return true; }
        return _pressed.ContainsKey(finger);
    }
    private void Request(ClientMessage message)
    {
        if (message.Type == "magick")
        {
            if (_pendingMagick == message.Slot) { CancelMagickAim(); return; }
            if (MagickAvailable?.Invoke(message.Slot) == false) return;
            if (message.Slot is 1 or 2)
            {
                ActionRequested?.Invoke(new() { Type = "release" });
                _directionalAimPending = false; _pendingMagick = message.Slot; _magickAimAmount = 1;
                _aimedProjectile = null;
                if (_aimFinger >= 0) _magickAimAmount = Math.Min(1, _aimKnob.Length() * Settings.Sensitivity);
                AimChanged?.Invoke(); QueueRedraw(); return;
            }
            CancelMagickAim();
        }
        switch (message.Type)
        {
            case "element":
                _selected.Add(message.Element);
                break;
            case "clear":
            case "cast":
            case "self":
            case "nova":
                _selected.Clear();
                break;
            default:
                ActionRequested?.Invoke(message);
                return;
        }
        _awaitingSelectionEcho = true;
        _selectionEchoFrames = 0;
        ActionRequested?.Invoke(message);
    }
    private Vector2 Stick(Vector2 offset)
    {
        if (offset.Length() < Settings.DeadZone * StickRadius) return Vector2.Zero;
        return offset.LimitLength(StickRadius) / StickRadius;
    }
    private void Move(Vector2 point) { _move = Stick(point - _moveCenter); QueueRedraw(); }
    private void Aim(Vector2 point)
    {
        _aimKnob = Stick(point - _aimCenter);
        if (_pendingMagick >= 0) _magickAimAmount = Math.Min(1, _aimKnob.Length() * Settings.Sensitivity);
        if (!_aimKnob.IsZeroApprox()) _aim = _aimKnob.Normalized();
        QueueRedraw();
    }
    private void CancelMagickAim()
    { _pendingMagick = -1; _magickAimAmount = 1; QueueRedraw(); }
    private bool EditControl(int finger, Vector2 point, bool down)
    {
        if (!down)
        {
            if (_editFinger != finger) return false;
            _editFinger = -1; _editingControl = ""; LayoutEdited?.Invoke(); return true;
        }
        if (_editFinger != -1 || PointerBlocked?.Invoke(point) == true) return false;
        point -= GlobalPosition;
        var action = _actions.FirstOrDefault(a => a.Panel.GetRect().HasPoint(point));
        var center = action != null ? action.Panel.Position + action.Panel.Size / 2 :
            point.DistanceTo(_moveHome) <= StickRadius ? _moveHome : point.DistanceTo(_aimCenter) <= StickRadius ? _aimCenter : new Vector2(-9999, -9999);
        if (center.X < -1000) return false;
        _editingControl = action?.Panel.Name.ToString() ?? (center == _moveHome ? "Move" : "Aim");
        _editFinger = finger; _editOffset = point - center; return true;
    }
    private bool DragControl(int finger, Vector2 point)
    {
        if (_editFinger != finger || _editingControl.Length == 0) return false;
        point -= GlobalPosition + _editOffset;
        Settings.Positions[_editingControl] = new(point.X / Math.Max(1, Size.X), point.Y / Math.Max(1, Size.Y));
        Layout(); return true;
    }
    public override void _Draw()
    {
        DrawStick(_moveCenter, _move, "MOVER", Ui.Blue);
        DrawStick(_aimCenter, _aimKnob, _pendingMagick >= 0 ? "SUELTA: " + SpellBalance.Magicks[_pendingMagick].Name.ToUpperInvariant() : _directionalAimPending
            ? _aimedSpellShape == SpellShape.Shards ? "SUELTA: HIELO" : "SUELTA: ESCUDO"
            : "APUNTAR / LANZAR", Ui.Gold);
    }
    private void DrawStick(Vector2 center, Vector2 direction, string caption, Color color)
    {
        DrawCircle(center, StickRadius, new Color(0.035f, 0.055f, 0.09f, 0.86f));
        DrawArc(center, StickRadius, 0, Mathf.Tau, 48, new Color(color, 0.92f), 3, true);
        DrawCircle(center + direction * (StickRadius - 27 * ControlScale), 27 * ControlScale, new Color(color, 0.68f));
        var font = ThemeDB.FallbackFont;
        float textWidth = font.GetStringSize(caption, fontSize: 19).X;
        DrawString(font, center + new Vector2(-textWidth / 2, -StickRadius - 16 * ControlScale), caption, fontSize: 19, modulate: color);
    }
}
