namespace Arcana.Core;

/// <summary>Los dos anillos ocupan posiciones propias; sus efectos no dependen de un afijo raro.</summary>
public static partial class RingEffects
{
    public static bool IsRing(EquipmentSlot slot) => slot is EquipmentSlot.GoldRing or EquipmentSlot.MagicRing;
    public static float Interval(int level) => 16 - Math.Clamp(level, 0, 10) * 0.8f;
    public static int ElementCopies(int level) => level >= 6 ? 2 : 1;
    public static Element? ElementFor(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => Element.Life, EquipmentFamily.Shield => Element.Shield,
        EquipmentFamily.Fire => Element.Fire, EquipmentFamily.Water => Element.Water,
        EquipmentFamily.Cold => Element.Cold, EquipmentFamily.Lightning => Element.Lightning,
        EquipmentFamily.Arcane => Element.Arcane, EquipmentFamily.Earth => Element.Earth, _ => null
    };
    public static Element[] ShieldElements(EquipmentFamily family, int level) => ElementFor(family) is { } element
        ? level >= 6 ? [Element.Shield, element, element] : [Element.Shield, element] : [Element.Shield];
    public static string ActionName(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => "Pulso sanador", EquipmentFamily.Shield => "Amparo compartido",
        EquipmentFamily.Fire => "Onda de brasas", EquipmentFamily.Water => "Salpicadura",
        EquipmentFamily.Cold => "Pulso de escarcha", EquipmentFamily.Lightning => "Descarga selectiva",
        EquipmentFamily.Arcane => "Pulso arcano", EquipmentFamily.Earth => "Sacudida de piedra", _ => "Repulsión"
    };
    public static string ActionDescription(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => "Cura al portador y a sus aliados cercanos.",
        EquipmentFamily.Shield => "Protege también a los aliados cercanos.",
        EquipmentFamily.Fire => "Daña y aplica quemadura a los enemigos cercanos.",
        EquipmentFamily.Water => "Moja y empuja a los enemigos cercanos.",
        EquipmentFamily.Cold => "Daña y enfría a los enemigos cercanos.",
        EquipmentFamily.Lightning => "Alcanza al enemigo cercano más próximo; desde nivel 6, hasta dos.",
        EquipmentFamily.Arcane => "Inflige daño arcano a los enemigos cercanos.",
        EquipmentFamily.Earth => "Inflige daño de tierra y empuja a los enemigos cercanos.",
        _ => "Empuja a los enemigos cercanos."
    };
    public static string Describe(EquipmentSlot slot, EquipmentFamily family, int level, EquipmentModifiers? equipment = null)
    {
        if (slot == EquipmentSlot.GoldRing) return "ANILLO DE ORO · " + ItemAffixes.Describe(new(ItemAffixKind.CoinDividend), level);
        if (slot != EquipmentSlot.MagicRing) return "";
        string element = ElementFor(family).HasValue ? EquipmentCatalogue.FamilyName(family) : "básico";
        string shield = family == EquipmentFamily.Mixed ? "Escudo básico" : "Escudo + " + element + (level >= 6 ? " + " + element : "");
        return $"{ActionName(family)} · nivel {level}/10. {shield}: se repone incluso si se rompe.\n" +
            DescribeNumbers(family, level, equipment ?? EquipmentModifiers.None) + "\n" +
            "Respeta los escudos más fuertes y permite seguir lanzando hechizos. " + ActionDescription(family) + " " +
            (level < 6 ? "Desde nivel 6 duplica el elemento y potencia la acción. " : "") +
            "Los anillos no cuentan para conjuntos.";
    }
    public static string Describe(EquipmentItem item, int? level = null, EquipmentModifiers? equipment = null) =>
        Describe(item.Slot, item.Family, level ?? item.UpgradeLevel, equipment ?? ItemEquipment(item.Bonuses.Where(b => b.Stat == EquipmentStat.ShieldCapacity).Sum(b => b.Current)));
    public static string Describe(PublicEquipmentItem item, EquipmentModifiers? equipment = null) =>
        Describe(item.Slot, item.Family, item.UpgradeLevel, equipment ?? ItemEquipment(item.Bonuses.Where(b => b.Stat == EquipmentStat.ShieldCapacity).Sum(b => b.Current)));
}

public sealed record RingUpgradeRequest(string ItemId, int ExpectedLevel);
