namespace Arcana.Core;

/// <summary>Vector de dos dimensiones. El cliente representa Y como Z en el mundo 3D.</summary>
public readonly record struct V2(float X, float Y)
{
    public static V2 Zero => new(0, 0);
    [System.Text.Json.Serialization.JsonIgnore] public float LengthSquared => X * X + Y * Y;
    [System.Text.Json.Serialization.JsonIgnore] public float Length => MathF.Sqrt(LengthSquared);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
    [System.Text.Json.Serialization.JsonIgnore] public V2 Normalized => Length > 0.0001f ? this / Length : Zero;
    [System.Text.Json.Serialization.JsonIgnore] public V2 Perpendicular => new(-Y, X);
    public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
    public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
    public static V2 operator *(V2 a, float b) => new(a.X * b, a.Y * b);
    public static V2 operator /(V2 a, float b) => new(a.X / b, a.Y / b);
    public static float Dot(V2 a, V2 b) => a.X * b.X + a.Y * b.Y;
    public static float Distance(V2 a, V2 b) => (a - b).Length;
    public static V2 Lerp(V2 a, V2 b, float t) => a + (b - a) * t;
    public V2 Rotated(float a) => new(X * MathF.Cos(a) - Y * MathF.Sin(a),
                                     X * MathF.Sin(a) + Y * MathF.Cos(a));
}

public static class Geometry
{
    public static float DistanceToSegment(V2 p, V2 a, V2 b)
    {
        var delta = b - a;
        float t = delta.LengthSquared > 0 ? Math.Clamp(V2.Dot(p - a, delta) / delta.LengthSquared, 0, 1) : 0;
        return V2.Distance(p, a + delta * t);
    }

    // Devuelve la primera intersección con un círculo, normalizada entre 0 y 1.
    public static float? SegmentCircle(V2 from, V2 to, V2 center, float radius)
    {
        var d = to - from;
        var f = from - center;
        if (f.LengthSquared <= radius * radius) return 0;
        float a = d.LengthSquared;
        if (a < 0.000001f) return null;
        float b = 2 * V2.Dot(f, d);
        float c = f.LengthSquared - radius * radius;
        float discriminant = b * b - 4 * a * c;
        if (discriminant < 0) return null;
        float t = (-b - MathF.Sqrt(discriminant)) / (2 * a);
        return t >= 0 && t <= 1 ? t : null;
    }
}

public sealed record Obstacle(V2 Position, float Radius, float Height);
public static class ArenaMap
{
    public const float HalfWidth = 22;
    public const float HalfHeight = 16;
    public const float WizardRadius = 0.48f;
    public static readonly Obstacle[] Obstacles =
    [
        new(new(-6, -5), 1.1f, 2.6f), new(new(6, 5), 1.1f, 2.6f),
        new(new(-6, 5), 1.1f, 2.6f), new(new(6, -5), 1.1f, 2.6f),
        new(new(-13, 0), 1.3f, 1.7f), new(new(13, 0), 1.3f, 1.7f),
        new(new(0, -10), 1.4f, 2.1f), new(new(0, 10), 1.4f, 2.1f)
    ];
    public static readonly V2[] ControlPoints = [new(-9, -7), new(0, 0), new(9, 7)];
    public static V2 Spawn(int team, int index) => new(team == 0 ? -18 : 18, (index % 4 - 1.5f) * 2.2f);
    public static bool Inside(V2 p) => MathF.Abs(p.X) < HalfWidth && MathF.Abs(p.Y) < HalfHeight;
}
