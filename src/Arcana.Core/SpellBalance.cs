using System.Globalization;

namespace Arcana.Core;

/// <summary>Valores compartidos por el combate y las cifras de F1. El catálogo indica daño base contra un mago seco.</summary>
public static class SpellBalance
{
    public const float Arcane = 115, Earth = 100, Lightning = 100, Fire = 58, Cold = 24, Ice = 68, Steam = 160;
    public const float WetLightning = 1.9f, BurnPerSecond = 28, BurnDuration = 4;
    public const float Heal = 140, SelfHeal = 145, MineHeal = 252;
    public const float UtilityPressure = 35;
    public const float RockMinimum = 1.1f, ChargeSeconds = 1.4f, ShardPower = 0.6f, MinePower = 1.8f, GroundPower = 0.4f;
    public const float BeamMinimumDamage = 0.3f, BeamMaximumDamage = 1.5f, BeamChargeSeconds = 2;
    public const float MeteorDelay = 1.4f, MeteorLifetime = 1.5f, MeteorRadius = 4, MeteorPower = 2.7f;
    public static readonly Element[] Offensive = [Element.Arcane, Element.Earth, Element.Lightning, Element.Fire, Element.Cold, Element.Water];
    public static IReadOnlyList<MagickDefinition> Magicks { get; } = Array.AsReadOnly<MagickDefinition>(
        [new("Celeridad", 25, 9), new("Traslación", 35, 12), new("Meteoro", 75, 24), new("Reanimar", 60, 22)]);
    public static float Component(SpellRecipe spell, Element element, bool wet = false) => element switch
    {
        Element.Arcane => Arcane * spell.Count(element),
        Element.Earth => Earth * spell.Count(element),
        Element.Lightning => Lightning * spell.Count(element) * (wet ? WetLightning : 1),
        Element.Fire => (spell.Steam ? Steam * 0.5f : Fire) * spell.Count(element),
        Element.Cold => (spell.Ice ? Ice * 0.5f : Cold) * spell.Count(element),
        Element.Water => (spell.Steam ? Steam * 0.5f : spell.Ice ? Ice * 0.5f : 0) * spell.Count(element),
        _ => 0
    };
    // Los elementos de estado o curación también ejercen presión sobre una barrera, sin convertirse en daño de vida.
    public static float ProtectionWeight(SpellRecipe spell, Element element)
    {
        if (element == Element.Shield && spell.Elements.Length > 1) return 0;
        float damage = Component(spell, element);
        return damage > 0 ? damage : UtilityPressure * spell.Count(element);
    }
    public static float Damage(SpellRecipe spell, bool wet = false)
    {
        double damage = 0; // Sum(float) acumula en doble precisión.
        foreach (var element in Offensive) damage += Component(spell, element, wet);
        return (float)damage;
    }
    public static float BeamDamageMultiplier(float seconds) => BeamMinimumDamage +
        (BeamMaximumDamage - BeamMinimumDamage) * Math.Clamp(seconds / BeamChargeSeconds, 0, 1);
    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static SpellNumbers Numbers(SpellRecipe spell, CastMode mode)
    {
        float damage = Damage(spell), wet = Damage(spell, true), healing = Heal * spell.Count(Element.Life);
        float min = 1, max = 1, healMin = 1, healMax = 1;
        string unit = "por impacto", healingUnit = "por impacto", extra = "";
        bool burns = spell.Has(Element.Fire) && !spell.Steam;
        if (mode == CastMode.Self)
        {
            if (spell.Has(Element.Shield))
            {
                damage = wet = 0; healing = 38 * spell.Count(Element.Life); healingUnit = "/s"; burns = false;
                extra = "Protección: " + (spell.Has(Element.Earth) ? "650" : spell.Elements.Length == 1 ? "420" : "180") + " de escudo; 12 s.";
                extra += " Ventajas y debilidades según sus elementos: consulta la tabla Elementos de la ayuda.";
            }
            else if (spell.Healing) { damage = wet = 0; healing = SelfHeal * spell.Count(Element.Life); burns = false; }
            else if (spell.Has(Element.Water)) { damage = wet = 0; burns = false; }
            else if (spell.Has(Element.Fire)) extra = "Sobre ti, mojado o con frío: limpia el estado en vez de causar daño.";
        }
        else if (spell.Has(Element.Shield))
        {
            if (spell.Healing) { damage = wet = 0; healing = MineHeal * spell.Count(Element.Life); unit = healingUnit = "por mina"; burns = false; }
            else if (spell.Has(Element.Arcane)) { min = max = MinePower; unit = "por mina"; }
            else { unit = "daño/s al contacto"; if (!spell.Has(Element.Fire) && !spell.Has(Element.Cold) && !spell.Has(Element.Lightning)) damage = wet = 0; }
            if (mode == CastMode.Nova) extra = "Crea seis minas o barreras; cada cifra corresponde a una.";
        }
        else if (mode == CastMode.Forward)
        {
            if (spell.Shape == SpellShape.Rock)
            {
                min = RockMinimum; max = RockMinimum + ChargeSeconds;
                healMin = min; healMax = max;
                extra = "La carga modifica el impacto.";
                if (spell.SplashRadius > 0) extra += " El splash causa hasta el 65 % y disminuye con la distancia.";
                if (spell.GroundKind != null)
                {
                    var ground = new SpellRecipe(spell.Elements.Where(e => e != Element.Earth));
                    extra += " Suelo: " + N(Damage(ground) * GroundPower) + " daño/s durante 4 s.";
                }
            }
            else if (spell.Shape == SpellShape.Shards)
            {
                min = max = healMin = healMax = ShardPower; unit = healingUnit = "por fragmento";
                extra = (3 + spell.Count(Element.Cold)) + " fragmentos; el total depende de cuántos impacten.";
            }
            else
            {
                unit = "daño/s"; healingUnit = "/s";
                if (spell.Shape == SpellShape.Beam && damage > 0)
                {
                    min = BeamMinimumDamage; max = BeamMaximumDamage;
                    extra = "Carga por tiempo: " + N(BeamMinimumDamage * 100) + " % al iniciar; 100 % de carga en " + N(BeamChargeSeconds) +
                        " s = " + N(BeamMaximumDamage * 100) + " % del daño original. Soltar o interrumpir reinicia la carga. Curación y estados no aumentan.";
                }
            }
        }
        if (spell.Steam || spell.Ice) extra += (extra.Length > 0 ? " " : "") +
            "La fusión reparte su daño entre Agua y " + (spell.Steam ? "Fuego" : "Frío") + "; el tercer elemento conserva su aporte.";
        return new(damage * min, damage * max, unit, healing * healMin, healing * healMax, healingUnit,
            burns ? BurnPerSecond : 0, wet * min, wet * max, spell.Has(Element.Lightning), extra);
    }
    public static string Format(SpellRecipe spell, CastMode mode)
    {
        var n = Numbers(spell, mode);
        string Range(float a, float b) => N(a) + (a == b ? "" : "–" + N(b));
        string result = "DAÑO BASE: " + Range(n.Minimum, n.Maximum) + " " + n.Unit;
        if (n.HealingMaximum > 0) result += " · CURACIÓN: " + Range(n.HealingMinimum, n.HealingMaximum) + " " + n.HealingUnit;
        if (n.BurnPerSecond > 0) result += "\nQuemadura adicional: " + N(n.BurnPerSecond) + " daño/s, hasta " + N(BurnDuration) + " s tras el último golpe.";
        if (n.Lightning) result += "\nContra mojado: " + Range(n.WetMinimum, n.WetMaximum) + " " + n.Unit + ".";
        if (n.Extra.Length > 0) result += "\n" + n.Extra;
        return result;
    }
}

public sealed record MagickDefinition(string Name, float Cost, float Cooldown);

public sealed record SpellNumbers(float Minimum, float Maximum, string Unit, float HealingMinimum, float HealingMaximum,
    string HealingUnit, float BurnPerSecond, float WetMinimum, float WetMaximum, bool Lightning, string Extra);
