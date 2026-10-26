namespace Arcana.Core;

/// <summary>Valores compartidos por el combate y las descripciones; las copias usan el rango mayor.</summary>
public static class CombatAffixes
{
    public const float ReprisalRadius = 3, ReprisalSeconds = 6;
    private static int RankValue(int rank, int diamond, int mythic) => rank >= 2 ? mythic : rank == 1 ? diamond : 0;
    public static int IcebreakerPercent(int rank) => RankValue(rank, 25, 40);
    public static int SteamImpactPercent(int rank) => RankValue(rank, 20, 35);
    public static int ArcaneExecutionPercent(int rank) => RankValue(rank, 20, 35);
    public static float BatteryPerHundred(int rank) => RankValue(rank, 15, 30) / 10f;
    public static int MendingPercent(int rank) => RankValue(rank, 10, 15);
    public static int ReprisalForce(int rank) => RankValue(rank, 10, 14);
    public static float DamageMultiplier(EquipmentModifiers gear, Element element, bool wet, bool frozen, float healthFraction) =>
        1 + (element switch
        {
            Element.Earth when frozen => IcebreakerPercent(gear.Icebreaker),
            Element.Fire when wet => SteamImpactPercent(gear.SteamImpact),
            Element.Arcane when healthFraction <= .30f => ArcaneExecutionPercent(gear.ArcaneExecution),
            _ => 0
        }) / 100f;
}
