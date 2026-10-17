using Godot;

namespace Arcana.Client;

/// <summary>Referencia de apuntado sobre el suelo; no representa un área de daño.</summary>
public partial class AimMarker : Control
{
    private readonly Vector2[] _ring = new Vector2[41];
    private Vector2? _from;
    private Color _color;
    internal Vector2 Target { get; private set; }

    public override void _Ready()
    {
        Name = "AimMarker"; MouseFilter = MouseFilterEnum.Ignore;
        Ui.Full(this); Hide();
    }

    public void ShowTarget(Vector2 center, Vector2 axisX, Vector2 axisY, Color color, Vector2? from = null)
    {
        Target = center - GlobalPosition; _from = from - GlobalPosition; _color = color;
        for (int i = 0; i < _ring.Length; i++)
        {
            float angle = Mathf.Tau * i / (_ring.Length - 1);
            _ring[i] = Target + axisX * Mathf.Cos(angle) + axisY * Mathf.Sin(angle);
        }
        Show(); QueueRedraw();
    }

    public override void _Draw()
    {
        var outline = new Color(0.015f, 0.025f, 0.04f, 0.9f);
        if (_from is { } origin)
        {
            DrawLine(origin, Target, outline, 4, true);
            DrawLine(origin, Target, new Color(_color, 0.7f), 1.5f, true);
        }
        DrawPolyline(_ring, outline, 5, true);
        DrawPolyline(_ring, _color, 2, true);
        DrawCrossLine(Vector2.Right * 7, outline);
        DrawCrossLine(Vector2.Down * 7, outline);
    }

    private void DrawCrossLine(Vector2 axis, Color outline)
    {
        DrawLine(Target - axis, Target + axis, outline, 5, true);
        DrawLine(Target - axis, Target + axis, _color, 2, true);
    }
}
