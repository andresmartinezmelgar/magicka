using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Geometría compartida: una superficie por símbolo, sin texturas, luces ni ruido por píxel.</summary>
internal static class AreaVisuals
{
    internal enum Pattern { Fire, Water, Frost, Mine, HealingMine, MineTrigger, HealingTrigger,
        FireAura, LightningAura, RegenerationAura, Berserker, FrostPulse, Impact, LightningLink }

    private static readonly Dictionary<Pattern, ArrayMesh> Shapes = [];
    private static readonly Dictionary<Color, ShaderMaterial> Skins = [];
    private static Shader? _shader;
    private static readonly Color Ember = new("ff6b28"), Gold = new("ffd78b"), Water = new("56bcff"),
        Ice = new("b6f1ff"), Electric = new("c4b5ff"), Green = new("65edbb"), Rage = new("ff705d");

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
                float pulse = sin(TIME * UV.y + UV.x);
                ink = vec4(COLOR.rgb, COLOR.a * (0.84 + pulse * 0.16));
                VERTEX.y += pulse * UV2.x;
                VERTEX.xz *= 1.0 + pulse * UV2.y;
            }
            void fragment() {
                ALBEDO = ink.rgb * tint.rgb;
                EMISSION = ALBEDO * 0.18;
                ALPHA = ink.a * tint.a;
            }
            """ };
        skin = new ShaderMaterial { Shader = _shader };
        skin.SetShaderParameter("tint", color); Skins[color] = skin; return skin;
    }

    internal static Pattern GroundPattern(string kind) => kind switch
    {
        "burning_ground" => Pattern.Fire, "frost_ground" => Pattern.Frost, "water_pool" => Pattern.Water,
        "healing_mine" => Pattern.HealingMine, _ => Pattern.Mine
    };
    internal static Pattern AuraPattern(int index) => index switch
    { 0 => Pattern.FireAura, 1 => Pattern.LightningAura, 2 => Pattern.RegenerationAura, _ => Pattern.Berserker };

    internal static MeshInstance3D Piece(Node3D root, Pattern pattern, Color? tint = null)
    {
        var piece = new MeshInstance3D { Mesh = Shape(pattern), MaterialOverride = Skin(tint),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 0.06f };
        root.AddChild(piece); return piece;
    }

    internal static ArrayMesh Shape(Pattern pattern)
    {
        if (Shapes.TryGetValue(pattern, out var shape)) return shape;
        using var b = new Ink();
        b.Speed = pattern switch { Pattern.RegenerationAura => 1.7f, Pattern.LightningAura => 8,
            Pattern.Berserker => 5, _ => 3.2f };
        bool ground = pattern is Pattern.Fire or Pattern.Water or Pattern.Frost or Pattern.Mine or Pattern.HealingMine;
        if (ground)
            b.Disc(new Color("08111c", 0.48f), new Color("101c2a", 0.12f));
        switch (pattern)
        {
            case Pattern.Fire:
            case Pattern.FireAura:
                b.Ring(0.978f, 1, new(Ember, 0.8f));
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.Tau / 12;
                    b.Arc(0.88f, 0.91f, a, a + 0.28f, new(Gold, 0.65f), 3);
                    b.Flame(a + 0.12f, 0.93f, new(Ember, 0.8f));
                    if (ground)
                    {
                        var p = At(0.33f + i % 3 * 0.16f, a);
                        b.Line(p * 0.5f, p, 0.018f, new(Ember, 0.6f));
                        b.Line(p, At(0.7f, a + 0.22f), 0.011f, new(Gold, 0.6f));
                    }
                }
                break;
            case Pattern.Water:
                b.Ring(0.976f, 1, new(Water, 0.8f));
                for (int i = 0; i < 3; i++)
                {
                    float r = 0.3f + i * 0.22f;
                    b.RadialMotion = 0.045f;
                    b.Arc(r, r + 0.025f, i * 1.9f, i * 1.9f + 4.7f, new(Water.Lightened(i * 0.14f), 0.55f), 32);
                    b.RadialMotion = 0;
                    b.Drop(At(0.8f, i * Mathf.Tau / 3), i * Mathf.Tau / 3, new(Ice, 0.85f));
                }
                break;
            case Pattern.Frost:
            case Pattern.FrostPulse:
                b.Ring(0.975f, 1, new(Ice, 0.85f));
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.Tau / 6;
                    var direction = At(1, a); var side = At(1, a + Mathf.Pi / 2);
                    b.Line(direction * 0.14f, direction * 0.85f, 0.025f, new(Ice, 0.8f));
                    for (int j = 0; j < 2; j++)
                    {
                        var fork = direction * (0.4f + j * 0.23f);
                        b.Line(fork, fork - direction * 0.13f + side * 0.13f, 0.02f, new(Ice, 0.7f));
                        b.Line(fork, fork - direction * 0.13f - side * 0.13f, 0.02f, new(Ice, 0.7f));
                    }
                    b.Triangle(At(0.94f, a - 0.08f), At(0.8f, a), At(0.94f, a + 0.08f), new(Ice, 0.7f));
                    if (ground)
                        b.Triangle(At(0.22f, a), At(0.74f, a + 0.1f), At(0.75f, a + 0.72f), new(Water, 0.18f));
                }
                break;
            case Pattern.LightningAura:
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.Tau / 12;
                    b.Arc(0.98f, 1, a, a + 0.18f, new(Electric, 0.8f), 3);
                    var p = At(0.74f, a); var q = At(0.86f, a + 0.13f);
                    var r = At(0.72f, a + 0.24f); var s = At(0.84f, a + 0.43f);
                    b.Line(p, q, 0.025f, new(Electric, 0.9f));
                    b.Line(q, r, 0.025f, new(Electric, 0.9f));
                    b.Line(r, s, 0.025f, new(Ice, 0.9f));
                }
                break;
            case Pattern.RegenerationAura:
                b.Ring(0.988f, 1, new(Green, 0.6f));
                b.Ring(0.55f, 0.57f, new(Green, 0.5f));
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.Tau / 4;
                    b.Cross(At(0.68f, a), 0.09f, new(Green, 0.9f));
                    b.Leaf(At(0.5f, a + 0.3f), a + 0.9f, new(Green, 0.75f));
                    b.Leaf(At(0.5f, a - 0.3f), a - 0.9f, new(Green, 0.75f));
                }
                break;
            case Pattern.Berserker:
                for (int i = 0; i < 3; i++)
                {
                    float a = i * Mathf.Tau / 3;
                    b.Arc(0.91f, 0.96f, a, a + 1.2f, new(Rage, 0.85f), 12);
                    for (int j = 0; j < 3; j++)
                    {
                        float slash = a + 0.3f + j * 0.22f;
                        b.Triangle(At(0.43f, slash - 0.08f), At(0.88f, slash + 0.09f),
                            At(0.61f, slash + 0.08f), new(j == 1 ? Gold : Rage, 0.95f));
                    }
                }
                break;
            case Pattern.Mine:
            case Pattern.HealingMine:
                b.Ring(0.97f, 1, new(Colors.White, 0.8f));
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Tau / 8;
                    b.Arc(0.85f, 0.88f, a, a + 0.47f, new(Colors.White, 0.65f), 4);
                    if (pattern == Pattern.HealingMine) b.Cross(At(0.67f, a), 0.065f, new(Colors.White, 0.8f));
                    else b.Triangle(At(0.77f, a - 0.065f), At(0.6f, a), At(0.77f, a + 0.065f), new(Colors.White, 0.85f));
                }
                break;
            case Pattern.MineTrigger:
            case Pattern.HealingTrigger:
                b.Ring(0.97f, 1, new(Colors.White, 0.8f));
                if (pattern == Pattern.HealingTrigger) b.Cross(Vector3.Zero, 0.26f, new(Colors.White, 0.95f));
                else
                {
                    b.Line(new(0, 0, -0.32f), new(0, 0, 0.1f), 0.12f, new(Colors.White, 0.95f));
                    b.Cross(new(0, 0, 0.29f), 0.05f, new(Colors.White, 0.95f));
                }
                // Un núcleo facetado y el círculo de activación comparten la misma superficie.
                for (int i = 0; i < 4; i++)
                {
                    var a = At(0.19f, i * Mathf.Pi / 2); var c = At(0.19f, (i + 1) * Mathf.Pi / 2);
                    b.Triangle(a + Vector3.Up * 0.23f, new(0, 0.65f, 0), c + Vector3.Up * 0.23f,
                        new(Colors.White, i % 2 == 0 ? 0.65f : 0.95f));
                }
                break;
            case Pattern.Impact:
                b.Ring(0.966f, 1, new(Gold, 0.8f));
                for (int i = 0; i < 10; i++)
                {
                    float a = i * Mathf.Tau / 10;
                    var fork = At(0.42f + i % 3 * 0.08f, a + 0.12f);
                    b.Line(At(0.12f, a), fork, 0.025f, new(Ember, 0.85f));
                    b.Line(fork, At(0.87f, a - 0.06f), 0.016f, new(Gold, 0.8f));
                    b.Line(fork, At(0.69f, a + 0.29f), 0.012f, new(Ember, 0.8f));
                }
                break;
            case Pattern.LightningLink:
                Vector3[] points = [new(0,0,0), new(0.16f,0,0.07f), new(0.31f,0,-0.065f),
                    new(0.47f,0,0.04f), new(0.55f,0,-0.055f), new(0.74f,0,0.06f), new(0.88f,0,-0.04f), new(1,0,0)];
                for (int i = 1; i < points.Length; i++) b.Line(points[i-1], points[i], 0.045f, Colors.White);
                break;
        }
        shape = b.Finish(); Shapes[pattern] = shape; return shape;
    }

    private static Vector3 At(float radius, float angle) => new(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);

    private sealed class Ink : IDisposable
    {
        private readonly SurfaceTool _surface = new();
        internal float Speed = 3.2f, RadialMotion;
        internal Ink() => _surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        private void Vertex(Vector3 point, Color color, float lift = 0)
        {
            _surface.SetNormal(Vector3.Up); _surface.SetColor(color);
            _surface.SetUV(new(point.X * 7 + point.Z * 11, Speed)); _surface.SetUV2(new(lift, RadialMotion));
            _surface.AddVertex(point);
        }
        internal void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, float lift = 0)
        { Vertex(a, color, lift); Vertex(b, color, lift); Vertex(c, color, lift); }
        internal void Disc(Color center, Color edge)
        {
            for (int i = 0; i < 48; i++)
            { Vertex(Vector3.Zero, center); Vertex(At(0.99f, i * Mathf.Tau / 48), edge); Vertex(At(0.99f, (i+1) * Mathf.Tau / 48), edge); }
        }
        internal void Ring(float inner, float outer, Color color) => Arc(inner, outer, 0, Mathf.Tau, color, 64);
        internal void Arc(float inner, float outer, float start, float end, Color color, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.Lerp(start, end, i / (float)segments), b = Mathf.Lerp(start, end, (i+1) / (float)segments);
                Triangle(At(inner, a), At(outer, a), At(outer, b), color);
                Triangle(At(inner, a), At(outer, b), At(inner, b), color);
            }
        }
        internal void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            var side = new Vector3(-(b-a).Z, 0, (b-a).X).Normalized() * width * 0.5f;
            Triangle(a-side, a+side, b+side, color); Triangle(a-side, b+side, b-side, color);
        }
        internal void Cross(Vector3 at, float radius, Color color)
        {
            Line(at - Vector3.Right * radius, at + Vector3.Right * radius, radius * 0.6f, color);
            Line(at - Vector3.Back * radius, at + Vector3.Back * radius, radius * 0.6f, color);
        }
        internal void Flame(float angle, float radius, Color color)
        {
            var at = At(radius, angle); var side = At(0.055f, angle + Mathf.Pi / 2);
            Triangle(at-side, at + Vector3.Up * 0.48f + At(0.035f, angle + 0.7f), at+side, color, 0.04f);
            Triangle(at-side*0.45f, at + Vector3.Up * 0.28f, at+side*0.45f, new(Gold, 0.85f), 0.025f);
        }
        internal void Drop(Vector3 at, float angle, Color color)
        {
            var side = At(0.05f, angle + Mathf.Pi / 2); var tip = At(0.12f, angle);
            Triangle(at-side, at+tip, at+side, color);
            Triangle(at-side, at+side, at-tip*0.5f, color);
        }
        internal void Leaf(Vector3 at, float angle, Color color)
        {
            var tip = At(0.1f, angle); var side = At(0.04f, angle + Mathf.Pi / 2);
            Triangle(at-tip, at+side, at+tip, color); Triangle(at-tip, at+tip, at-side, color);
        }
        internal ArrayMesh Finish() => _surface.Commit();
        public void Dispose() => _surface.Dispose();
    }
}
