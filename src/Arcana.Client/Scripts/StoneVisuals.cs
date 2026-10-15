using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Roca facetada compartida por proyectiles, armadura y fragmentos; solo presentación.</summary>
internal static class StoneVisuals
{
    private static readonly ArrayMesh?[] Shapes = new ArrayMesh?[4];
    private static readonly Dictionary<(int Elements, int Tone), ShaderMaterial> Materials = [];
    private static Shader? _shader;
    private static readonly Color[] Chips = [new("a68a65"), new("78634e"), new("ccb18a")];
    internal static Color ChipColor(int index) => Chips[(int)((uint)index % Chips.Length)];

    internal static ArrayMesh Shape(int seed = 0)
    {
        int variant = (int)((uint)seed % Shapes.Length);
        if (Shapes[variant] is { } existing) return existing;
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] points = [new(-1,t,0),new(1,t,0),new(-1,-t,0),new(1,-t,0),
            new(0,-1,t),new(0,1,t),new(0,-1,-t),new(0,1,-t),
            new(t,0,-1),new(t,0,1),new(-t,0,-1),new(-t,0,1)];
        int[] triangles = [0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,
            11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,
            4,9,5,2,4,11,6,2,10,8,6,7,9,8,1];
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        Vector3 Deform(Vector3 point)
        {
            // Los vértices compartidos reciben la misma deformación, sin huecos entre caras.
            point = point.Normalized();
            float noise = 0.5f + 0.5f * Mathf.Sin(point.Dot(new(13.7f, 27.1f, 19.3f)) + variant * 4.7f);
            return point * (0.82f + noise * 0.18f) * new Vector3(1, 0.90f, 0.96f);
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            a = Deform(a); b = Deform(b); c = Deform(c);
            var normal = (b - a).Cross(c - a).Normalized();
            if (normal.Dot(a + b + c) < 0) { (b, c) = (c, b); normal = -normal; }
            float shade = 0.70f + 0.30f * (0.5f + 0.5f * Mathf.Sin((a + b + c).Dot(new(9, 17, 23))));
            surface.SetColor(new(shade, shade, shade)); surface.SetNormal(normal);
            // Godot usa el orden horario para las caras exteriores.
            surface.AddVertex(a); surface.AddVertex(c); surface.AddVertex(b);
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = points[triangles[i]].Normalized(); var b = points[triangles[i + 1]].Normalized();
            var c = points[triangles[i + 2]].Normalized();
            var ab = (a + b).Normalized(); var bc = (b + c).Normalized(); var ca = (c + a).Normalized();
            Triangle(a, ab, ca); Triangle(ab, b, bc); Triangle(ca, bc, c); Triangle(ab, bc, ca);
        }
        return Shapes[variant] = surface.Commit();
    }

    internal static ShaderMaterial Surface(Element[] elements, int tone = 0)
    {
        int mask = 0; foreach (var element in elements) mask |= 1 << (int)element;
        var key = (mask, tone);
        if (Materials.TryGetValue(key, out var cached)) return cached;
        _shader ??= new Shader { Code = """
            shader_type spatial;
            uniform vec4 accent : source_color = vec4(0.78, 0.58, 0.31, 1.0);
            uniform float imbued = 0.0;
            uniform float tone = 1.0;
            varying vec3 local_position;
            float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
            float grain(vec2 p) {
                vec2 cell = floor(p);
                vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
                return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
                    mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
            }
            void vertex() { local_position = VERTEX; }
            void fragment() {
                vec3 p = local_position * 3.5;
                vec2 plane = vec2(p.x + p.y * 0.61, p.z - p.y * 0.37);
                float mineral = grain(plane) * 0.8 + grain(plane * 3.2) * 0.2;
                float seam = abs(sin(p.x * 1.6 + p.z * 0.8 + grain(plane.yx * 0.8) * 3.0));
                float cracks = 1.0 - smoothstep(0.025, 0.085, seam);
                vec3 rock = mix(vec3(0.16, 0.15, 0.13), vec3(0.39, 0.34, 0.27), mineral);
                rock *= COLOR.rgb * tone;
                ALBEDO = mix(rock, mix(rock * 0.32, accent.rgb, imbued * 0.8), cracks * 0.78);
                ROUGHNESS = 0.97;
                SPECULAR = 0.12;
                EMISSION = rock * 0.055 + accent.rgb * cracks * imbued * 0.65;
            }
            """ };
        Element[] secondary = elements.Where(e => e is not (Element.Earth or Element.Shield)).ToArray();
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("accent", secondary.Length > 0 ? Ui.SpellColor(secondary) : Ui.ElementColor(Element.Earth));
        material.SetShaderParameter("imbued", secondary.Length > 0 ? 1f : 0f);
        material.SetShaderParameter("tone", 1f - Math.Clamp(tone, 0, 2) * 0.08f);
        Materials[key] = material; return material;
    }
}
