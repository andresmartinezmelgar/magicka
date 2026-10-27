using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private readonly record struct SurvivalPalette(string GroundA, string GroundB, string Border, string Cap,
        string Outside, string Accent, string Ambient, string Sun);
    private SurvivalPalette _survivalPalette;
    private ShaderMaterial? _survivalFloorMaterial;
    internal int SurvivalDecorations { get; private set; }

    private static SurvivalPalette BiomePalette(ArenaTheme theme) => theme switch
    {
        ArenaTheme.Forest => new("293d2e", "3c5237", "584735", "899069", "182b24", "72965c", "c3d2cf", "f1edcd"),
        ArenaTheme.City => new("35424c", "4c535a", "59646b", "91a0a3", "202d37", "bd9b61", "c3ccd9", "ece6d8"),
        ArenaTheme.Ruins => new("3c4235", "535641", "666852", "969879", "242d25", "78905c", "c3cfbd", "e6dfbb"),
        ArenaTheme.Sanctuary => new("343e51", "4a5263", "68728b", "a7b6c2", "1e2637", "91b1cb", "c1cbe1", "e6e9ff"),
        ArenaTheme.Desert => new("594d3a", "756047", "8b7252", "b59b70", "302b23", "a9b66b", "dacbb3", "ffe1b1"),
        ArenaTheme.Frozen => new("354956", "526a76", "667e8b", "a6bdc8", "1c2c3b", "94c5d2", "bbcfe1", "d6e7ff"),
        ArenaTheme.Volcanic => new("302d34", "4a373c", "48404a", "8b5946", "291b23", "ba7149", "d0bdc9", "f1c8ac"),
        ArenaTheme.Crystal => new("303950", "4b4966", "5b6184", "9ca6c6", "1d2238", "7fa8bc", "c3c6e3", "e5ddff"),
        ArenaTheme.Marsh => new("293b35", "425143", "4f5041", "879177", "172a2e", "829a6d", "bfcdc7", "dee8d5"),
        ArenaTheme.Fungal => new("373348", "495163", "5d536e", "9e8aaf", "251f35", "80adae", "cec4de", "eee0f3"),
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    private void PrepareSurvivalScenery(Node3D root)
    {
        SceneryTrees = SceneryBuildings = SurvivalDecorations = 0;
        ScenerySourceMeshes = SceneryDrawMeshes = ScenerySourceVertices = SceneryBatchedVertices = 0;
        _survivalPalette = BiomePalette(_map.Theme);
        root.SetMeta("survival_biome", (int)_map.Theme);
        _environment.BackgroundColor = new(_survivalPalette.Outside);
        _environment.AmbientLightColor = new(_survivalPalette.Ambient);
        _environment.AmbientLightEnergy = Ui.IsMobile ? .48f : .55f;
        _sun.LightColor = new(_survivalPalette.Sun); _sun.LightEnergy = Ui.IsMobile ? .90f : .95f;
        var outside = Mesh(root, new PlaneMesh { Size = new(_map.HalfWidth * 2 + 48, _map.HalfHeight * 2 + 48) },
            new(0, -.22f, 0), new(_survivalPalette.Outside));
        outside.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _survivalFloorMaterial = new ShaderMaterial { Shader = SurvivalGroundShader };
        _survivalFloorMaterial.SetShaderParameter("biome", (int)_map.Theme);
        _survivalFloorMaterial.SetShaderParameter("ground_a", new Color(_survivalPalette.GroundA));
        _survivalFloorMaterial.SetShaderParameter("ground_b", new Color(_survivalPalette.GroundB));
        _survivalFloorMaterial.SetShaderParameter("accent", new Color(_survivalPalette.Accent));
    }

    private void BuildSurvivalBorder(Node3D root, Vector3 size, Vector3 at)
    {
        MeshInstance3D Part(Vector3 dimensions, Vector3 position, string color)
        {
            var piece = Box(root, dimensions, position, new(color));
            piece.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; return piece;
        }
        bool horizontal = size.X >= size.Z;
        if (_map.Theme is ArenaTheme.Forest or ArenaTheme.Marsh)
        {
            float length = horizontal ? size.X : size.Z;
            var direction = horizontal ? Vector3.Right : Vector3.Back;
            int posts = Math.Max(1, (int)MathF.Ceiling(length / 3));
            for (int i = 0; i <= posts; i++)
                Part(new(.18f, .95f, .18f), new Vector3(at.X, .475f, at.Z) + direction * (length * (i / (float)posts - .5f)), _survivalPalette.Border);
            foreach (float height in new[] { .35f, .72f })
                Part(horizontal ? new(size.X, .12f, .16f) : new(.16f, .12f, size.Z), new(at.X, height, at.Z), _survivalPalette.Cap);
            return;
        }
        Part(new(size.X, .85f, size.Z), new(at.X, .425f, at.Z), _survivalPalette.Border);
        var cap = Part(new(size.X, .09f, size.Z), new(at.X, .895f, at.Z), _survivalPalette.Cap);
        if (_map.Theme == ArenaTheme.Volcanic)
            cap.MaterialOverride = Material(new(_survivalPalette.Accent), .12f);
    }
}
