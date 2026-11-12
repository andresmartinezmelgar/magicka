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
        EquipmentFamily.Life => "Curación de grupo", EquipmentFamily.Shield => "Escudo para aliados",
        EquipmentFamily.Fire => "Onda de fuego", EquipmentFamily.Water => "Onda de agua",
        EquipmentFamily.Cold => "Onda de frío", EquipmentFamily.Lightning => "Descarga de rayo",
        EquipmentFamily.Arcane => "Onda arcana", EquipmentFamily.Earth => "Onda de tierra", _ => "Empuje a enemigos"
    };
    public static string ActionDescription(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => "Al activarse, te cura a ti y a los aliados cercanos. Su escudo también te regenera vida mientras siga activo.",
        EquipmentFamily.Shield => "Al activarse, crea un escudo para ti y otro para cada aliado cercano.",
        EquipmentFamily.Fire => "Daña y aplica quemadura a los enemigos cercanos.",
        EquipmentFamily.Water => "Moja y empuja a los enemigos cercanos.",
        EquipmentFamily.Cold => "Daña y enfría a los enemigos cercanos.",
        EquipmentFamily.Lightning => "Ataca al enemigo más cercano. Desde el nivel 6 alcanza hasta dos enemigos distintos, con el mismo daño por enemigo.",
        EquipmentFamily.Arcane => "Inflige daño arcano a los enemigos cercanos.",
        EquipmentFamily.Earth => "Inflige daño de tierra y empuja a los enemigos cercanos.",
        _ => "Empuja a los enemigos cercanos."
    };
    public static string Describe(EquipmentSlot slot, EquipmentFamily family, int level, EquipmentModifiers? equipment = null)
    {
        if (slot == EquipmentSlot.GoldRing) return "ANILLO DE ORO · " + ItemAffixes.Describe(new(ItemAffixKind.CoinDividend), level);
        if (slot != EquipmentSlot.MagicRing) return "";
        return $"{ActionName(family)} · Nivel {level}/10.\n" +
            DescribeNumbers(family, level, equipment ?? EquipmentModifiers.None) + "\n" +
            "Si el escudo se rompe, se repone en la siguiente activación; no al instante.\n" +
            "Conserva un escudo activo cuya capacidad máxima sea mayor. Puedes seguir lanzando tus hechizos.\n" +
            (level < 6 ? "Al llegar al nivel 6: " : "Mejora del nivel 6 activa: ") + LevelSixDescription(family) + "\n" +
            "Los anillos no cuentan para las bonificaciones de conjunto.";
    }
    private static string LevelSixDescription(EquipmentFamily family) => family switch
    {
        EquipmentFamily.Life => "se duplican el escudo base, la regeneración y la curación al activarse.",
        EquipmentFamily.Shield => "se duplican los escudos base para ti y para cada aliado.",
        EquipmentFamily.Lightning => "se duplica el escudo base y puedes alcanzar a dos enemigos distintos. El daño por enemigo no aumenta.",
        EquipmentFamily.Fire => "se duplican el escudo base y el daño del impacto. El daño por segundo de la quemadura no cambia.",
        EquipmentFamily.Water => "se duplican el escudo base, el empuje y el daño contra escudos.",
        EquipmentFamily.Cold => "se duplican el escudo base, el daño del impacto y el frío aplicado.",
        EquipmentFamily.Earth => "se duplican el escudo base, el daño del impacto y el empuje.",
        EquipmentFamily.Arcane => "se duplican el escudo base y el daño del impacto.",
        _ => "se duplican el escudo base y la fuerza del empuje."
    };
    public static string Describe(EquipmentItem item, int? level = null, EquipmentModifiers? equipment = null) =>
        Describe(item.Slot, item.Family, level ?? item.UpgradeLevel, equipment ?? ItemEquipment(item.Bonuses.Where(b => b.Stat == EquipmentStat.ShieldCapacity).Sum(b => b.Current)));
    public static string Describe(PublicEquipmentItem item, EquipmentModifiers? equipment = null) =>
        Describe(item.Slot, item.Family, item.UpgradeLevel, equipment ?? ItemEquipment(item.Bonuses.Where(b => b.Stat == EquipmentStat.ShieldCapacity).Sum(b => b.Current)));
}

public sealed record RingUpgradeRequest(string ItemId, int ExpectedLevel);
