namespace Arcana.Core;

public sealed record TouchPosition(float X, float Y);
public sealed record TouchSettings
{
    public float Size { get; init; } = 1;
    public float Spacing { get; init; } = 1;
    public float DeadZone { get; init; } = 12f / 82;
    public float Sensitivity { get; init; } = 1;
    public bool LeftHanded { get; init; }
    public Dictionary<string, TouchPosition> Positions { get; init; } = new(StringComparer.Ordinal);

    public TouchSettings Normalize() => this with
    {
        Size = Limit(Size, .75f, 1.3f, 1), Spacing = Limit(Spacing, .8f, 1.25f, 1),
        DeadZone = Limit(DeadZone, .05f, .35f, 12f / 82), Sensitivity = Limit(Sensitivity, .6f, 1.8f, 1),
        Positions = (Positions ?? []).Where(p => p.Key.Length < 64 && p.Value != null && float.IsFinite(p.Value.X) && float.IsFinite(p.Value.Y))
            .ToDictionary(p => p.Key, p => new TouchPosition(Math.Clamp(p.Value.X, 0, 1), Math.Clamp(p.Value.Y, 0, 1)), StringComparer.Ordinal)
    };
    private static float Limit(float value, float min, float max, float fallback) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
