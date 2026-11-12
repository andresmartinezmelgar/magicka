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
            $"Activación automática: cada {N(n.Cooldown)} s mientras lo llevas equipado. No necesitas pulsar ningún botón.",
            $"Escudo: {N(n.Shield)} puntos de protección · duración máxima {N(n.ShieldDuration)} s, salvo que se rompa antes.",
            $"Tipo de protección: {(family is EquipmentFamily.Mixed or EquipmentFamily.Shield ? "escudo básico" : "escudo de " + EquipmentCatalogue.FamilyName(family))}.",
            $"Alcance del efecto: hasta {N(n.Radius)} m desde ti; no atraviesa paredes."
        };
        if (family == EquipmentFamily.Life)
        {
            lines.Add($"Curación al activarse: tú y cada aliado al alcance recuperáis {N(n.Healing)} puntos de vida.");
            lines.Add($"Regeneración: recuperas {N(n.Regeneration)} puntos de vida por segundo mientras el escudo de Vida de este anillo siga activo y tenga puntos.");
            lines.Add($"Promedio de las activaciones: {N(n.MeanPulseHealing)} puntos de vida por segundo y persona. Promedio máximo para ti, sumando la regeneración: {N(n.MeanSelfHealing)} puntos de vida por segundo.");
        }
        else if (family == EquipmentFamily.Shield)
            lines.Add($"Al activarse: cada aliado al alcance recibe un escudo de {N(n.AllyShield)} puntos, con la misma duración máxima que el tuyo.");
        else if (n.Damage > 0)
        {
            if (family == EquipmentFamily.Lightning)
                lines.Add($"Al activarse: ataca hasta {n.Targets} enemigo{(n.Targets == 1 ? "" : "s distintos")}, empezando por el más cercano.");
            else
                lines.Add("Al activarse: " + ActionDescription(family));
            lines.Add($"Daño inmediato: {N(n.Damage)} puntos de daño por enemigo y activación.");
            if (n.BurnPerSecond > 0)
                lines.Add($"Quemadura adicional: {N(n.BurnPerSecond)} puntos de daño por segundo durante {N(n.BurnDuration)} s. Ese golpe no quema a un enemigo mojado.");
            if (family == EquipmentFamily.Lightning)
                lines.Add($"Contra un enemigo mojado: {N(n.WetDamage)} puntos de daño por descarga.");
            lines.Add($"Promedio de daño: {N(n.MeanDamage)} puntos de daño por segundo y enemigo, si recibe todas las activaciones" +
                (n.BurnPerSecond > 0 ? "; incluye la quemadura." : "."));
        }
        else
        {
            lines.Add("Al activarse: " + ActionDescription(family));
            if (n.ShieldPressure > 0)
                lines.Add($"Daño contra escudos: {N(n.ShieldPressure)} puntos por enemigo y activación; esta cifra no es daño a la vida.");
        }
        if (n.Healing > 0 || n.Damage > 0)
            lines.Add("Los promedios cuentan la espera entre activaciones; no son otra curación ni otro ataque continuo.");
        if (family == EquipmentFamily.Earth)
            lines.Add("Mientras dure la protección de Tierra, caminas un 22 % más despacio.");
        lines.Add("Valores con tu equipo. El tipo de mago, las reliquias y las defensas o estados del objetivo pueden cambiar el resultado.");
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
