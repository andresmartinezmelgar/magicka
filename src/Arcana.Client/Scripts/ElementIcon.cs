using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Formas vectoriales distintas: la identificación no depende solo del color.</summary>
public partial class ElementIcon : Control
{
    private Element? _value;
    public Element? Value { get => _value; set { if (_value != value) { _value = value; QueueRedraw(); } } }
    public ElementIcon() { CustomMinimumSize = new(25, 25); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        if (_value == null) return;
        Vector2 center = Size / 2; float r = Math.Min(Size.X, Size.Y) * 0.43f;
        var color = Ui.ElementColor(_value.Value);
        Vector2 P(float x, float y) => center + new Vector2(x, y) * r;
        void Polygon(params Vector2[] points) => DrawColoredPolygon(points, color);
        switch (_value)
        {
            case Element.Water:
                Polygon(P(0, -1), P(0.62f, -0.12f), P(0.76f, 0.38f), P(0.52f, 0.84f), P(0, 1), P(-0.52f, 0.84f), P(-0.76f, 0.38f), P(-0.62f, -0.12f)); break;
            case Element.Life:
                DrawRect(new(P(-0.25f, -0.9f), new Vector2(0.5f, 1.8f) * r), color);
                DrawRect(new(P(-0.9f, -0.25f), new Vector2(1.8f, 0.5f) * r), color); break;
            case Element.Shield:
                Polygon(P(-0.8f, -0.8f), P(0.8f, -0.8f), P(0.7f, 0.25f), P(0, 1), P(-0.7f, 0.25f)); break;
            case Element.Cold:
                for (int i = 0; i < 6; i++)
                {
                    var direction = Vector2.FromAngle(i * Mathf.Tau / 6); var side = direction.Orthogonal();
                    DrawLine(center, center + direction * r, color, 2, true);
                    DrawLine(center + direction * r * 0.58f, center + (direction * 0.35f + side * 0.24f) * r, color, 1.5f, true);
                }
                break;
            case Element.Lightning:
                Polygon(P(0.2f, -1), P(-0.72f, 0.15f), P(-0.08f, 0.15f), P(-0.3f, 1), P(0.82f, -0.25f), P(0.13f, -0.25f)); break;
            case Element.Arcane:
                Polygon(P(0, -1), P(0.25f, -0.28f), P(1, 0), P(0.25f, 0.28f), P(0, 1), P(-0.25f, 0.28f), P(-1, 0), P(-0.25f, -0.28f)); break;
            case Element.Earth:
                Vector2 a = P(-0.7f, -0.6f), b = P(0.1f, -0.88f), c = P(0.78f, -0.32f),
                    d = P(0.9f, 0.55f), e = P(0.22f, 0.9f), f = P(-0.85f, 0.45f), ridge = P(-0.12f, -0.05f);
                DrawColoredPolygon([a, b, c, ridge], color.Lightened(0.36f));
                DrawColoredPolygon([a, ridge, e, f], color);
                DrawColoredPolygon([ridge, c, d, e], color.Darkened(0.35f));
                DrawPolyline([a, b, c, d, e, f, a], color.Darkened(0.78f), Math.Max(1, r * 0.10f), true);
                DrawPolyline([b, ridge, P(0.12f, 0.30f), P(-0.08f, 0.6f)], color.Darkened(0.72f), Math.Max(1, r * 0.10f), true);
                DrawLine(ridge, P(0.40f, 0.06f), color.Darkened(0.72f), Math.Max(1, r * 0.08f), true); break;
            case Element.Fire:
                Polygon(P(0.08f, -1), P(0.44f, -0.15f), P(0.72f, -0.48f), P(0.86f, 0.38f), P(0.48f, 0.88f), P(-0.4f, 0.9f), P(-0.83f, 0.38f), P(-0.35f, -0.48f), P(-0.28f, 0.08f)); break;
        }
    }
}
