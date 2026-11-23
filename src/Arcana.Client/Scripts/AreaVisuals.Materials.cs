using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal static partial class AreaVisuals
{
    internal static ShaderMaterial Skin(Color? tint = null)
    {
        var color = tint ?? Colors.White;
        if (Skins.TryGetValue(color, out var skin)) return skin;
        _shader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
            uniform vec4 tint : source_color = vec4(1.0);
            varying vec4 ink;
            void vertex() {
                float pulse = sin(TIME * UV.y + dot(VERTEX.xz, vec2(7.0, 11.0)));
                ink = vec4(COLOR.rgb, COLOR.a * (0.84 + pulse * 0.16));
                VERTEX.y += pulse * UV2.x;
                VERTEX.xz *= 1.0 + pulse * UV2.y;
            }
            void fragment() {
                float edge = abs(UV.x);
                float feather = max(fwidth(UV.x), 0.08);
                float core = 1.0 - smoothstep(0.18, 0.28 + feather, edge);
                float glow = (1.0 - smoothstep(0.0, 1.0, edge)) * 0.14;
                float stroke = mix(1.0, min(1.0, core * 0.86 + glow), 1.0 - step(1.5, edge));
                ALBEDO = ink.rgb * tint.rgb;
                EMISSION = ALBEDO * 0.28;
                ALPHA = ink.a * tint.a * stroke;
            }
            """ };
        skin = new ShaderMaterial { Shader = _shader };
        skin.SetShaderParameter("tint", color); Skins[color] = skin; return skin;
    }

    internal static ShaderMaterial WarningSkin()
    {
        _warningShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
            uniform float progress = 0.0;
            void fragment() {
                vec2 p = UV * 2.0 - 1.0;
                float radius = length(p);
                float pixel = max(fwidth(radius), 0.0005);
                float rim = 1.0 - smoothstep(pixel * 0.35, pixel * 1.25, abs(radius - 0.985));
                float halo = (1.0 - smoothstep(0.0, 0.045, abs(radius - 0.97))) * 0.12;
                float timer_radius = 0.18 + progress * 0.76;
                float timer = 1.0 - smoothstep(pixel * 0.3, pixel, abs(radius - timer_radius));
                float haze = (1.0 - smoothstep(0.15, 0.98, radius)) * 0.035;
                float pulse = 0.82 + sin(TIME * (2.2 + progress * 2.6)) * 0.18;
                float mask = 1.0 - smoothstep(0.995, 1.0, radius);
                ALBEDO = mix(vec3(1.0, 0.19, 0.055), vec3(1.0, 0.68, 0.28), timer * 0.8 + progress * 0.12);
                EMISSION = ALBEDO * (rim * (0.25 + pulse * 0.16) + timer * 0.18);
                ALPHA = min(0.95, rim * (0.86 + progress * 0.08) + timer * (0.5 + progress * 0.16)
                    + (halo + haze) * (0.75 + pulse * 0.25)) * mask;
            }
            """ };
        return new ShaderMaterial { Shader = _warningShader, RenderPriority = 8 };
    }

    internal static Pattern GroundPattern(string kind) => kind switch
    {
        "burning_ground" => Pattern.Fire, "frost_ground" => Pattern.Frost, "water_pool" or "equipment_water_pool" => Pattern.Water,
        "healing_mine" => Pattern.HealingMine, _ => Pattern.Mine
    };

    internal static Pattern AuraPattern(int index) => index switch
    { 0 => Pattern.FireAura, 1 => Pattern.LightningAura, 2 => Pattern.RegenerationAura,
        3 => Pattern.ArcaneAura, 4 => Pattern.WaterAura, _ => Pattern.Berserker };

    internal static MeshInstance3D Piece(Node3D root, Pattern pattern, Color? tint = null)
    {
        var piece = new MeshInstance3D { Mesh = Shape(pattern), MaterialOverride = Skin(tint),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 0.06f };
        root.AddChild(piece); return piece;
    }
}
