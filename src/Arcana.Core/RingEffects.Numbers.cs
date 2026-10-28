using System.Globalization;

namespace Arcana.Core;

public static partial class RingEffects
{
    public const float ShieldSeconds = 12, PulseRadius = 3, LightningRadius = 4, RegenerationPerLife = 38;
    public static float BaseShield(EquipmentFamily family, int level) =>
        (family is EquipmentFamily.Mixed or EquipmentFamily.Shield ? 420 : 180) * ElementCopies(level);
    public static float BaseAllyShield(int level) => 100 * ElementCopies(level);
    public static float BaseHealing(int level) => 60 * ElementCopies(level);
    public static float BaseRegeneration(EquipmentFamily family, int level) => family == EquipmentFamily.Life ? RegenerationPerLife * ElementCopies(level) : 0;
    public static float PulseScale(EquipmentFamily family, int level) => family == EquipmentFamily.Lightning ? .5f : .5f * ElementCopies(level);
    private static EquipmentModifiers ItemEquipment(float shieldBonus) => EquipmentModifiers.None with
        { ShieldCapacity = 1 + Math.Clamp(shieldBonus, -.25f, EquipmentCatalogue.Maximum(EquipmentStat.ShieldCapacity)) };

    /// <summary>Valores con el equipo, antes de rasgos de mago, reliquias y defensas/estados del objetivo.</summary>
    public static RingNumbers Numbers(EquipmentFamily family, int level, EquipmentModifiers? equipment = null)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        if (level is < 0 or > 10) throw new ArgumentOutOfRangeException(nameof(level));
        var gear = equipment ?? EquipmentModifiers.None;
        var element = ElementFor(family);
        float scale = PulseScale(family, level);
        float damage = element.HasValue ? SpellBalance.Damage(new SpellRecipe([element.Value])) * scale *
            gear.Power * gear.ElementPower(element.Value) : 0;
        float burn = family == EquipmentFamily.Fire ? SpellBalance.BurnPerSecond * gear.Power * gear.Fire * gear.BurnPotency : 0;
        return new(BaseShield(family, level) * gear.ShieldCapacity, ShieldSeconds * gear.ShieldDuration, Interval(level),
            BaseRegeneration(family, level) * gear.Healing, family == EquipmentFamily.Life ? BaseHealing(level) * gear.Healing : 0,
            damage, burn, burn > 0 ? SpellBalance.BurnDuration * gear.BurnDuration : 0,
            family == EquipmentFamily.Lightning ? ElementCopies(level) : 0,
            family == EquipmentFamily.Shield ? BaseAllyShield(level) * gear.ShieldCapacity : 0,
            family == EquipmentFamily.Lightning ? LightningRadius : PulseRadius,
            family == EquipmentFamily.Water ? SpellBalance.UtilityPressure * scale * gear.Power * gear.Water : 0,
            family == EquipmentFamily.Lightning ? damage * SpellBalance.WetLightning * gear.WetLightningPower : 0);
    }

    /// <summary>Proyección de las cifras públicas; no reconstruye ni consulta el inventario de la cuenta.</summary>
    public static EquipmentModifiers EquipmentFor(PublicEquipment view)
    {
        float Bonus(EquipmentStat stat) => view.Stats.Single(s => s.Stat == stat).Effective;
        return EquipmentModifiers.None with
        {
            ShieldCapacity = 1 + Bonus(EquipmentStat.ShieldCapacity), ShieldDuration = 1 + Bonus(EquipmentStat.ShieldDuration),
            Healing = 1 + Bonus(EquipmentStat.HealingPower), Power = 1 + Bonus(EquipmentStat.Power),
            Fire = 1 + Bonus(EquipmentStat.FirePower), Cold = 1 + Bonus(EquipmentStat.ColdPower),
            Water = 1 + Bonus(EquipmentStat.WaterPower), Lightning = 1 + Bonus(EquipmentStat.LightningPower),
            Earth = 1 + Bonus(EquipmentStat.EarthPower), Arcane = 1 + Bonus(EquipmentStat.ArcanePower),
            WetLightningPower = 1 + Bonus(EquipmentStat.WetLightningPower),
            BurnDuration = 1 + Bonus(EquipmentStat.BurnDuration),
            BurnPotency = view.Items.Any(i => ItemAffixes.Active(i.Affix, i.Slot)?.Kind == ItemAffixKind.DoubleBurn) ? 2 : 1
        };
    }

    private static string DescribeNumbers(EquipmentFamily family, int level, EquipmentModifiers equipment)
    {
        var n = Numbers(family, level, equipment);
        static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"Escudo: {N(n.Shield)} HP · duración {N(n.ShieldDuration)} s · recarga {N(n.Cooldown)} s.",
            $"Radio de la habilidad: {N(n.Radius)} m, con línea de visión."
        };
        if (family == EquipmentFamily.Life)
        {
            lines.Add($"Recuperas {N(n.Regeneration)} HP/s con el escudo de vida activo y con HP.");
            lines.Add($"Curación por pulso: {N(n.Healing)} HP por aliado ({N(n.MeanPulseHealing)} HP/s de media).");
            lines.Add($"Curación propia media máxima: {N(n.MeanSelfHealing)} HP/s si el escudo permanece activo.");
        }
        else if (family == EquipmentFamily.Shield)
            lines.Add($"Escudo por aliado: {N(n.AllyShield)} HP por activación.");
        else if (n.Damage > 0)
        {
            lines.Add($"Daño por pulso: {N(n.Damage)} HP por objetivo. Daño medio entre pulsos: {N(n.MeanDamage)} DMG/s.");
            if (n.BurnPerSecond > 0)
                lines.Add($"Quemadura: {N(n.BurnPerSecond)} DMG/s durante {N(n.BurnDuration)} s, incluida en la media.");
            if (family == EquipmentFamily.Lightning)
                lines.Add($"Alcanza hasta {n.Targets} objetivo{(n.Targets == 1 ? "" : "s")}, con el daño indicado por objetivo. Contra un objetivo mojado: {N(n.WetDamage)} HP de daño por descarga.");
        }
        else
        {
            if (n.ShieldPressure > 0) lines.Add($"Presión contra escudos: {N(n.ShieldPressure)} HP por activación; moja y empuja.");
        }
        lines.Add("Cifras con el equipo, antes de rasgos, reliquias y defensas o estados del objetivo.");
        return string.Join('\n', lines);
    }
}

public readonly record struct RingNumbers(float Shield, float ShieldDuration, float Cooldown,
    float Regeneration, float Healing, float Damage, float BurnPerSecond, float BurnDuration,
    int Targets, float AllyShield, float Radius, float ShieldPressure, float WetDamage)
{
    public float MeanPulseHealing => Healing / Cooldown;
    public float MeanSelfHealing => (Healing + Regeneration * Math.Min(ShieldDuration, Cooldown)) / Cooldown;
    public float MeanDamage => (Damage + BurnPerSecond * Math.Min(BurnDuration, Cooldown)) / Cooldown;
}
