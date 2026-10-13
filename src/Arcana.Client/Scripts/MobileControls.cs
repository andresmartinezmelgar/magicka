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
    private int _moveFinger = -1, _aimFinger = -1;
    private Vector2 _move, _aim = Vector2.Right, _aimKnob;
    private Vector2 _moveCenter, _moveHome, _aimCenter;
    private readonly ElementBuffer _selected = new();
    private bool _awaitingSelectionEcho, _directionalAimPending;
    private SpellShape _aimedSpellShape;
    private int _selectionEchoFrames;
    private const float Radius = 82;
    internal bool BarrierAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Barrier;
    internal bool IceAimPending => _directionalAimPending && _aimedSpellShape == SpellShape.Shards;
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
        AddButton("Vaciar", new() { Type = "clear" });
        AddButton("Bastón", new() { Type = "melee" });
        AddButton("Sobre ti", new() { Type = "self" }, color: Ui.Health);
        AddButton("Área", new() { Type = "nova" }, color: Ui.Gold);
        for (int i = 0; i < SpellBalance.Magicks.Count; i++)
            _magicks.Add(AddButton(SpellBalance.Magicks[i].Name, new() { Type = "magick", Slot = i }, color: Ui.Gold));
        AddButton("Ayuda", run: () => HelpRequested?.Invoke());
        AddButton("Tabla", run: () => ScoreRequested?.Invoke());
        AddButton("Menú", run: () => MenuRequested?.Invoke());
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
        _moveHome = new(142, height - 125);
        if (_moveFinger < 0) _moveCenter = _moveHome;
        _aimCenter = new(width - 142, height - 125);
        for (int i = 0; i < 8; i++) Place(i, new(width - 246 + i % 2 * 116, height - 572 + i / 2 * 80), new(108, 72));
        Place(8, new(251, height - 148), new(118, 62));
        Place(9, new(251, height - 80), new(118, 62));
        Place(10, new(width - 369, height - 148), new(118, 62));
        Place(11, new(width - 369, height - 80), new(118, 62));
        for (int i = 0; i < 4; i++) Place(12 + i, new(width / 2 - 244 + i * 124, height - 90), new(120, 72));
        for (int i = 0; i < 3; i++) Place(16 + i, new(width - 382 + i * 124, 22), new(118, 62));
        QueueRedraw();
    }
    private void Place(int index, Vector2 position, Vector2 size)
    { _actions[index].Panel.Position = position; _actions[index].Panel.Size = size; }
    public void Reset()
    {
        _moveFinger = _aimFinger = -1; _move = _aimKnob = Vector2.Zero; _moveCenter = _moveHome;
        _directionalAimPending = false;
        _awaitingSelectionEcho = false;
        foreach (var action in _pressed.Values) action.Panel.Modulate = Colors.White;
        _pressed.Clear(); QueueRedraw();
    }
    public void Update(WizardSnapshot? player)
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
        for (int i = 0; i < _magicks.Count; i++)
        {
            var magick = SpellBalance.Magicks[i];
            float cost = i == 3 ? player.ReanimationCost : magick.Cost;
            _magicks[i].Text.Text = magick.Name + "\n" + (player.Cooldowns[i] > 0 ? $"{Math.Ceiling(player.Cooldowns[i])}s" : $"{cost:0}");
            _magicks[i].Text.AddThemeColorOverride("font_color", player.Cooldowns[i] > 0 || player.Energy < cost ? Ui.Muted : Ui.Gold);
        }
    }
    public override void _Input(InputEvent input)
    {
        if (!IsVisibleInTree()) return;
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
                bool castOnRelease = _directionalAimPending && !canceled;
                _directionalAimPending = false; QueueRedraw();
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
        if (_moveFinger < 0 && new Rect2(Vector2.Zero, new(Size.X / 2, Size.Y)).HasPoint(point))
        { _moveFinger = finger; _moveCenter = point; _move = Vector2.Zero; QueueRedraw(); return true; }
        if (_aimFinger < 0 && point.DistanceTo(_aimCenter) <= Radius + 20)
        {
            _aimFinger = finger;
            _aimedSpellShape = new SpellRecipe(_selected.Elements).Shape;
            _directionalAimPending = _aimedSpellShape is SpellShape.Barrier or SpellShape.Shards;
            Aim(point); AimChanged?.Invoke();
            if (!_directionalAimPending) Request(new() { Type = "cast" });
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
    private static Vector2 Stick(Vector2 offset)
    {
        if (offset.Length() < 12) return Vector2.Zero;
        return offset.LimitLength(Radius) / Radius;
    }
    private void Move(Vector2 point) { _move = Stick(point - _moveCenter); QueueRedraw(); }
    private void Aim(Vector2 point)
    { _aimKnob = Stick(point - _aimCenter); if (!_aimKnob.IsZeroApprox()) _aim = _aimKnob.Normalized(); QueueRedraw(); }
    public override void _Draw()
    {
        DrawStick(_moveCenter, _move, "MOVER", Ui.Blue);
        DrawStick(_aimCenter, _aimKnob, _directionalAimPending
            ? _aimedSpellShape == SpellShape.Shards ? "SUELTA: HIELO" : "SUELTA: ESCUDO"
            : "APUNTAR / LANZAR", Ui.Gold);
    }
    private void DrawStick(Vector2 center, Vector2 direction, string caption, Color color)
    {
        DrawCircle(center, Radius, new Color(0.035f, 0.055f, 0.09f, 0.86f));
        DrawArc(center, Radius, 0, Mathf.Tau, 48, new Color(color, 0.92f), 3, true);
        DrawCircle(center + direction * (Radius - 27), 27, new Color(color, 0.68f));
        var font = ThemeDB.FallbackFont;
        float textWidth = font.GetStringSize(caption, fontSize: 19).X;
        DrawString(font, center + new Vector2(-textWidth / 2, -Radius - 16), caption, fontSize: 19, modulate: color);
    }
}
