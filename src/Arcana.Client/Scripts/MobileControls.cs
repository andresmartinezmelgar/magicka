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
    private Vector2 _moveCenter, _moveHome, _moveOrigin, _aimCenter;
    private readonly PredictedElementSelection _selected = new();
    private bool _directionalAimPending;
    private SpellShape _aimedSpellShape;
    private Element[]? _aimedProjectile;
    private bool _rockChargeObserved;
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
    private float ControlScale => BaseScale * Settings.Size * _fitScale;
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
            action.Text.ClipText = true;
            action.Text.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
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
    public void Reset()
    {
        _moveFinger = _aimFinger = -1; _move = _aimKnob = Vector2.Zero; _moveCenter = _moveHome;
        _directionalAimPending = false;
        _aimedProjectile = null; _rockChargeObserved = false;
        CancelMagickAim();
        _selected.Reset();
        foreach (var action in _pressed.Values) action.Panel.Modulate = Colors.White;
        _pressed.Clear(); QueueRedraw();
    }
    public void Update(WizardSnapshot? player, WorldSnapshot? world = null)
    {
        if (player == null) return;
        _selected.Confirm(player.Selected, player.SelectionSequence);
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
            bool available = player.Alive && player.Frozen <= 0 && player.Cooldowns[i] <= 0 && player.Energy >= cost && !missingAlly;
            if (_pendingMagick == i && !available) CancelMagickAim();
            string caption = magick.Name + "\n" + (_pendingMagick == i ? "Apuntando" :
                !player.Alive ? "Caído" : player.Frozen > 0 ? "Congelado" : player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" :
                missingAlly ? "Sin aliado" : player.Energy < cost ? $"Faltan {Math.Ceiling(cost - player.Energy)}" : $"{cost:0}");
            if (_magicks[i].Text.Text != caption)
            {
                _magicks[i].Text.Text = caption;
                FitMagickCaption(_magicks[i]);
            }
            _magicks[i].Text.AddThemeColorOverride("font_color", available ? Ui.Gold : Ui.Muted);
        }
    }

    private static void FitMagickCaption(TouchAction action)
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
        var action = _actions.FirstOrDefault(a => ButtonRect(a).HasPoint(point));
        if (action != null)
        {
            _pressed[finger] = action; action.Panel.Modulate = new Color(1.2f, 1.2f, 1.2f);
            if (action.Message != null)
                Request(new() { Type = action.Message.Type, Element = action.Message.Element, Slot = action.Message.Slot });
            action.Run?.Invoke(); return true;
        }
        if (_hudAreas.Any(area => area.Bounds.HasPoint(point + GlobalPosition)) ||
            MapRectangle?.HasPoint(point + GlobalPosition) == true) return false;
        // Las posiciones editadas tienen prioridad sobre la zona flotante de movimiento.
        // Un toque adicional en un joystick ocupado nunca se reasigna al otro control.
        float aimDistance = point.DistanceTo(_aimCenter), moveDistance = point.DistanceTo(_moveHome);
        bool nearAim = aimDistance <= StickRadius + 20, nearMove = moveDistance <= StickRadius + 20;
        if (nearAim && (!nearMove || aimDistance <= moveDistance))
        {
            if (_aimFinger >= 0) return true;
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
        bool movementZone = new Rect2(new(Settings.LeftHanded ? Size.X / 2 : 0, 0), new(Size.X / 2, Size.Y)).HasPoint(point);
        if (nearMove || movementZone)
        {
            if (_moveFinger >= 0) return true;
            _moveFinger = finger; _moveOrigin = point;
            _moveCenter = FreeMoveCenter(point); _move = Vector2.Zero; QueueRedraw(); return true;
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
        bool selectionAction = message.Type is "element" or "clear" or "cast" or "self" or "nova";
        if (selectionAction) _selected.Predict(message);
        ActionRequested?.Invoke(message);
        // Submit asigna la secuencia de la conexión o de la partida local.
        if (selectionAction) _selected.Track(message);
    }
    private Vector2 Stick(Vector2 offset)
    {
        if (offset.Length() < Settings.DeadZone * StickRadius) return Vector2.Zero;
        return offset.LimitLength(StickRadius) / StickRadius;
    }
    private void Move(Vector2 point) { _move = Stick(point - _moveOrigin); QueueRedraw(); }
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
        var action = _actions.FirstOrDefault(a => ButtonRect(a).HasPoint(point));
        var center = action != null ? ButtonRect(action).GetCenter() :
            point.DistanceTo(_moveHome) <= StickRadius ? _moveHome : point.DistanceTo(_aimCenter) <= StickRadius ? _aimCenter : new Vector2(-9999, -9999);
        if (center.X < -1000) return false;
        _editingControl = action?.Panel.Name.ToString() ?? (center == _moveHome ? "Move" : "Aim");
        _editFinger = finger; _editOffset = point - center; return true;
    }
    private bool DragControl(int finger, Vector2 point)
    {
        if (_editFinger != finger || _editingControl.Length == 0) return false;
        point -= GlobalPosition + _editOffset;
        MoveEditedControl(point); return true;
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
        DrawArc(center, StickRadius - 1.5f, 0, Mathf.Tau, 48, new Color(color, 0.92f), 3, true);
        DrawCircle(center + direction * (StickRadius - 27 * ControlScale), 27 * ControlScale, new Color(color, 0.68f));
        var font = ThemeDB.FallbackFont;
        int fontSize = Math.Max(8, (int)(18 * ControlScale));
        float available = StickRadius * 2 - 12 * ControlScale;
        while (fontSize > 8 && font.GetStringSize(caption, fontSize: fontSize).X > available) fontSize--;
        while (caption.Length > 1 && font.GetStringSize(caption, fontSize: fontSize).X > available) caption = caption[..^2] + "…";
        float textWidth = font.GetStringSize(caption, fontSize: fontSize).X;
        DrawString(font, center + new Vector2(-textWidth / 2, -StickRadius + 28 * ControlScale), caption, fontSize: fontSize, modulate: color);
    }
}
