namespace Arcana.Core;

/// <summary>Vector de dos dimensiones. El cliente representa Y como Z en el mundo 3D.</summary>
public readonly record struct V2(float X, float Y)
{
    public static V2 Zero => new(0, 0);
    [System.Text.Json.Serialization.JsonIgnore] public float LengthSquared => X * X + Y * Y;
    [System.Text.Json.Serialization.JsonIgnore] public float Length => MathF.Sqrt(LengthSquared);
    [System.Text.Json.Serialization.JsonIgnore] public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
    [System.Text.Json.Serialization.JsonIgnore] public V2 Normalized
    {
        get { float length = Length; return length > 0.0001f ? this / length : Zero; }
    }
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
    /// <summary>Intersección entre segmentos; para rayos frontales colineales devuelve el centro del solapamiento.</summary>
    public static bool SegmentMeeting(V2 a, V2 b, V2 c, V2 d, out V2 meeting)
    {
        meeting = V2.Zero;
        var r = b - a; var s = d - c;
        float cross = r.X * s.Y - r.Y * s.X;
        var offset = c - a;
        if (MathF.Abs(cross) < 0.0001f)
        {
            if (r.LengthSquared < 0.0001f || s.LengthSquared < 0.0001f ||
                MathF.Abs(offset.X * r.Y - offset.Y * r.X) > 0.001f || V2.Dot(r, s) >= 0) return false;
            float start = V2.Dot(c - a, r) / r.LengthSquared, end = V2.Dot(d - a, r) / r.LengthSquared;
            float low = Math.Max(0, Math.Min(start, end)), high = Math.Min(1, Math.Max(start, end));
            if (high - low < 0.0001f) return false;
            meeting = a + r * ((low + high) * 0.5f); return true;
        }
        float t = (offset.X * s.Y - offset.Y * s.X) / cross;
        float u = (offset.X * r.Y - offset.Y * r.X) / cross;
        if (t < 0 || t > 1 || u < 0 || u > 1) return false;
        meeting = a + r * t; return true;
    }

    public static float DistanceToSegment(V2 p, V2 a, V2 b)
    {
        var delta = b - a;
        float t = delta.LengthSquared > 0 ? Math.Clamp(V2.Dot(p - a, delta) / delta.LengthSquared, 0, 1) : 0;
        return V2.Distance(p, a + delta * t);
    }

    // Devuelve la primera intersección con un círculo, normalizada entre 0 y 1.
    public static float? SegmentCircle(V2 from, V2 to, V2 center, float radius)
    {
        if (OutsideBounds(from, to, center, center, radius)) return null;
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

    /// <summary>Primera colisión de un círculo en movimiento con un segmento y sus extremos.</summary>
    public static float? SegmentCapsule(V2 from, V2 to, V2 a, V2 b, float radius)
    {
        if (OutsideBounds(from, to, a, b, radius)) return null;
        if (DistanceToSegment(from, a, b) <= radius) return 0;
        float? best = SegmentCircle(from, to, a, radius);
        if (SegmentCircle(from, to, b, radius) is float end && (best == null || end < best)) best = end;
        var edge = b - a;
        if (edge.LengthSquared < 0.000001f) return best;
        var normal = edge.Normalized.Perpendicular;
        float start = V2.Dot(from - a, normal), speed = V2.Dot(to - from, normal);
        if (MathF.Abs(speed) < 0.000001f) return best;
        for (int side = -1; side <= 1; side += 2)
        {
            float t = (side * radius - start) / speed;
            if (t < 0 || t > 1 || best != null && t >= best) continue;
            float along = V2.Dot(V2.Lerp(from, to, t) - a, edge) / edge.LengthSquared;
            if (along >= 0 && along <= 1) best = t;
        }
        return best;
    }

    // Filtro conservador: la prueba exacta sigue resolviendo contactos y esquinas.
    // El margen evita descartar tangencias por el redondeo de coordenadas.
    private static bool OutsideBounds(V2 from, V2 to, V2 a, V2 b, float radius)
    {
        float padding = radius + 0.00001f;
        return radius >= 0 && (Math.Min(from.X, to.X) > Math.Max(a.X, b.X) + padding ||
            Math.Max(from.X, to.X) < Math.Min(a.X, b.X) - padding ||
            Math.Min(from.Y, to.Y) > Math.Max(a.Y, b.Y) + padding ||
            Math.Max(from.Y, to.Y) < Math.Min(a.Y, b.Y) - padding);
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
    public static bool Inside(V2 p) => MathF.Abs(p.X) < HalfWidth && MathF.Abs(p.Y) < HalfHeight;
    public static ArenaDefinition Get(ArenaMapId id) => ArenaMaps.Get(id);
}
