using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Calidad por forma y color; diez segmentos para las mejoras, sin rótulos.</summary>
internal partial class EquipmentItemIndicators : Control
{
    private ItemTier? _tier;
    private int _level;
    private bool _affordable;
    internal void Update(ItemTier? tier, int level, bool affordable)
    { _tier = tier; _level = level; _affordable = affordable; QueueRedraw(); }
    public override void _Draw()
    {
        if (_tier is not { } tier) return;
        Color color = EquipmentPanel.TierColor(tier);
        var center = new Vector2(14, 40);
        Vector2 P(float x, float y) => center + new Vector2(x, y) * 8;
        DrawCircle(center, 9, new Color("0b192b"));
        switch (tier)
        {
            case ItemTier.Silver:
                DrawArc(center, 7, 0, Mathf.Tau, 20, color, 2, true); DrawLine(P(-.5f, 0), P(.5f, 0), color, 2, true); break;
            case ItemTier.Gold:
                var star = Enumerable.Range(0, 10).Select(i => center + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * (i % 2 == 0 ? 8 : 3.5f)).ToArray();
                DrawColoredPolygon(star, color); break;
            case ItemTier.Platinum:
                DrawColoredPolygon([P(-1, 0), P(-.4f, -.8f), P(.2f, 0), P(-.4f, .8f)], color);
                DrawColoredPolygon([P(-.2f, 0), P(.4f, -.8f), P(1, 0), P(.4f, .8f)], color); break;
            case ItemTier.Diamond:
                DrawColoredPolygon([P(0, -1), P(.8f, 0), P(0, 1), P(-.8f, 0)], color);
                DrawLine(P(-.5f, 0), P(.5f, 0), Colors.White, 1, true); break;
            case ItemTier.Mythic:
                DrawColoredPolygon([P(-1, .5f), P(-1, -.6f), P(-.4f, 0), P(0, -1), P(.4f, 0), P(1, -.6f), P(1, .5f)], color);
                DrawLine(P(-.8f, .9f), P(.8f, .9f), color, 2, true); break;
        }
        for (int i = 0; i < DefenseRules.ItemUpgradeLimit; i++)
            DrawRect(new(new Vector2(6 + i * 5.2f, 50), new Vector2(4, 3)), i < _level ? color : new Color("3a4c63"));
        if (_affordable)
        {
            DrawLine(new(31, 44), new(31, 32), Ui.Gold, 2, true);
            DrawPolyline([new(26, 37), new(31, 32), new(36, 37)], Ui.Gold, 2, true);
        }
    }
}
