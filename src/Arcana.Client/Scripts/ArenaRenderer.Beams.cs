using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private Shader? _beamShader;

    private void BeamSegment(Vector3 from, Vector3 to, Color color, float radius, float endRadius = -1,
        int profile = 0, int charges = 1, Color? accent = null, float power = 1)
    {
        float length = from.DistanceTo(to);
        if (_beamUsed >= 512 || length < 0.05f) return;
        if (_beamUsed == _beamPool.Count)
        {
            _beamShader ??= new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
                uniform vec4 beam_color : source_color;
                uniform vec4 accent_color : source_color;
                uniform float span = 1.0;
                uniform float profile = 1.0;
                uniform float detail = 1.0;
                uniform float power = 1.0;
                uniform float clock = 0.0;
                void fragment() {
                    float along = UV.y * span;
                    float flow = along * 3.8 - clock * 7.0;
                    float spiral = UV.x * 12.56637 + along * 2.4 - clock * 3.0;
                    float pulse = pow(0.5 + 0.5 * sin(flow), 6.0);
                    float filament = pow(0.5 + 0.5 * sin(spiral), 10.0);
                    if (profile > 1.5 && profile < 2.5) {
                        pulse = pow(0.5 + 0.5 * sin(along * 2.7 + clock * 5.0), 10.0);
                    } else if (profile > 2.5 && profile < 3.5) {
                        filament *= 0.65 + 0.35 * sin(along * 8.0 - clock * 11.0);
                    } else if (profile > 3.5 && profile < 4.5) {
                        filament = smoothstep(0.80, 0.95, cos(along * 6.0 - clock * 3.0));
                    } else if (profile > 4.5) {
                        filament = pow(0.5 + 0.5 * sin(along * 12.0 - clock * 15.0), 14.0);
                    }
                    float ends = smoothstep(0.0, 0.055, UV.y) * smoothstep(0.0, 0.11, 1.0 - UV.y);
                    if (profile > 5.5) {
                        ALBEDO = beam_color.rgb;
                        EMISSION = ALBEDO * 0.2;
                        ALPHA = beam_color.a * ends;
                    } else {
                        ALBEDO = mix(beam_color.rgb, accent_color.rgb, filament * 0.55);
                        EMISSION = ALBEDO * (0.16 + pulse * 0.16) * (0.7 + clamp(power, 0.0, 1.5) * 0.2);
                        ALPHA = beam_color.a * ends * (0.58 + pulse * 0.25 + filament * (0.13 + detail * 0.025));
                    }
                }
                """ };
            var root = new Node3D(); AddChild(root);
            var shape = new CylinderMesh { RadialSegments = 10, Height = 1 };
            var mesh = Mesh(root, shape, Vector3.Zero, color, 0.35f);
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            mesh.RotationDegrees = new(90, 0, 0);
            _beamPool.Add(new(root, mesh, shape, new ShaderMaterial { Shader = _beamShader }));
        }
        var beam = _beamPool[_beamUsed++];
        beam.Root.Visible = true; beam.Root.Position = from; beam.Root.LookAt(to);
        // Cambiar Height reconstruía los vértices de cientos de cilindros por frame.
        // La escala conserva sus extremos, UV y radio; solo cambian los radios si lo pide el hechizo.
        beam.Mesh.Scale = new(1, length, 1);
        float bottom = endRadius < 0 ? radius : endRadius;
        if (beam.Radius != radius || beam.EndRadius != bottom)
        {
            if (beam.Radius != radius) { beam.Shape.TopRadius = radius; beam.Radius = radius; }
            if (beam.EndRadius != bottom) { beam.Shape.BottomRadius = bottom; beam.EndRadius = bottom; }
            BeamGeometryUpdates++;
        }
        beam.Mesh.Position = new(0, 0, -length / 2);
        Godot.Material material;
        if (profile == 0) material = Material(color, 0.25f, true);
        else
        {
            Color detailColor = accent ?? color;
            if (beam.Color != color) { beam.Skin.SetShaderParameter("beam_color", color); beam.Color = color; }
            if (beam.Accent != detailColor) { beam.Skin.SetShaderParameter("accent_color", detailColor); beam.Accent = detailColor; }
            if (beam.Span != length) { beam.Skin.SetShaderParameter("span", length); beam.Span = length; }
            if (beam.Profile != profile) { beam.Skin.SetShaderParameter("profile", profile); beam.Profile = profile; }
            if (beam.Charges != charges) { beam.Skin.SetShaderParameter("detail", charges); beam.Charges = charges; }
            if (beam.Power != power) { beam.Skin.SetShaderParameter("power", power); beam.Power = power; }
            beam.Skin.SetShaderParameter("clock", (float)_time);
            material = beam.Skin;
        }
        if (!ReferenceEquals(beam.Material, material)) { beam.Mesh.MaterialOverride = material; beam.Material = material; }
    }

    private void DrawBeam(BeamSnapshot beam, bool details)
    {
        var from = World(beam.From, 1); var to = World(beam.To, 1);
        if (from.DistanceSquaredTo(to) < 0.01f) return;
        var strength = SpellVisualStrength.For(beam.Elements);
        var direction = (to - from).Normalized(); var side = direction.Cross(Vector3.Up).Normalized();
        Color color = Ui.SpellColor(beam.Elements);
        if (beam.Shape == SpellShape.Lightning)
        {
            const int segments = 8; Vector3 previous = from;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var next = from.Lerp(to, t) + side * (i == segments ? 0
                    : Mathf.Sin((float)_time * 18 + i * 7 + beam.Owner) * (0.12f + strength.Charges * 0.07f));
                if (!details) BeamSegment(previous, next, color, 0.035f + strength.Charges * 0.012f);
                else
                {
                    BeamSegment(previous, next, color.Lightened(Ui.IsMobile ? 0.24f : 0.68f), 0.016f);
                    for (int branch = 0; branch < strength.LightningBranches; branch++)
                    {
                        if (i != 2 + branch * 2) continue;
                        var end = next + side * (branch % 2 == 0 ? 0.4f : -0.4f) + direction * 0.25f;
                        BeamSegment(next, end, new Color(color, 0.55f), 0.024f, 0.007f);
                    }
                }
                previous = next;
            }
        }
        else if (beam.Shape is SpellShape.Spray or SpellShape.Force)
        {
            // Render only once: the main renderer invokes a basic and detail pass.
            // Match the collision server sector (dot > 0.74 for spray, same rule for force).
            if (!details) return;
            bool spray = beam.Shape == SpellShape.Spray;
            float length = from.DistanceTo(to);
            float reach = length; // Includes range bonuses and any actual obstruction.
            if (reach <= 0.05f) return;
            float dotLimit = SpellBalance.ChannelConeDot;
            float halfAngle = Mathf.Acos(dotLimit);
            float edge = reach * Mathf.Sin(halfAngle);
            float forwardExtent = reach * Mathf.Cos(halfAngle);
            Color rim = beam.Elements.Contains(Element.Fire) ? new("ffd596")
                : beam.Elements.Contains(Element.Cold) ? new("dbf7ff")
                : beam.Elements.Contains(Element.Water) ? new("a9edff") : color.Lightened(0.30f);
            // Light interior plus an explicit arc; the rim makes the actual damage sector readable.
            var mist = new Color(color, spray ? 0.075f : 0.050f);
            BeamSegment(from, to, mist, 0.08f, Math.Max(0.10f, edge * 0.75f));
            Vector3 previous = from + direction * forwardExtent - side * edge;
            var edgeColor = new Color(rim, spray ? 0.43f : 0.27f);
            BeamSegment(from, previous, edgeColor, 0.018f, 0.029f, profile: 6);
            const int arcSections = 6;
            for (int i = 1; i <= arcSections; i++)
            {
                float angle = -halfAngle + (2f * halfAngle * i / arcSections);
                Vector3 next = from + direction * (reach * MathF.Cos(angle))
                    + side * (reach * MathF.Sin(angle));
                BeamSegment(previous, next, edgeColor, 0.021f, profile: 6);
                previous = next;
            }
            BeamSegment(previous, from, edgeColor, 0.029f, 0.018f, profile: 6);
            // Compact elemental ribbons inside the wedge.
            int strands = Ui.IsMobile ? strength.Charges : Math.Min(strength.SprayStrands, 6);
            for (int i = 0; i < strands; i++)
            {
                float t = Mathf.PosMod((float)_time * 0.75f + i / (float)strands, 1);
                float lateral = MathF.Sin(i * 3.7f + (float)_time * 5) * (t * edge * 0.55f);
                var p = from.Lerp(to, t) + side * lateral;
                BeamSegment(p, p + direction * 0.23f, new Color(rim, 0.50f),
                    0.012f + 0.03f * t, 0.006f, profile: 6);
            }
        }
        else
        {
            bool healing = beam.Elements.Contains(Element.Life);
            var primary = healing ? Ui.Health : Ui.ElementColor(Element.Arcane);
            var modifier = beam.Elements.FirstOrDefault(e => e is not Element.Arcane and not Element.Life,
                healing ? Element.Life : Element.Arcane);
            var accent = modifier == Element.Cold && !Ui.IsMobile ? new Color("80e5ff") : Ui.ElementColor(modifier);
            if (modifier is Element.Fire or Element.Cold or Element.Lightning)
                primary = primary.Lerp(accent, modifier == Element.Lightning ? 0.65f : 0.78f);
            int profile = modifier switch { Element.Fire => 3, Element.Cold => 4, Element.Lightning => 5, _ => healing ? 2 : 1 };
            float thickness = Mathf.Sqrt(Math.Clamp(beam.Power, SpellBalance.BeamMinimumDamage, 3));
            float charged = Math.Clamp((beam.Power - SpellBalance.BeamMinimumDamage) /
                (SpellBalance.BeamMaximumDamage - SpellBalance.BeamMinimumDamage), 0, 1);
            float radius = strength.BeamRadius * thickness;
            if (!details)
            {
                BeamSegment(from, to, new Color(primary, 0.38f + charged * 0.2f), radius,
                    profile: profile, charges: strength.Charges, accent: accent, power: beam.Power);
                var core = beam.Interaction == "merged" ? Ui.Text : primary.Lightened(Ui.IsMobile ? 0.18f : 0.48f);
                var tip = from.Lerp(to, 0.94f);
                float coreRadius = (0.026f + strength.Charges * 0.005f) * thickness;
                BeamSegment(from, tip, core, coreRadius, profile: 6);
                BeamSegment(tip, to, core, coreRadius, 0.003f, profile: 6);
                return;
            }
            var up = side.Cross(direction).Normalized();
            int strands = strength.Charges - 1;
            for (int strand = 0; strand < strands; strand++)
            {
                Vector3 previous = from;
                const int pieces = 6;
                for (int i = 1; i <= pieces; i++)
                {
                    float t = i / (float)pieces;
                    float angle = t * Mathf.Tau + (float)_time * 2.4f + strand * MathF.PI;
                    var offset = (side * MathF.Cos(angle) + up * MathF.Sin(angle))
                        * MathF.Sin(t * MathF.PI) * (0.08f + strength.Charges * 0.025f) * thickness;
                    if (modifier == Element.Lightning)
                        offset = side * MathF.Sin((float)_time * 17 + i * 7 + strand * 3) * MathF.Sin(t * MathF.PI) * 0.19f * thickness;
                    var next = from.Lerp(to, t) + offset;
                    var strandColor = modifier is Element.Fire or Element.Cold or Element.Lightning
                        ? strand == 0 ? accent : Ui.ElementColor(healing ? Element.Life : Element.Arcane)
                        : primary;
                    BeamSegment(previous, next, new Color(strandColor, 0.45f + charged * 0.21f), 0.016f * thickness, profile: 6);
                    previous = next;
                }
            }
            for (int i = 0; i < strength.Charges; i++)
            {
                float t = 0.08f + Mathf.PosMod((float)_time * (healing ? 0.65f : 0.9f) + i / (float)strength.Charges, 1) * 0.82f;
                var at = from.Lerp(to, t);
                // Pulsos pequeños conservan la dirección real del canal y el color del modificador.
                var packetColor = modifier is Element.Cold ? accent.Lightened(0.45f) : accent;
                BeamSegment(at, at + direction * 0.27f, new Color(packetColor, 0.56f + charged * 0.2f),
                    0.035f * thickness, 0.013f, profile: 6);
                if (modifier is Element.Fire or Element.Cold or Element.Lightning)
                {
                    var start = at + side * (i % 2 == 0 ? radius * 1.2f : -radius * 1.2f);
                    BeamSegment(start, start + direction * 0.16f + up * 0.06f, new Color(accent, 0.3f + charged * 0.2f),
                        0.022f * thickness, 0.005f, profile: 6);
                }
            }
        }
    }
}
