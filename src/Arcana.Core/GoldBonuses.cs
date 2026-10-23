namespace Arcana.Core;

/// <summary>Bonos sobre oro base; los restos se conservan sin multiplicar ventas ni otros bonos.</summary>
public static class GoldBonuses
{
    public static bool Valid(int extraPerTwo, int percent) => extraPerTwo is 0 or 1 && percent is 0 or 10 or 20;

    public static int Apply(int baseGold, int extraPerTwo, int percent, ref int coinRemainder, ref int percentRemainder)
    {
        if (baseGold is < 0 or > 100000000 || !Valid(extraPerTwo, percent) ||
            coinRemainder is < 0 or > 1 || percentRemainder is < 0 or > 99)
            throw new ArgumentException("Bonificación de oro inválida.");
        long coins = (long)baseGold * extraPerTwo + coinRemainder;
        long percentage = (long)baseGold * percent + percentRemainder;
        int total = checked(baseGold + (int)(coins / 2) + (int)(percentage / 100));
        coinRemainder = (int)(coins % 2);
        percentRemainder = (int)(percentage % 100);
        return total;
    }
}
