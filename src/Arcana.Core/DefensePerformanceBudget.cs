namespace Arcana.Core;

/// <summary>Ventanas activas de 2 s; reserva margen sobre 30 FPS y recupera una banda tras 10 s estables.</summary>
public sealed class DefensePerformanceBudget
{
    public const int MinimumLimit = 16, DefaultLimit = 48;
    private static readonly int[] Limits = [16, 24, 32, 48, 64, 96, 128, 160, DefenseRules.MaxMinions];
    public DefensePerformanceBudget(int initial = DefaultLimit) => Limit = Limits.Contains(initial) ? initial : DefaultLimit;
    private int _goodWindows;
    public int Limit { get; private set; } = DefaultLimit;

    public static int LimitForFps(float fps) => fps switch
    { < 24 => 16, < 30 => 24, < 34 => 32, < 40 => 48, < 48 => 64, < 56 => 96, < 64 => 128, < 80 => 160, _ => DefenseRules.MaxMinions };

    public bool Observe(float fps, float frameTime95 = 0)
    {
        if (!float.IsFinite(fps) || fps is < 1 or > 240) return false;
        if (float.IsFinite(frameTime95) && frameTime95 is >= 1 and <= 1000) fps = Math.Min(fps, 1000 / frameTime95);
        int target = LimitForFps(fps), previous = Limit;
        if (target < Limit) { Limit = target; _goodWindows = 0; }
        else if (target > Limit)
        {
            if (++_goodWindows >= 5)
            { Limit = Limits.First(n => n > Limit); _goodWindows = 0; }
        }
        else _goodWindows = 0;
        return Limit != previous;
    }
}
