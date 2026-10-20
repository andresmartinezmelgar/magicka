using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Approved style-B art. Both atlases share four columns, with independent cell sizes.
/// Loaded once; regions are reused by HUD, personal wards, walls and field effects.
/// Missing assets fail gracefully to existing procedural visuals.
/// </summary>
internal static class ArcanaSpriteAtlas
{
    private const int Columns = 4;
    private const int ElementCell = 96;
    private const int ShieldCell = 96;
    private const string ElementsPath = "res://Assets/VFX/element-style-b.webp";
    private const string ShieldsPath = "res://Assets/VFX/shields-style-b.webp";

    private static Texture2D? _elements, _shields;
    private static bool _elementsAttempted, _shieldsAttempted;
    private static readonly Dictionary<int, AtlasTexture> ElementRegions = [];
    private static readonly Dictionary<int, AtlasTexture> ShieldRegions = [];

    private static Texture2D? Load(string path, ref Texture2D? cache, ref bool attempted)
    {
        if (attempted) return cache;
        attempted = true;
        if (ResourceLoader.Exists(path))
            cache = ResourceLoader.Load<Texture2D>(path);
        return cache;
    }

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
        var atlas = Load(ElementsPath, ref _elements, ref _elementsAttempted);
        return Region(atlas, ElementCell, (int)element, ElementRegions);
    }

    private static Texture2D? Shield(int index)
    {
        var atlas = Load(ShieldsPath, ref _shields, ref _shieldsAttempted);
        return Region(atlas, ShieldCell, index, ShieldRegions);
    }

    // Tiles 0..3: E, EW, ER, ES (shield, life, cold, arcane).
    internal static Texture2D? Personal(Element[] elements)
    {
        if (elements.Contains(Element.Life)) return Shield(1);
        if (elements.Contains(Element.Cold)) return Shield(2);
        if (elements.Contains(Element.Arcane)) return Shield(3);
        return Shield(0);
    }

    // Tiles 4..7: EF, EQ, ER, ED; these are defensive field motifs.
    internal static Texture2D? Area(Element[] elements)
    {
        if (elements.Contains(Element.Fire)) return Shield(4);
        if (elements.Contains(Element.Water)) return Shield(5);
        if (elements.Contains(Element.Cold)) return Shield(6);
        if (elements.Contains(Element.Earth)) return Shield(7);
        return null;
    }

    // Tiles 8..11: ED, EDF, EQR, ES.
    internal static Texture2D? Barrier(Element[] elements)
    {
        if (elements.Contains(Element.Earth) && elements.Contains(Element.Fire)) return Shield(9);
        if (elements.Contains(Element.Water) && elements.Contains(Element.Cold)) return Shield(10);
        if (elements.Contains(Element.Earth)) return Shield(8);
        if (elements.Contains(Element.Arcane)) return Shield(11);
        return null;
    }

    internal static Texture2D? Mine(Element[] elements, bool healing)
        => healing ? Shield(1) : elements.Contains(Element.Arcane) ? Shield(11) : Area(elements);
}
