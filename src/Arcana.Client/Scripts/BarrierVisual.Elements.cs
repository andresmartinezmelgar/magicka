using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class BarrierVisual
{
    private static Shader? _elementShader;
    private ShaderMaterial? _elementSkin;
    private StandardMaterial3D? _iceSkin;
    internal bool Stone { get; private set; }
    internal bool HasElementSurface => _elementSkin != null;

    private void BuildElements(WallSnapshot wall, float length, bool stone)
    {
        var recipe = new SpellRecipe(wall.Elements);
        if (!wall.Elements.Any(e => e is Element.Fire or Element.Water or Element.Cold or Element.Lightning)) return;
        _elementShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
            uniform float span = 4.4;
            uniform float clock = 0.0;
            uniform float fade = 1.0;
            uniform float fire = 0.0;
            uniform float water = 0.0;
            uniform float frost = 0.0;
            uniform float electric = 0.0;
            uniform float steam = 0.0;
            uniform float stone = 0.0;
            float noise(vec2 p) {
                return sin(p.x * 2.7 + sin(p.y * 3.1)) * 0.5 + sin(p.x * 5.3 - p.y * 1.7) * 0.25;
            }
            void fragment() {
                float x = UV.x * span;
                float y = UV.y;
                vec3 light = vec3(0.0);
                float alpha = 0.0;
                float sides = smoothstep(0.0, 0.025, UV.x) * smoothstep(0.0, 0.025, 1.0 - UV.x);
                if (fire > 0.0) {
                    float tongues = (stone > 0.0 ? 0.66 : 0.48) + 0.13 * sin(x * 7.0 + clock * 3.4)
                        + 0.08 * sin(x * 13.0 - clock * 4.3);
                    float flame = 1.0 - smoothstep(tongues - 0.15, tongues + 0.05, y);
                    float flicker = 0.65 + 0.35 * sin(x * 11.0 - y * 13.0 + clock * 8.0);
                    vec3 heat = mix(vec3(1.0, 0.17, 0.025), vec3(1.0, 0.78, 0.2), pow(1.0 - y, 3.0));
                    float a = flame * flicker * fire * (stone > 0.0 ? 0.67 : 0.78);
                    if (stone > 0.0) a *= 0.18 + smoothstep(0.32, 0.50, y) * 0.82;
                    light += heat * a; alpha += a;
                }
                if (water > 0.0) {
                    float stream = pow(0.5 + 0.5 * sin(x * 15.0 + sin(y * 3.0 + clock) * 0.6), 5.0);
                    float flow = pow(0.5 + 0.5 * sin(y * 24.0 + clock * 13.0 + x * 4.0), 9.0);
                    float foam = (1.0 - smoothstep(0.0, 0.14, y)) * (0.5 + stream * 0.5);
                    float a = (0.18 + stream * 0.35 + flow * stream * 0.17 + foam * 0.35)
                        * water * smoothstep(0.0, 0.05, 1.0 - y);
                    light += mix(vec3(0.08, 0.46, 0.88), vec3(0.75, 0.95, 1.0), stream * 0.35 + foam * 0.6) * a;
                    alpha += a;
                }
                if (frost > 0.0) {
                    float crystal = abs(sin(x * 5.5 + y * 6.0)) * abs(sin(x * 8.0 - y * 11.0));
                    float rim = pow(1.0 - y, 6.0) + pow(y, 12.0);
                    float a = (0.14 + (1.0 - smoothstep(0.02, 0.085, crystal)) * 0.45 + rim * 0.3) * frost;
                    light += vec3(0.46, 0.84, 1.0) * a; alpha += a;
                }
                if (electric > 0.0) {
                    float tick = floor(clock * 9.0);
                    float path = 0.42 + sin(x * 4.5 + tick * 2.8) * 0.11
                        + sin(x * 12.0 - tick * 4.1) * 0.075;
                    float bolt = 1.0 - smoothstep(0.008, 0.03, abs(y - path));
                    float branch = (1.0 - smoothstep(0.006, 0.017, abs(y - path - sin(x * 8.0) * 0.18)))
                        * step(0.6, sin(x * 3.0 + tick));
                    float a = (bolt + branch * 0.5) * (0.45 + 0.5 * pow(0.5 + 0.5 * sin(clock * 17.0), 4.0)) * electric;
                    light += vec3(0.57, 0.65, 1.0) * a; alpha += a;
                }
                if (steam > 0.0) {
                    float cloud = smoothstep(-0.35, 0.35, noise(vec2(x * 1.7, y * 3.0 - clock * 1.4)));
                    float a = cloud * 0.45 * sin(y * 3.14159) * steam;
                    light += vec3(0.75, 0.86, 0.89) * a; alpha += a;
                }
                ALBEDO = light / max(alpha, 0.001);
                EMISSION = light * 0.22;
                ALPHA = min(alpha, 0.88) * sides * fade;
            }
            """ };
        _elementSkin = new ShaderMaterial { Shader = _elementShader };
        _elementSkin.SetShaderParameter("span", length);
        _elementSkin.SetShaderParameter("stone", stone ? 1 : 0);
        _elementSkin.SetShaderParameter("fire", recipe.Has(Element.Fire) && !recipe.Steam ? 1 : 0);
        _elementSkin.SetShaderParameter("water", recipe.Has(Element.Water) && !recipe.Steam && !recipe.Ice ? 1 : 0);
        _elementSkin.SetShaderParameter("frost", recipe.Has(Element.Cold) ? 1 : 0);
        _elementSkin.SetShaderParameter("electric", recipe.Has(Element.Lightning) ? 1 : 0);
        _elementSkin.SetShaderParameter("steam", recipe.Steam ? 1 : 0);
        var face = new SurfaceTool(); face.Begin(Godot.Mesh.PrimitiveType.Triangles);
        const int segments = 24;
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments, b = (i + 1) / (float)segments;
            void Vertex(float u, float v)
            {
                face.SetUV(new(u, v));
                float height = stone ? recipe.Has(Element.Fire) && !recipe.Steam ? 3.2f : 1.8f
                    : 1.55f + MathF.Sin(u * MathF.PI) * 0.65f;
                face.AddVertex(new(side * (stone ? 0.25f : 0.025f), 0.08f + height * v, (u - 0.5f) * length));
            }
            Vertex(a, 0); Vertex(b, 0); Vertex(a, 1);
            Vertex(a, 1); Vertex(b, 0); Vertex(b, 1);
        }
        face.GenerateNormals(); Piece(face.Commit(), Vector3.Zero, _elementSkin);
        if (recipe.Has(Element.Cold))
        {
            _iceSkin = new StandardMaterial3D { AlbedoColor = new Color(new Color("9cddf1"), 0.68f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.22f,
                EmissionEnabled = true, Emission = new("58afd1"), EmissionEnergyMultiplier = 0.18f };
            int count = Math.Clamp((int)MathF.Ceiling(length / 0.55f), 3, 12);
            for (int i = 0; i < count; i++)
            {
                float z = ((i + 0.5f) / count - 0.5f) * length;
                var crystal = Piece(new PrismMesh { Size = new(0.16f, 0.45f + i % 3 * 0.12f, 0.24f) },
                    new(0, stone ? 1.58f : 1.55f + MathF.Sin((i + 0.5f) / count * MathF.PI) * 0.65f, z), _iceSkin);
                crystal.Rotation = new(0, i * 0.7f, MathF.Sin(i * 2.7f) * 0.12f);
            }
        }
    }

    private void UpdateElements(float fade, float time)
    {
        if (_elementSkin == null) return;
        _elementSkin.SetShaderParameter("clock", time);
        _elementSkin.SetShaderParameter("fade", fade);
        if (_iceSkin != null) _iceSkin.AlbedoColor = new(new Color("9cddf1"), 0.68f * fade);
    }
}
