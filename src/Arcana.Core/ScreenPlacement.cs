namespace Arcana.Core;

/// <summary>Rectángulos de pantalla sin dependencia del motor.</summary>
public readonly record struct ScreenRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
}

public static class ScreenPlacement
{
    public const float MinimumGap = 8;

    public static bool Separated(ScreenRect a, ScreenRect b, float gap = MinimumGap) =>
        a.Right + gap <= b.X + .01f || b.Right + gap <= a.X + .01f ||
        a.Bottom + gap <= b.Y + .01f || b.Bottom + gap <= a.Y + .01f;

    /// <summary>Busca el punto libre más cercano, también entre varios obstáculos y junto a los bordes.</summary>
    public static bool TryPlace(ScreenRect wanted, ScreenRect screen, IReadOnlyList<ScreenRect> occupied,
        out ScreenRect result, float gap = MinimumGap)
    {
        result = wanted;
        if (!float.IsFinite(wanted.X + wanted.Y + wanted.Width + wanted.Height + screen.X + screen.Y + screen.Width + screen.Height + gap) ||
            wanted.Width <= 0 || wanted.Height <= 0 || gap < 0) return false;
        float left = screen.X + gap, top = screen.Y + gap;
        float right = screen.Right - gap - wanted.Width, bottom = screen.Bottom - gap - wanted.Height;
        if (right < left || bottom < top) return false;
        var preferred = wanted with { X = Math.Clamp(wanted.X, left, right), Y = Math.Clamp(wanted.Y, top, bottom) };
        bool Free(ScreenRect candidate)
        {
            for (int i = 0; i < occupied.Count; i++) if (!Separated(candidate, occupied[i], gap)) return false;
            return true;
        }
        if (Free(preferred)) { result = preferred; return true; }
        // Un mínimo de distancia en el espacio libre está en la proyección deseada,
        // un borde de pantalla o un borde de obstáculo; no hace falta barrer píxeles.
        var xs = new List<float> { preferred.X, left, right };
        var ys = new List<float> { preferred.Y, top, bottom };
        foreach (var rect in occupied)
        {
            xs.Add(Math.Clamp(rect.X - wanted.Width - gap, left, right));
            xs.Add(Math.Clamp(rect.Right + gap, left, right));
            ys.Add(Math.Clamp(rect.Y - wanted.Height - gap, top, bottom));
            ys.Add(Math.Clamp(rect.Bottom + gap, top, bottom));
        }
        float best = float.PositiveInfinity;
        foreach (float x in xs.Distinct()) foreach (float y in ys.Distinct())
        {
            float distance = (x - wanted.X) * (x - wanted.X) + (y - wanted.Y) * (y - wanted.Y);
            if (distance >= best) continue;
            var candidate = wanted with { X = x, Y = y };
            if (!Free(candidate)) continue;
            best = distance; result = candidate;
        }
        return float.IsFinite(best);
    }
}
