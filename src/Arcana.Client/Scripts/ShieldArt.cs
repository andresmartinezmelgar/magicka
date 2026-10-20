using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Estilo B: materiales GPU compartidos para escudos y runas de campo.
/// El servidor conserva vida, colisiones, radio y duración: aquí solo hay dibujo.
/// </summary>
internal static class ShieldArt
{
    private static Shader? _bubbleShader;
    private static Shader? _groundShader;

    internal static ShaderMaterial Bubble()
    {
        _bubbleShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_back, blend_mix, depth_draw_never, shadows_disabled;
            uniform vec4 accent : source_color = vec4(1.0, 0.8, 0.15, 1.0);
            uniform vec4 secondary : source_color = vec4(0.45, 0.8, 1.0, 1.0);
            uniform float pattern = 0.0;
            uniform float intensity = 1.0;
            uniform float layers = 1.0;
            uniform float translucency = 0.22;
            float line(float x, float width) {
                return 1.0 - smoothstep(width, width * 2.5, abs(x));
            }
            void fragment() {
                vec2 p = UV;
                float t = TIME;
                float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 2.5);
                // Trama mágica con costuras móviles: no añade geometría ni partículas.
                float a = line(sin(p.x * 37.0 + sin(p.y * 23.0) * 0.8 + t * 0.55), 0.065);
                float b = line(sin(p.y * 31.0 - p.x * 7.0 + t * 0.32), 0.07);
                float lattice = max(a, b) * 0.35;
                float moving = 0.0;
                if (pattern < 0.5) {
                    moving = line(sin((p.x + p.y) * 25.0 + t * 1.7), 0.10);
                } else if (pattern < 1.5) {
                    // Vida: ondas que ascienden y filamentos orgánicos.
                    moving = line(sin(p.y * 35.0 - t * 2.5 + sin(p.x * 16.0) * 2.0), 0.19);
                } else if (pattern < 2.5) {
                    // Frío: geometría radial y facetas cristalinas.
                    moving = max(line(sin(p.x * 64.0), 0.09), line(sin((p.x - p.y) * 38.0), 0.07));
                } else if (pattern < 3.5) {
                    // Arcano: aros oscilantes de sigilos.
                    moving = line(sin(p.y * 47.0 + sin(p.x * 20.0 - t) * 2.5), 0.09);
                } else if (pattern < 4.5) {
                    // Fuego: lenguas ascendentes.
                    moving = line(sin(p.y * 32.0 - t * 5.0 + sin(p.x * 29.0 + t) * 2.0), 0.22);
                } else if (pattern < 5.5) {
                    // Tierra: placas facetadas, fracturas.
                    moving = max(line(sin(p.x * 23.0 + p.y * 34.0), 0.08), line(sin(p.x * 32.0 - p.y * 18.0), 0.07));
                } else if (pattern < 6.5) {
                    // Rayo: descarga eléctrica irregular, sin destellos de pantalla.
                    moving = line(sin(p.y * 54.0 + sin(p.x * 38.0 + floor(t * 8.0)) * 3.0), 0.13);
                } else {
                    // Agua: corrientes circulares y reflejos.
                    moving = line(sin(p.y * 27.0 + sin(p.x * 11.0 + t) * 3.0 - t * 2.8), 0.14);
                }
                float sheen = 0.50 + 0.50 * sin(p.x * 12.0 + p.y * 17.0 + t * 1.8);
                float detail = clamp(lattice + moving * 0.38 + rim * 0.65, 0.0, 1.0);
                vec3 ink = mix(accent.rgb, secondary.rgb, moving * 0.45 + sheen * 0.18);
                ALBEDO = ink * (0.55 + detail * 0.45);
                EMISSION = ink * (0.18 + detail * 0.38) * intensity;
                ALPHA = clamp(translucency * (0.36 + rim * 1.3 + detail * 0.85)
                    * (0.87 + 0.13 * layers), 0.02, 0.78);
            }
            """ };
        return new ShaderMaterial { Shader = _bubbleShader };
    }

    internal static void UpdateBubble(ShaderMaterial skin, Element[] elements, Color color,
        float integrity, int charges, bool frozen, bool invulnerable)
    {
        int pattern = frozen ? 2 : invulnerable ? 0
            : elements.Contains(Element.Life) ? 1
            : elements.Contains(Element.Cold) ? 2
            : elements.Contains(Element.Arcane) ? 3
            : elements.Contains(Element.Fire) ? 4
            : elements.Contains(Element.Earth) ? 5
            : elements.Contains(Element.Lightning) ? 6
            : elements.Contains(Element.Water) ? 7 : 0;
        Color second = elements.Contains(Element.Water) && elements.Contains(Element.Life)
            ? Ui.ElementColor(Element.Water)
            : color.Lightened(0.55f);
        skin.SetShaderParameter("accent", new Color(color.R, color.G, color.B));
        skin.SetShaderParameter("secondary", second);
        skin.SetShaderParameter("pattern", (float)pattern);
        skin.SetShaderParameter("intensity", Math.Clamp(integrity, 0.12f, 1.0f));
        skin.SetShaderParameter("layers", (float)Math.Clamp(charges, 1, 3));
        skin.SetShaderParameter("translucency", color.A);
    }

    /// <summary>Runas circulares sobre el suelo. Su tamaño sigue exactamente el radio del servidor.</summary>
    internal static (MeshInstance3D Mesh, ShaderMaterial Material) GroundSigil(Node3D root,
        float radius, Color color)
    {
        _groundShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never, shadows_disabled;
            uniform vec4 accent : source_color;
            uniform float armed = 0.0;
            float stroke(float distance, float width) {
                return 1.0 - smoothstep(width, width * 2.0, abs(distance));
            }
            void fragment() {
                vec2 xy = UV * 2.0 - 1.0;
                float r = length(xy);
                if (r > 0.995) discard;
                float theta = atan(xy.y, xy.x);
                float outer = stroke(r - 0.93, 0.012);
                float middle = stroke(r - 0.75, 0.008);
                float inner = stroke(r - 0.36, 0.009);
                float ticks = stroke(sin(theta * 12.0), 0.10) * stroke(r - 0.83, 0.06);
                float glyph = stroke(sin(theta * 8.0 + TIME * 0.32), 0.08) * stroke(r - 0.54, 0.016);
                float pulse = 0.75 + 0.25 * sin(TIME * (armed > 0.5 ? 3.0 : 1.2));
                float strength = outer * 0.8 + middle * 0.6 + inner * 0.45 + ticks * 0.52 + glyph * 0.35;
                ALBEDO = accent.rgb;
                EMISSION = accent.rgb * strength * 0.6;
                ALPHA = clamp(strength * pulse * (armed > 0.5 ? 0.47 : 0.28), 0.0, 0.6);
            }
            """ };
        var material = new ShaderMaterial { Shader = _groundShader };
        material.SetShaderParameter("accent", color);
        var mesh = new MeshInstance3D
        {
            Name = "StyleBGroundSigil",
            Mesh = new PlaneMesh { Size = new(2, 2) },
            MaterialOverride = material,
            Position = new(0, 0.016f, 0),
            Scale = new(radius, 1, radius),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        root.AddChild(mesh);
        return (mesh, material);
    }
}
