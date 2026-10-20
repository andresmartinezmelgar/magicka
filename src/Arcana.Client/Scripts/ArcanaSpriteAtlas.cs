using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Approved element icons and character-free defense art. Shield sprites are shared
/// across all recipes (E, EX and EXY), with deterministic layers for two affinities.
/// Purely visual: no gameplay/network logic lives here.
/// </summary>
internal static class ArcanaSpriteAtlas
{
    internal enum DefenseForm { Personal, Area, Wall }

    private const int Columns = 4;
    private const int ElementCell = 96;
    private const int ShieldCell = 160;
    private const string ElementsPath = "res://Assets/VFX/element-style-b.webp";
    private static readonly string[] DefensePaths =
    [
        "res://Assets/VFX/personal-shields-style-b.webp",
        "res://Assets/VFX/area-shields-style-b.webp",
        "res://Assets/VFX/wall-shields-style-b.webp"
    ];

    // E, EW, ER, ES, EF, EQ, ED, EA: valid shield-affinity motifs.
    private static readonly Element[] Affinities =
    [
        Element.Life, Element.Cold, Element.Arcane, Element.Fire,
        Element.Water, Element.Earth, Element.Lightning
    ];

    private static Texture2D? _elements;
    private static bool _elementsAttempted;
    private static readonly Texture2D?[] _defenseAtlases = new Texture2D?[3];
    private static readonly bool[] _defenseAttempted = new bool[3];
    private static readonly Dictionary<int, AtlasTexture> ElementRegions = [];
    private static readonly Dictionary<int, AtlasTexture>[] DefenseRegions =
        [new(), new(), new()];

    private static Texture2D? Load(string path)
        => ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;

    private static Texture2D? Region(Texture2D? atlas, int cell, int index,
        Dictionary<int, AtlasTexture> regions)
    {
        if (atlas == null) return null;
        if (regions.TryGetValue(index, out var cached)) return cached;
        int x = (index % Columns) * cell, y = (index / Columns) * cell;
        if (atlas.GetWidth() < x + cell || atlas.GetHeight() < y + cell) return null;
        var texture = new AtlasTexture
        {
            Atlas = atlas,
            Region = new Rect2(x, y, cell, cell),
            FilterClip = true
        };
        regions[index] = texture;
        return texture;
    }

    internal static Texture2D? Icon(Element element)
    {
        if (!_elementsAttempted)
        {
            _elementsAttempted = true;
            _elements = Load(ElementsPath);
        }
        return Region(_elements, ElementCell, (int)element, ElementRegions);
    }

    // Layer 0: first unique affinity (or basic shield).
    // Layer 1: optional second unique affinity. The keyboard input order does
    // not change the visual result; repeated elements strengthen existing VFX.
    internal static Texture2D? Defense(DefenseForm form, Element[] elements, int layer = 0)
    {
        if (layer is < 0 or > 1) return null;
        int atlasIndex = (int)form;
        if (!_defenseAttempted[atlasIndex])
        {
            _defenseAttempted[atlasIndex] = true;
            _defenseAtlases[atlasIndex] = Load(DefensePaths[atlasIndex]);
        }

        int chosen = 0, count = 0;
        foreach (var element in Affinities)
        {
            if (Array.IndexOf(elements, element) < 0) continue;
            if (count == layer)
            {
                chosen = Array.IndexOf(Affinities, element) + 1;
                count++;
                break;
            }
            count++;
        }
        if (count <= layer) return layer == 0
            ? Region(_defenseAtlases[atlasIndex], ShieldCell, 0, DefenseRegions[atlasIndex])
            : null;
        return Region(_defenseAtlases[atlasIndex], ShieldCell, chosen, DefenseRegions[atlasIndex]);
    }

    internal static int AffinityLayers(Element[] elements)
    {
        int count = 0;
        foreach (var affinity in Affinities)
            if (Array.IndexOf(elements, affinity) >= 0) count++;
        return Math.Clamp(count, 1, 2);
    }
}
