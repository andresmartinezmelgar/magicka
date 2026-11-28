namespace Arcana.Core;

// Identificadores de las habilidades elegidas; no añaden especies al bestiario.
public enum DefenseSpecialAbility { None = 0, FrontalShield = 6, DeathExplosion = 10 }

public static partial class DefenseRules
{
    public const float SpecialSpawnChance = 0.08f;
    public const float SpecialSpawnGap = 30, SpecialSpawnGapVariation = 20, SpecialDeathGap = 15;
    public const float SpecialHealthMultiplier = 1.75f, SpecialDamageMultiplier = 1.2f;
    public const int SpecialExperienceMultiplier = 2, SpecialGoldMultiplier = 3, SpecialLootMultiplier = 3;
    public const int MaximumDropGold = 208 * SpecialGoldMultiplier;
    public const float SpecialShieldDuration = 3, SpecialShieldCycle = 9, SpecialShieldDamageMultiplier = 0.25f;
    // Cono frontal de 120 grados; los laterales y la espalda quedan expuestos.
    public const float SpecialShieldFrontDot = 0.5f;
    public const string SpecialExplosionKind = "special_death_explosion";
    public const float SpecialExplosionDelay = 1.4f, SpecialExplosionRadius = 4.5f, SpecialExplosionDamage = 140;
    public static int SpecialLimit(int difficulty) => difficulty < 10 ? 0 : Math.Min(MaxMinions, 1 + (difficulty - 10) / 5);
    public static string SpecialName(DefenseSpecialAbility ability) => ability switch
    {
        DefenseSpecialAbility.FrontalShield => "Escudo frontal",
        DefenseSpecialAbility.DeathExplosion => "Detonación final",
        _ => ""
    };
}
