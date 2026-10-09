namespace Arcana.Core;

public enum Element { Water, Life, Shield, Cold, Lightning, Arcane, Earth, Fire }
public enum SpellShape { Force, Spray, Beam, Lightning, Rock, Shards, Barrier }
public enum GameMode { Duel, Arena, Warfare }

/// <summary>La selección es una lista; los elementos incompatibles se anulan al pulsar la tecla.</summary>
public sealed class ElementBuffer
{
    private readonly List<Element> _elements = [];
    public IReadOnlyList<Element> Elements => _elements;
    public bool Add(Element element)
    {
        if (!Enum.IsDefined(element)) return false;
        int incompatible = _elements.FindIndex(other => Opposite(other, element));
        if (incompatible >= 0) { _elements.RemoveAt(incompatible); return true; }
        if (_elements.Count >= 3) return false;
        _elements.Add(element);
        return true;
    }
    public void Clear() => _elements.Clear();
    public Element[] Consume() { var result = _elements.ToArray(); Clear(); return result; }
    public static bool Opposite(Element a, Element b) =>
        a == Element.Shield && b == Element.Shield ||
        Pair(a, b, Element.Fire, Element.Cold) ||
        Pair(a, b, Element.Life, Element.Arcane) ||
        Pair(a, b, Element.Water, Element.Lightning) ||
        Pair(a, b, Element.Earth, Element.Lightning);
    private static bool Pair(Element a, Element b, Element x, Element y) => a == x && b == y || a == y && b == x;
}

/// <summary>Resuelve cualquier combinación válida, sin un catálogo de hechizos prefijados.</summary>
public sealed class SpellRecipe
{
    public Element[] Elements { get; }
    public SpellShape Shape { get; }
    public bool Steam => Has(Element.Water) && Has(Element.Fire);
    public bool Ice => Has(Element.Water) && Has(Element.Cold);
    public bool Healing => Has(Element.Life);
    // Solo las rocas imbuidas salpican. Tierra pura y los fragmentos de hielo siguen siendo impactos directos.
    public float SplashRadius => Shape != SpellShape.Rock ? 0
        : Has(Element.Arcane) ? 2.8f : Has(Element.Fire) ? 2.5f
        : Has(Element.Water) ? 2.7f : Has(Element.Cold) ? 2.1f : 0;
    public string? GroundKind => Shape != SpellShape.Rock ? null
        : Has(Element.Fire) ? "burning_ground" : Has(Element.Cold) ? "frost_ground"
        : Has(Element.Water) ? "water_pool" : null;
    public int Count(Element e)
    {
        int count = 0;
        foreach (var element in Elements) if (element == e) count++;
        return count;
    }
    public bool Has(Element e) => Elements.Contains(e);
    public float Power => Math.Max(1, Elements.Length);
    public SpellRecipe(IEnumerable<Element> elements)
    {
        Span<Element> resolved = stackalloc Element[3];
        int count = 0;
        foreach (var element in elements)
        {
            if (!Enum.IsDefined(element)) continue;
            int opposite = -1;
            for (int i = 0; i < count; i++) if (ElementBuffer.Opposite(resolved[i], element)) { opposite = i; break; }
            if (opposite >= 0)
            {
                for (int i = opposite; i + 1 < count; i++) resolved[i] = resolved[i + 1];
                count--;
            }
            else if (count < resolved.Length) resolved[count++] = element;
        }
        Elements = resolved[..count].ToArray();
        Shape = Has(Element.Shield) ? SpellShape.Barrier
            : Has(Element.Earth) ? SpellShape.Rock
            : Ice ? SpellShape.Shards
            : Has(Element.Arcane) || Has(Element.Life) ? SpellShape.Beam
            : Has(Element.Lightning) ? SpellShape.Lightning
            : Elements.Length > 0 ? SpellShape.Spray : SpellShape.Force;
    }
    public float Range => Shape switch
    {
        SpellShape.Beam => 17, SpellShape.Lightning => 8.5f,
        SpellShape.Spray => Steam ? 6.5f : 6, SpellShape.Force => 4, _ => 20
    };
}
