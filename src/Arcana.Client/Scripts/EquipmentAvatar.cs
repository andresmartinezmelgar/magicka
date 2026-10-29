using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>El sprite real del mago, con seis zonas accesibles por tacto, ratón y teclado.</summary>
public partial class EquipmentAvatar : Control
{
    private WizardSprites.Atlas _atlas = null!;
    private EquipmentSlot _selected;
    private EquipmentSlot? _hovered;
    private readonly Dictionary<EquipmentSlot, Button> _buttons = [];
    private readonly Dictionary<EquipmentSlot, Color> _colors = [];
    private static readonly EquipmentSlot[] Order = [EquipmentSlot.Hat, EquipmentSlot.Weapon,
        EquipmentSlot.Chest, EquipmentSlot.GoldRing, EquipmentSlot.Boots, EquipmentSlot.MagicRing];
    internal IReadOnlyDictionary<EquipmentSlot, Button> Buttons => _buttons;

    public void Build(WizardType type, Action<EquipmentSlot> choose)
    {
        Name = "EquipmentWizardAvatar"; MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        CustomMinimumSize = new(155, 164); TextureFilter = TextureFilterEnum.Linear;
        _atlas = WizardSprites.Load(type);
        VisibilityChanged += RefreshMoteProcessing;
        RefreshHaloAnimation();
        foreach (var slot in Order)
        {
            var current = slot;
            var button = Ui.Button("", () => choose(current));
            button.Name = "EquipmentSlot" + slot; button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.CustomMinimumSize = new(0, 44); button.AddThemeFontSizeOverride("font_size", 13);
            button.MouseEntered += () => { _hovered = current; QueueRedraw(); };
            button.MouseExited += () => { _hovered = null; QueueRedraw(); };
            button.TooltipText = "Elegir " + EquipmentCatalogue.SlotName(slot).ToLowerInvariant();
            AddChild(button); _buttons.Add(slot, button); _colors[slot] = Ui.Muted;
        }
        Resized += Arrange;
        Arrange();
    }

    internal void UpdateSlot(EquipmentSlot slot, EquipmentItem? item, bool selected, int? upgradeLevel = null)
    {
        if (selected) _selected = slot;
        var color = item == null ? Ui.Muted : EquipmentPanel.TierColor(item.Tier);
        _colors[slot] = color;
        if (item == null) _halos.Remove(slot);
        else _halos[slot] = (item.Tier, Math.Clamp(upgradeLevel ?? item.UpgradeLevel, 0, 10), item.Family);
        RefreshHaloAnimation();
        var button = _buttons[slot];
        string name = slot switch { EquipmentSlot.GoldRing => "Anillo de oro", EquipmentSlot.MagicRing => "Anillo mágico", _ => EquipmentCatalogue.SlotName(slot) };
        button.Text = slot == EquipmentSlot.GoldRing ? "Anillo\noro" : slot == EquipmentSlot.MagicRing ? "Anillo\nmágico" : name + "\n" + (item == null ? "Vacío" : EquipmentCatalogue.TierName(item.Tier));
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var style = Ui.Panel(selected ? new("203f5d") : new("0e2035"), 7, selected ? Ui.Gold : color.Darkened(.3f));
            style.ContentMarginLeft = style.ContentMarginRight = 4; style.ContentMarginTop = style.ContentMarginBottom = 3;
            button.AddThemeStyleboxOverride(state, style);
        }
        button.AddThemeColorOverride("font_color", selected ? Ui.Gold : color);
        button.TooltipText = name + (item == null ? " · sin equipar" : " · " + EquipmentCatalogue.ItemName(item)) + "\nSeleccionar objetos a la derecha.";
        QueueRedraw();
    }

    private void Arrange()
    {
        if (_portrait)
        {
            ArrangeAura();
            QueueRedraw();
            return;
        }
        float width = Size.X, height = Size.Y;
        float bw = Math.Max(65, Math.Min(132, width * .35f));
        float bh = height < 260 ? 44 : 54;
        for (int i = 0; i < Order.Length; i++)
        {
            var button = _buttons[Order[i]];
            button.Position = new(i % 2 == 0 ? 0 : width - bw, 8 + i / 2 * Math.Max(bh + 8, (height - bh - 16) / 2));
            button.Size = new(bw, bh);
            button.AddThemeFontSizeOverride("font_size", width < 260 ? 12 : 14);
        }
        ArrangeAura();
        QueueRedraw();
    }

    private Rect2 SpriteBounds()
    {
        float height = _portrait
            ? Math.Min(Size.Y * .78f, Size.X * .78f * _atlas.Spec.Height / _atlas.Spec.Width)
            : Math.Min(Size.Y * .88f, Size.X * 1.28f);
        float width = height * _atlas.Spec.Width / _atlas.Spec.Height;
        return new((Size.X - width) / 2, (Size.Y - height) / 2, width, height);
    }
    private Vector2 Target(EquipmentSlot slot)
    {
        var bounds = SpriteBounds();
        var point = slot switch
        {
            EquipmentSlot.Hat => new Vector2(.52f, .24f), EquipmentSlot.Chest => new Vector2(.52f, .65f),
            EquipmentSlot.Boots => new Vector2(.52f, .86f), EquipmentSlot.Weapon => new Vector2(.28f, .42f),
            EquipmentSlot.GoldRing => new Vector2(.32f, .62f), _ => new Vector2(.68f, .65f)
        };
        return bounds.Position + point * bounds.Size;
    }

    public override void _Draw()
    {
        if (_atlas == null || Size.X < 1 || Size.Y < 1) return;
        DrawCircle(Size * new Vector2(.5f, .88f), Size.X * .24f, new Color(.12f, .30f, .44f, .16f));
        DrawAuraParticles();
        var source = new Rect2(0, 0, _atlas.Spec.Width / 4f, _atlas.Spec.Height / 4f);
        DrawTextureRectRegion(_atlas.Texture, SpriteBounds(), source);
        if (_portrait) return;
        foreach (var slot in Order)
        {
            var target = Target(slot); var rect = _buttons[slot].GetRect();
            var start = new Vector2(rect.Position.X < Size.X / 2 ? rect.End.X : rect.Position.X, rect.GetCenter().Y);
            bool active = slot == _selected || slot == _hovered;
            Color color = active ? Ui.Gold : _colors[slot]; color.A = active ? .95f : .55f;
            DrawLine(start, target, color, active ? 2 : 1, true);
            DrawCircle(target, active ? 9 : 5, new Color(color, active ? .25f : .15f));
            DrawArc(target, active ? 10 : 6, 0, Mathf.Tau, 24, color, active ? 2 : 1, true);
        }
    }

    public override void _GuiInput(InputEvent input)
    {
        if (_portrait) return;
        Vector2? point = input switch { InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } m => m.Position,
            InputEventScreenTouch { Pressed: true } t => t.Position, _ => null };
        if (!point.HasValue) return;
        var slot = Order.MinBy(s => Target(s).DistanceSquaredTo(point.Value));
        if (Target(slot).DistanceTo(point.Value) > 24 || _buttons[slot].Disabled) return;
        _buttons[slot].EmitSignal(BaseButton.SignalName.Pressed); AcceptEvent();
    }
}
