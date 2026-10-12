using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPreview : Control
{
    public EquipmentAppearance Appearance { get; set; } = new();
    public override void _Draw()
    {
        var center = new Vector2(Size.X * 0.5f, 55);
        Color Color(ItemTier? tier) => tier is { } t ? EquipmentPanel.TierColor(t) : new Color("596477");
        DrawCircle(center + new Vector2(0, -17), 12, new("e3c4a1"));
        DrawColoredPolygon([center + new Vector2(-21, -26), center + new Vector2(0, -53), center + new Vector2(21, -26)], Color(Appearance.Hat));
        DrawColoredPolygon([center + new Vector2(-12, -4), center + new Vector2(12, -4), center + new Vector2(24, 35), center + new Vector2(-24, 35)], Color(Appearance.Chest));
        DrawRect(new(center + new Vector2(-20, 34), new Vector2(16, 9)), Color(Appearance.Boots));
        DrawRect(new(center + new Vector2(4, 34), new Vector2(16, 9)), Color(Appearance.Boots));
        DrawLine(center + new Vector2(37, -37), center + new Vector2(37, 39), Color(Appearance.Weapon), 5);
        var family = Appearance.Family switch { EquipmentFamily.Life => Ui.ElementColor(Element.Life), EquipmentFamily.Shield => Ui.Shield, EquipmentFamily.Fire => Ui.ElementColor(Element.Fire), _ => Ui.Blue };
        DrawCircle(center + new Vector2(37, -40), 8, family);
    }
}
