namespace Arcana.Core;

/// <summary>Recibe medias de ventanas de 5 s. Reduce rápido y recupera una banda cada 15 s estables.</summary>
public sealed class DefensePerformanceBudget
{
    public const int MinimumLimit = 32, DefaultLimit = 96;
    private static readonly int[] Limits = [32, 48, 64, 96, 128, 160, DefenseRules.MaxMinions];
    private int _goodWindows;
    public int Limit { get; private set; } = DefaultLimit;

    public static int LimitForFps(float fps) => fps switch
    { < 18 => 32, < 25 => 48, < 32 => 64, < 40 => 96, < 48 => 128, < 56 => 160, _ => DefenseRules.MaxMinions };

    public bool Observe(float fps)
    {
        if (!float.IsFinite(fps) || fps is < 1 or > 240) return false;
        int target = LimitForFps(fps), previous = Limit;
        if (target < Limit) { Limit = target; _goodWindows = 0; }
        else if (target > Limit)
        {
            if (++_goodWindows >= 3)
            { Limit = Limits.First(n => n > Limit); _goodWindows = 0; }
        }
        else _goodWindows = 0;
        return Limit != previous;
    }
}
