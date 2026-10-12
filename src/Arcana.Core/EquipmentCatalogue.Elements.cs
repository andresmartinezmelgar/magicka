namespace Arcana.Core;

public sealed record EquipmentSetBonus(EquipmentStat Stat, float TwoPieces, float FourPieces);

public static partial class EquipmentCatalogue
{
    public static IReadOnlyList<EquipmentFamily> FocusedFamilies { get; } = Array.AsReadOnly(new[]
    { EquipmentFamily.Water, EquipmentFamily.Life, EquipmentFamily.Shield, EquipmentFamily.Cold,
        EquipmentFamily.Lightning, EquipmentFamily.Arcane, EquipmentFamily.Earth, EquipmentFamily.Fire });
    private static readonly EquipmentSetBonus[][] Sets =
    [
        [],
        [new(EquipmentStat.HealingPower, 0.05f, 0.15f), new(EquipmentStat.LifeEfficiency, 0, 0.10f)],
        [new(EquipmentStat.ShieldDuration, 0.10f, 0.20f), new(EquipmentStat.ShieldCapacity, 0, 0.20f)],
        [new(EquipmentStat.FirePower, 0.05f, 0.15f), new(EquipmentStat.BurnDuration, 0, 0.20f)],
        [new(EquipmentStat.WaterPower, 0.05f, 0.15f), new(EquipmentStat.WetDuration, 0, 0.20f)],
        [new(EquipmentStat.ColdPower, 0.05f, 0.15f), new(EquipmentStat.FreezeDuration, 0, 0.20f)],
        [new(EquipmentStat.LightningPower, 0.05f, 0.15f), new(EquipmentStat.WetLightningPower, 0, 0.20f)],
        [new(EquipmentStat.ArcanePower, 0.05f, 0.15f), new(EquipmentStat.BeamChargeSpeed, 0, 0.20f)],
        [new(EquipmentStat.EarthPower, 0.05f, 0.15f), new(EquipmentStat.RockChargeSpeed, 0, 0.20f)]
    ];
    public static IReadOnlyList<EquipmentSetBonus> SetBonuses(EquipmentFamily family) =>
        Enum.IsDefined(family) ? Sets[(int)family] : throw new ArgumentOutOfRangeException(nameof(family));
    public static float Maximum(EquipmentStat stat) => stat switch
    {
        EquipmentStat.MovementSpeed => 0.30f,
        EquipmentStat.SpellRange or EquipmentStat.Defense or EquipmentStat.LifeEfficiency or EquipmentStat.WetLightningPower => 0.35f,
        EquipmentStat.Power => 0.40f,
        EquipmentStat.FirePower or EquipmentStat.WaterPower or EquipmentStat.ColdPower or EquipmentStat.LightningPower or EquipmentStat.ArcanePower or EquipmentStat.EarthPower => 0.50f,
        _ => 0.60f
    };
}
