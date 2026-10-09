namespace Arcana.Core;

/// <summary>Relaciones compartidas por guardas, escudos y barreras. No cambian la función de curar o mojar.</summary>
public static class ElementInteractions
{
    public const float Advantage = 1.5f, Disadvantage = 0.5f, SameElement = 0.18f;
    private static readonly Element[] BasicShield = [Element.Shield];
    public static ReadOnlySpan<Element> ShieldProtection => BasicShield;

    public static Element StrongAgainst(Element element) => element switch
    {
        Element.Water => Element.Fire, Element.Fire => Element.Cold, Element.Cold => Element.Water,
        Element.Earth => Element.Lightning, Element.Lightning => Element.Shield,
        Element.Shield => Element.Arcane, Element.Arcane => Element.Life,
        Element.Life => Element.Earth,
        _ => throw new ArgumentOutOfRangeException(nameof(element))
    };
    public static Element Weakness(Element element)
    {
        // La debilidad se deriva de la misma tabla; nunca se mantiene una segunda lista.
        for (int i = 0; i < 8; i++) if (StrongAgainst((Element)i) == element) return (Element)i;
        throw new ArgumentOutOfRangeException(nameof(element));
    }
    public static float Multiplier(Element attack, Element defense) => attack == defense ? SameElement
        : StrongAgainst(attack) == defense ? Advantage : StrongAgainst(defense) == attack ? Disadvantage : 1;

    public static float ProtectionMultiplier(Element attack, ReadOnlySpan<Element> protection)
    {
        // E aporta la forma de una barrera. Si está imbuida, la defensa es su mezcla elemental.
        bool imbued = false;
        foreach (var element in protection) if (element != Element.Shield) { imbued = true; break; }
        float total = 0; int count = 0;
        foreach (var element in protection)
        {
            if (imbued && element == Element.Shield) continue;
            total += Multiplier(attack, element); count++;
        }
        return count == 0 ? 1 : total / count;
    }
}
