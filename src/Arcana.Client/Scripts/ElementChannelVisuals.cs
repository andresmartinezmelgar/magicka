using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal static class ElementChannelVisuals
{
    internal const int Ice = 6, Water = 7, Steam = 8;
    internal static readonly Color IceBody = Ui.ElementColor(Element.Cold).Lerp(Colors.White, .72f);
    private static readonly Color IceShadow = Ui.ElementColor(Element.Cold).Lerp(Colors.White, .30f);
    private static readonly Color IceHighlight = Ui.ElementColor(Element.Cold).Lerp(Colors.White, .96f);
    private static readonly Dictionary<int, Godot.Mesh> Meshes = [];
    private static readonly Dictionary<int, ShaderMaterial> Materials = [];
    private static ArrayMesh? _waterJet;
    private static ShaderMaterial? _projectileIce;
    private static PrismMesh? _legacyIce;
    internal static PrismMesh LegacyIce => _legacyIce ??= new() { Size = new(.3f, .9f, .3f) };

    internal static bool IsSteam(Element[] elements) =>
        elements.Contains(Element.Fire) && elements.Contains(Element.Water);

    internal static int ParticleShape(BeamSnapshot beam)
    {
        if (IsSteam(beam.Elements) && beam.Shape is SpellShape.Spray or SpellShape.Beam) return Steam;
        if (beam.Shape != SpellShape.Spray) return -1;
        if (beam.Elements.Contains(Element.Cold)) return Ice;
        if (FlameVisuals.Matches(beam)) return FlameVisuals.Shape;
        return beam.Elements.Contains(Element.Water) ? Water : -1;
    }
    internal static bool ReplacesSpray(BeamSnapshot beam) =>
        beam.Shape == SpellShape.Spray && ParticleShape(beam) >= FlameVisuals.Shape;

    internal static ShaderMaterial Material(int shape)
    {
        if (Materials.TryGetValue(shape, out var material)) return material;
        string name = shape switch { Ice => "ice-crystal", Water => "water-drop", Steam => "steam", _ => "water-jet" };
        material = new() { Shader = ResourceLoader.Load<Shader>($"res://Assets/VFX/{name}.gdshader")
            ?? throw new InvalidDataException($"Falta el shader {name}.") };
        Materials.Add(shape, material); return material;
    }

    internal static ShaderMaterial ProjectileIceMaterial()
    {
        if (_projectileIce != null) return _projectileIce;
        _projectileIce = new() { Shader = Material(Ice).Shader };
        _projectileIce.SetShaderParameter("life_driven", false);
        return _projectileIce;
    }

    internal static Godot.Mesh Mesh(int shape)
    {
        if (Meshes.TryGetValue(shape, out var mesh)) return mesh;
        mesh = shape == Ice ? IceCrystal() : new QuadMesh { Size = Vector2.One };
        Meshes.Add(shape, mesh); return mesh;
    }

    private static ArrayMesh IceCrystal()
    {
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        Color dark = IceShadow, light = IceBody, white = IceHighlight;
        void Vertex(Vector2 p, Color color)
        { surface.SetNormal(Vector3.Back); surface.SetColor(color); surface.AddVertex(new(p.X, p.Y, .025f)); }
        void Arm(Vector2 from, Vector2 to, float width)
        {
            var axis = (to - from).Normalized(); var side = new Vector2(-axis.Y, axis.X) * width;
            Vector2 a = from + side, b = from - side, mid = from.Lerp(to, .55f);
            Vertex(a, light); Vertex(b, dark); Vertex(mid, white);
            Vertex(b, dark); Vertex(to, light); Vertex(mid, white);
            Vertex(to, light); Vertex(a, light); Vertex(mid, white);
        }
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            var axis = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            Arm(Vector2.Zero, axis, .07f);
            foreach (float at in new[] { .40f, .66f })
            foreach (int sign in new[] { -1, 1 })
            {
                float branch = angle + sign * Mathf.Pi / 3;
                var end = axis * at + new Vector2(MathF.Cos(branch), MathF.Sin(branch)) * (at < .5f ? .29f : .22f);
                Arm(axis * at, end, .045f);
            }
        }
        return surface.Commit();
    }

    internal static ArrayMesh WaterJet()
    {
        if (_waterJet != null) return _waterJet;
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        const int segments = 40, sides = 10;
        void Vertex(int along, int radial)
        {
            float angle = radial / (float)sides * Mathf.Tau;
            var normal = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            surface.SetNormal(normal); surface.SetUV(new(radial / (float)sides, along / (float)segments));
            surface.AddVertex(new(normal.X, normal.Y, along / (float)segments));
        }
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            Vertex(i, j); Vertex(i + 1, j); Vertex(i + 1, j + 1);
            Vertex(i, j); Vertex(i + 1, j + 1); Vertex(i, j + 1);
        }
        _waterJet = surface.Commit(); return _waterJet;
    }

    internal static void ClearCache()
    {
        foreach (var mesh in Meshes.Values) mesh.Dispose(); Meshes.Clear();
        foreach (var material in Materials.Values) material.Dispose(); Materials.Clear();
        _waterJet?.Dispose(); _waterJet = null;
        _projectileIce?.Dispose(); _projectileIce = null;
        _legacyIce?.Dispose(); _legacyIce = null;
    }
}
