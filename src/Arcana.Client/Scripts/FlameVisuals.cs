using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Lenguas de fuego propias: tres láminas cruzadas con volumen y contorno animado.</summary>
internal static class FlameVisuals
{
    internal const int Shape = 5, MaximumParticles = 192;
    private static ArrayMesh? _shape;

    internal static bool Matches(BeamSnapshot beam) => beam.Shape == SpellShape.Spray &&
        beam.Elements.Contains(Element.Fire) && !beam.Elements.Contains(Element.Water) &&
        !beam.Elements.Contains(Element.Cold);

    internal static ShaderMaterial Material() => new()
    {
        Shader = ResourceLoader.Load<Shader>("res://Assets/VFX/flamethrower.gdshader")
            ?? throw new InvalidDataException("Falta el shader del lanzallamas.")
    };

    internal static ArrayMesh Mesh()
    {
        if (_shape != null) return _shape;
        using var surface = new SurfaceTool();
        surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        const int segments = 8;
        for (int plane = 0; plane < 3; plane++)
        {
            float angle = plane * Mathf.Pi / 3;
            var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            void Vertex(float across, float along)
            {
                surface.SetNormal(new(-radial.Y, radial.X, 0));
                surface.SetUV(new(across, along));
                surface.SetUV2(radial);
                surface.AddVertex(new(radial.X * (across - .5f), radial.Y * (across - .5f), along - .5f));
            }
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1) / (float)segments;
                Vertex(0, a); Vertex(1, a); Vertex(1, b);
                Vertex(0, a); Vertex(1, b); Vertex(0, b);
            }
        }
        _shape = surface.Commit();
        return _shape;
    }
}
