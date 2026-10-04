using System.Globalization;

namespace Arcana.Core;

/// <summary>Valores compartidos por el combate y las cifras de F1. El catálogo indica daño base contra un mago seco.</summary>
public static class SpellBalance
{
    public const float Arcane = 115, Earth = 100, Lightning = 100, Fire = 58, Cold = 24, Ice = 68, Steam = 160;
    public const float WetLightning = 1.9f, BurnPerSecond = 28, BurnDuration = 4;
    public const float Heal = 140, SelfHeal = 145, MineHeal = 252;
    public const float RockMinimum = 1.1f, ChargeSeconds = 1.4f, ShardPower = 0.6f, MinePower = 1.8f, GroundPower = 0.4f;
    public static readonly Element[] Offensive = [Element.Arcane, Element.Earth, Element.Lightning, Element.Fire, Element.Cold];
    public static float Component(SpellRecipe spell, Element element, bool wet = false) => element switch
    {
        Element.Arcane => Arcane * spell.Count(element),
        Element.Earth => Earth * spell.Count(element),
        Element.Lightning => Lightning * spell.Count(element) * (wet ? WetLightning : 1),
        Element.Fire => spell.Steam ? Steam : Fire * spell.Count(element),
        Element.Cold => (spell.Ice ? Ice : Cold) * spell.Count(element),
        _ => 0
    };
    public static float Damage(SpellRecipe spell, bool wet = false) => Offensive.Sum(e => Component(spell, e, wet));
    private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    public static SpellNumbers Numbers(SpellRecipe spell, CastMode mode)
    {
        float damage = Damage(spell), wet = Damage(spell, true), healing = Heal * spell.Count(Element.Life);
        float min = 1, max = 1; string unit = "por impacto", healingUnit = "por impacto", extra = "";
        bool burns = spell.Has(Element.Fire) && !spell.Steam;
        if (mode == CastMode.Self)
        {
            if (spell.Has(Element.Shield))
            {
                damage = wet = 0; healing = 38 * spell.Count(Element.Life); healingUnit = "/s"; burns = false;
                extra = "Protección: " + (spell.Has(Element.Earth) ? "650" : spell.Elements.Length == 1 ? "420" : "180") + " de escudo; 12 s.";
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
                min = max = ShardPower; unit = healingUnit = "por fragmento";
                extra = (3 + spell.Count(Element.Cold)) + " fragmentos; el total depende de cuántos impacten.";
            }
            else { unit = "daño/s"; healingUnit = "/s"; }
        }
        return new(damage * min, damage * max, unit, healing * min, healing * max, healingUnit,
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

public sealed record SpellNumbers(float Minimum, float Maximum, string Unit, float HealingMinimum, float HealingMaximum,
    string HealingUnit, float BurnPerSecond, float WetMinimum, float WetMaximum, bool Lightning, string Extra);
