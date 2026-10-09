using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class BarrierVisual
{
    private static Shader? _elementShader;
    private ShaderMaterial? _elementSkin, _foamSkin;
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
            uniform float ground = 0.0;
            uniform float reinforcement = 0.0;
            uniform float mobile_contrast = 0.0;
            uniform vec4 water_color : source_color;
            uniform vec4 frost_color : source_color;
            uniform vec4 electric_color : source_color;
            float hash(vec2 p) {
                return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
            }
            float grain(vec2 p) {
                vec2 cell = floor(p);
                vec2 f = fract(p);
                f = f * f * (3.0 - 2.0 * f);
                return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
                    mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
            }
            void fragment() {
                float x = UV.x * span;
                float y = UV.y;
                vec3 light = vec3(0.0);
                float alpha = 0.0;
                float sides = smoothstep(0.0, 0.025, UV.x) * smoothstep(0.0, 0.025, 1.0 - UV.x);
                if (ground > 0.0) {
                    // Espuma y ondas a los pies de la cascada, sobre el suelo del muro.
                    float width = abs(UV.x * 2.0 - 1.0);
                    float waves = pow(0.5 + 0.5 * sin(width * 22.0 - clock * 8.0 + UV.y * span * 3.0), 5.0);
                    float bubbles = grain(vec2(UV.y * span * 10.0, width * 6.0 - clock * 2.0));
                    float edge = (1.0 - smoothstep(0.65, 1.0, width))
                        * smoothstep(0.0, 0.025, UV.y) * smoothstep(0.0, 0.025, 1.0 - UV.y);
                    float foam = smoothstep(0.35, 0.8, bubbles) * (1.0 - width);
                    light = mix(vec3(0.08, 0.42, 0.70), vec3(0.80, 0.96, 1.0), foam + waves * 0.25);
                    alpha = (0.16 + foam * 0.5 + waves * 0.16) * edge;
                }
                if (fire > 0.0) {
                    vec2 flow = vec2(x * 5.0 + sin(y * 8.0 - clock * 2.0) * 0.45, y * 7.0 - clock * 3.0);
                    float turbulence = grain(flow) * 0.7 + grain(flow * 2.1) * 0.3;
                    float tongues = 0.40 + reinforcement * 0.045 + 0.33 * pow(0.5 + 0.5 * sin(x * 8.0 + sin(clock * 2.3 + x) * 0.7), 1.4);
                    float flame = smoothstep(-0.04, 0.075, tongues - y + (turbulence - 0.5) * 0.22);
                    float lift = stone > 0.0 ? smoothstep(0.36, 0.51, y) : 1.0;
                    float core = (1.0 - smoothstep(0.20, 0.78, y)) * turbulence;
                    vec3 heat = mix(vec3(1.0, 0.12, 0.015), vec3(1.0, 0.86, 0.35), core);
                    float a = flame * (0.48 + turbulence * 0.48) * fire * (0.12 + lift * 0.88);
                    light += heat * a; alpha += a;
                }
                if (water > 0.0 && ground < 0.5) {
                    float stream = pow(0.5 + 0.5 * sin(x * (18.0 + reinforcement * 5.0)
                        + sin(y * 4.0 + clock) * 0.5), 6.0 - reinforcement);
                    // UV.y crece hacia arriba: sumar tiempo hace que los reflejos bajen.
                    float flow = grain(vec2(x * 10.0, y * 18.0 + clock * 10.0));
                    float streak = pow(0.5 + 0.5 * sin(y * 38.0 + clock * 19.0 + x * 4.0), 12.0) * stream;
                    float foam = (1.0 - smoothstep(0.0, 0.13, y)) * (0.55 + flow * 0.45);
                    float lip = smoothstep(0.91, 0.98, y) * (0.3 + stream * 0.7);
                    float a = (0.34 + stream * 0.26 + streak * 0.16 + foam * 0.3 + lip * 0.14) * water;
                    light += mix(mix(vec3(0.06, 0.36, 0.72), water_color.rgb, mobile_contrast), vec3(0.78, 0.96, 1.0),
                        (stream * 0.28 + streak * 0.35 + foam * 0.55 + lip * 0.3) * (1.0 - mobile_contrast * 0.3)) * a;
                    alpha += a;
                }
                if (frost > 0.0) {
                    float crystal = abs(sin(x * 5.5 + y * 6.0)) * abs(sin(x * 8.0 - y * 11.0));
                    float rim = pow(1.0 - y, 6.0) + pow(y, 12.0);
                    float a = (0.14 + (1.0 - smoothstep(0.02, 0.085, crystal)) * 0.45 + rim * 0.3) * frost;
                    light += mix(vec3(0.46, 0.84, 1.0), frost_color.rgb, mobile_contrast) * a; alpha += a;
                }
                if (electric > 0.0) {
                    float tick = floor(clock * 12.0);
                    float path = 0.46 + sin(x * 4.5 + tick * 2.8) * 0.14
                        + sin(x * 17.0 - tick * 4.1) * 0.08;
                    float distance_to_bolt = abs(y - path);
                    float bolt = 1.0 - smoothstep(0.005, 0.021, distance_to_bolt);
                    float halo = (1.0 - smoothstep(0.02, 0.065, distance_to_bolt)) * 0.18;
                    float branch = (1.0 - smoothstep(0.004, 0.012, abs(y - path - sin(x * 8.0) * 0.23)))
                        * step(0.3, sin(x * 3.0 + tick));
                    float cell = floor(x * 2.8);
                    vec2 spark_at = vec2((cell + 0.15 + hash(vec2(cell, tick)) * 0.7) / 2.8,
                        0.2 + hash(vec2(tick, cell + 7.0)) * 0.6);
                    vec2 spark_delta = vec2(x - spark_at.x, (y - spark_at.y) * 2.0);
                    float spark = (1.0 - smoothstep(0.015, 0.055, length(spark_delta)))
                        + (1.0 - smoothstep(0.003, 0.015, min(abs(spark_delta.x), abs(spark_delta.y))))
                        * (1.0 - smoothstep(0.04, 0.12, length(spark_delta)));
                    spark *= 1.0 - smoothstep(0.12, 0.8, fract(clock * 12.0));
                    float second_path = 0.68 + sin(x * 6.5 - tick * 3.1) * 0.12;
                    float second_bolt = (1.0 - smoothstep(0.005, 0.018, abs(y - second_path))) * reinforcement * 0.45;
                    float a = (bolt + second_bolt + halo + branch * 0.65 + spark * (0.7 + reinforcement * 0.2)) * electric;
                    light += mix(mix(vec3(0.35, 0.50, 1.0), electric_color.rgb, mobile_contrast), vec3(0.90, 0.94, 1.0),
                        (bolt * 0.75 + spark * 0.2) * (1.0 - mobile_contrast * 0.65)) * a;
                    alpha += a;
                }
                if (steam > 0.0) {
                    float cloud = smoothstep(0.25, 0.75, grain(vec2(x * 2.7, y * 5.0 - clock * 1.4)));
                    float a = cloud * 0.45 * sin(y * 3.14159) * steam;
                    light += vec3(0.75, 0.86, 0.89) * a; alpha += a;
                }
                ALBEDO = ground > 0.0 ? light : light / max(alpha, 0.001);
                EMISSION = light * (fire > 0.0 || electric > 0.0 ? 0.40 : 0.16);
                ALPHA = min(alpha, 0.9) * (ground > 0.0 ? 1.0 : sides) * fade;
            }
            """ };
        _elementSkin = new ShaderMaterial { Shader = _elementShader };
        SetElementPalette(_elementSkin);
        _elementSkin.SetShaderParameter("span", length);
        _elementSkin.SetShaderParameter("stone", stone ? 1 : 0);
        _elementSkin.SetShaderParameter("reinforcement", Math.Max(0, Charges - 2));
        _elementSkin.SetShaderParameter("fire", recipe.Has(Element.Fire) && !recipe.Steam ? 1 : 0);
        _elementSkin.SetShaderParameter("water", recipe.Has(Element.Water) && !recipe.Steam && !recipe.Ice ? 1 : 0);
        _elementSkin.SetShaderParameter("frost", recipe.Has(Element.Cold) ? 1 : 0);
        _elementSkin.SetShaderParameter("electric", recipe.Has(Element.Lightning) ? 1 : 0);
        _elementSkin.SetShaderParameter("steam", recipe.Steam ? 1 : 0);
        using var face = new SurfaceTool(); face.Begin(Godot.Mesh.PrimitiveType.Triangles);
        const int segments = 24;
        for (int side = stone ? -1 : 1; side <= 1; side += 2)
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments, b = (i + 1) / (float)segments;
            void Vertex(float u, float v)
            {
                face.SetUV(new(u, v));
                float height = stone ? recipe.Has(Element.Fire) && !recipe.Steam ? 3.2f + _reinforcement * 0.7f
                    : 1.8f + _reinforcement * 0.76f
                    : 1.55f + _reinforcement * 0.18f + MathF.Sin(u * MathF.PI) * 0.65f;
                face.AddVertex(new(side * (stone ? StoneThickness / 2 + 0.025f : 0.03f),
                    0.08f + height * v, (u - 0.5f) * length));
            }
            Vertex(a, 0); Vertex(b, 0); Vertex(a, 1);
            Vertex(a, 1); Vertex(b, 0); Vertex(b, 1);
        }
        face.GenerateNormals(); Piece(face.Commit(), Vector3.Zero, _elementSkin);
        if (recipe.Has(Element.Water) && !recipe.Steam && !recipe.Ice)
        {
            _foamSkin = new ShaderMaterial { Shader = _elementShader };
            SetElementPalette(_foamSkin);
            _foamSkin.SetShaderParameter("ground", 1);
            _foamSkin.SetShaderParameter("water", 1);
            _foamSkin.SetShaderParameter("span", length);
            Piece(new PlaneMesh { Size = new(1.15f + Math.Max(0, Charges - 2) * 0.35f, length) }, new(0, 0.075f, 0), _foamSkin);
        }
        if (recipe.Has(Element.Cold))
        {
            _iceSkin = new StandardMaterial3D { AlbedoColor = new Color(Ui.IsMobile ? Ui.ElementColor(Element.Cold) : new Color("9cddf1"), 0.68f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.22f,
                EmissionEnabled = true, Emission = new("58afd1"), EmissionEnergyMultiplier = 0.18f };
            int count = Math.Clamp((int)MathF.Ceiling(length / 0.55f), 3, 12);
            for (int i = 0; i < count; i++)
            {
                float z = ((i + 0.5f) / count - 0.5f) * length;
                var crystal = Piece(new PrismMesh { Size = new(0.16f, 0.45f + i % 3 * 0.12f, 0.24f) },
                    new(0, stone ? 1.7f + _reinforcement * 0.76f : 1.55f + _reinforcement * 0.18f
                        + MathF.Sin((i + 0.5f) / count * MathF.PI) * 0.65f, z), _iceSkin);
                crystal.Rotation = new(0, i * 0.7f, MathF.Sin(i * 2.7f) * 0.12f);
            }
        }
    }
    private static void SetElementPalette(ShaderMaterial material)
    {
        material.SetShaderParameter("mobile_contrast", Ui.IsMobile ? 1 : 0);
        material.SetShaderParameter("water_color", Ui.ElementColor(Element.Water));
        material.SetShaderParameter("frost_color", Ui.ElementColor(Element.Cold));
        material.SetShaderParameter("electric_color", Ui.ElementColor(Element.Lightning));
    }

    private void UpdateElements(float fade, float time)
    {
        if (_elementSkin == null) return;
        _elementSkin.SetShaderParameter("clock", time);
        _elementSkin.SetShaderParameter("fade", fade);
        if (_foamSkin != null)
        {
            _foamSkin.SetShaderParameter("clock", time);
            _foamSkin.SetShaderParameter("fade", fade);
        }
        if (_iceSkin != null) _iceSkin.AlbedoColor = new(Ui.IsMobile ? Ui.ElementColor(Element.Cold) : new Color("9cddf1"), 0.68f * fade);
    }
}
