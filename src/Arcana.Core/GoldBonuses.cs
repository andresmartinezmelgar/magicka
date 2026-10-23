namespace Arcana.Core;

/// <summary>Bonos sobre oro base; los restos se conservan sin multiplicar ventas ni otros bonos.</summary>
public static class GoldBonuses
{
    // Múltiplo común de 1..11: conserva fracciones exactas incluso al mejorar o cambiar de pieza.
    public const int CoinFractionScale = 27720;
    public static int EveryForLevel(int level) => level is >= 0 and <= 10 ? 11 - level
        : throw new ArgumentOutOfRangeException(nameof(level));
    public static bool Valid(int extraEvery, int percent) => extraEvery is >= 0 and <= 11 && percent is 0 or 10 or 20;

    public static int Apply(int baseGold, int extraEvery, int percent, ref int coinRemainder, ref int percentRemainder)
    {
        if (baseGold is < 0 or > 100000000 || !Valid(extraEvery, percent) ||
            coinRemainder is < 0 or >= CoinFractionScale || percentRemainder is < 0 or > 99)
            throw new ArgumentException("Bonificación de oro inválida.");
        long coins = (extraEvery > 0 ? (long)baseGold * (CoinFractionScale / extraEvery) : 0) + coinRemainder;
        long percentage = (long)baseGold * percent + percentRemainder;
        int total = checked(baseGold + (int)(coins / CoinFractionScale) + (int)(percentage / 100));
        coinRemainder = (int)(coins % CoinFractionScale);
        percentRemainder = (int)(percentage % 100);
        return total;
    }
}
