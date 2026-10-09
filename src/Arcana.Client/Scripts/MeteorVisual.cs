using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Caída visual sincronizada con los 1,4 s de aviso del servidor y su radio real.</summary>
public partial class MeteorVisual : Node3D
{
    private static Shader? _rockShader, _tailShader, _warningShader;
    private readonly Node3D _flight = new();
    private readonly Node3D _plume = new();
    private MeshInstance3D _rock = null!;
    private ShaderMaterial _rockSkin = null!, _tail = null!, _core = null!, _warning = null!;
    private float _remaining;
    internal float Height => _flight.Position.Y;
    internal float Radius { get; private set; }

    internal static Vector3 FlightPosition(float lifetime)
    {
        float remaining = Math.Clamp((lifetime - (SpellBalance.MeteorLifetime - SpellBalance.MeteorDelay)) / SpellBalance.MeteorDelay, 0, 1);
        float progress = 1 - remaining;
        return new(-2.4f * remaining, 0.65f + 12 * (1 - progress * progress), -1.6f * remaining);
    }

    public void Build(FieldSnapshot field)
    {
        Position = ArenaRenderer.World(field.Position); Radius = field.Radius; _remaining = field.Lifetime;
        _rockShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode cull_disabled;
            uniform float clock = 0.0;
            varying vec3 local_position;
            void vertex() { local_position = VERTEX; }
            void fragment() {
                vec3 p = local_position * 7.0;
                vec3 warped = p + sin(p.yzx * 1.7) * 0.28;
                float seams = abs(sin(warped.x + warped.z * 0.47) * sin(warped.y - warped.z * 0.63));
                float cracks = 1.0 - smoothstep(0.015, 0.085, seams);
                float grain = 0.5 + 0.5 * sin(p.x * 5.7 + sin(p.y * 6.1) + p.z * 4.3);
                vec3 basalt = mix(vec3(0.065, 0.07, 0.075), vec3(0.24, 0.15, 0.10), grain * 0.75);
                vec3 heat = mix(vec3(1.0, 0.13, 0.018), vec3(1.0, 0.78, 0.28), cracks * cracks);
                ALBEDO = mix(basalt, heat, cracks * 0.74);
                ROUGHNESS = 0.96;
                EMISSION = heat * cracks * (0.8 + sin(clock * 5.0 + p.y) * 0.12);
            }
            """ };
        _tailShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
            uniform float clock = 0.0;
            uniform float tail_span = 4.8;
            uniform float core = 0.0;
            varying float height;
            float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
            float grain(vec2 p) {
                vec2 cell = floor(p);
                vec2 f = fract(p);
                f = f * f * (3.0 - 2.0 * f);
                return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
                    mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
            }
            void vertex() { height = clamp((VERTEX.y + tail_span * 0.5) / tail_span, 0.0, 1.0); }
            void fragment() {
                float taper = 1.0 - height;
                float ribbons = grain(vec2(UV.x * 12.0 + sin(height * 8.0 - clock * 2.0), height * 10.0 - clock * 6.0));
                float wisps = smoothstep(0.2 + height * 0.28, 0.76, ribbons);
                vec3 heat = mix(vec3(1.0, 0.13, 0.015), vec3(1.0, 0.74, 0.25), taper * 0.85);
                ALBEDO = mix(heat, vec3(1.0, 0.94, 0.67), core * pow(taper, 0.55));
                EMISSION = ALBEDO * (0.38 + ribbons * 0.2);
                ALPHA = pow(taper, 0.72) * (0.28 + wisps * 0.48) * smoothstep(0.0, 0.07, height)
                    * mix(wisps, 0.85, core);
            }
            """ };
        _warningShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
            uniform float progress = 0.0;
            uniform float clock = 0.0;
            void fragment() {
                vec2 p = UV * 2.0 - 1.0;
                float radius = length(p);
                float angle = atan(p.y, p.x);
                float rim = (1.0 - smoothstep(0.016, 0.036, abs(radius - 0.96)));
                float sweep = (1.0 - smoothstep(0.015, 0.035, abs(radius - (0.18 + progress * 0.68)))) * 0.5;
                float marks = step(0.72, cos(angle * 12.0 - clock * 0.8)) * smoothstep(0.72, 0.88, radius) * (1.0 - smoothstep(0.9, 0.96, radius));
                ALBEDO = vec3(1.0, 0.37 + progress * 0.18, 0.11);
                EMISSION = ALBEDO * 0.2;
                ALPHA = (rim * 0.75 + sweep * 0.36 + marks * 0.55 + (1.0 - smoothstep(0.0, 1.0, radius)) * 0.08)
                    * (0.55 + progress * 0.45);
            }
            """ };
        _warning = new ShaderMaterial { Shader = _warningShader };
        Piece(this, new PlaneMesh { Size = Vector2.One * field.Radius * 2 }, new(0, 0.055f, 0), _warning);
        AddChild(_flight);
        _rockSkin = new ShaderMaterial { Shader = _rockShader };
        _rock = Piece(_flight, RockMesh(), Vector3.Zero, _rockSkin);
        _rock.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        _flight.AddChild(_plume);
        _tail = new ShaderMaterial { Shader = _tailShader };
        Piece(_plume, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.81f, Height = 4.8f, RadialSegments = 16 },
            new(0, 2.98f, 0), _tail);
        _core = new ShaderMaterial { Shader = _tailShader };
        _core.SetShaderParameter("core", 1);
        _core.SetShaderParameter("tail_span", 3.1f);
        Piece(_plume, new CylinderMesh { TopRadius = 0.015f, BottomRadius = 0.47f, Height = 3.1f, RadialSegments = 12 },
            new(0, 1.91f, 0), _core);
        _flight.Position = FlightPosition(field.Lifetime);
        AlignPlume(field.Lifetime);
    }

    private static ArrayMesh RockMesh()
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] points = [new(-1,t,0),new(1,t,0),new(-1,-t,0),new(1,-t,0),new(0,-1,t),new(0,1,t),
            new(0,-1,-t),new(0,1,-t),new(t,0,-1),new(t,0,1),new(-t,0,-1),new(-t,0,1)];
        int[] triangles = [0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
            3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1];
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        Vector3 Rough(Vector3 point)
        {
            Vector3 p = point.Normalized();
            float radius = 0.77f + 0.085f * MathF.Sin(p.X * 7.1f + p.Y * 13.2f + p.Z * 9.3f)
                + 0.035f * MathF.Sin(p.X * 19.1f - p.Y * 5.2f + p.Z * 11.3f);
            return p * radius * new Vector3(1, 1.12f, 0.94f);
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            a = Rough(a); b = Rough(b); c = Rough(c);
            surface.SetNormal((b - a).Cross(c - a).Normalized());
            // Los índices del icosaedro son antihorarios: invertirlos para la cara exterior de Godot.
            surface.AddVertex(a); surface.AddVertex(c); surface.AddVertex(b);
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = points[triangles[i]].Normalized(), b = points[triangles[i + 1]].Normalized(),
                c = points[triangles[i + 2]].Normalized();
            Vector3 ab = (a + b).Normalized(), bc = (b + c).Normalized(), ca = (c + a).Normalized();
            Triangle(a, ab, ca); Triangle(ab, b, bc); Triangle(ca, bc, c); Triangle(ab, bc, ca);
        }
        return surface.Commit();
    }

    private static MeshInstance3D Piece(Node3D root, Godot.Mesh mesh, Vector3 position, Material material)
    {
        var piece = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(piece); return piece;
    }

    public void Update(FieldSnapshot field, float delta, float time)
    {
        _remaining = Mathf.MoveToward(_remaining, field.Lifetime, delta * 1.2f);
        _flight.Position = FlightPosition(_remaining);
        AlignPlume(_remaining);
        _rock.Rotation += new Vector3(delta * 2.1f, delta * 1.3f, delta * 0.7f);
        _rockSkin.SetShaderParameter("clock", time);
        _tail.SetShaderParameter("clock", time);
        _core.SetShaderParameter("clock", time);
        _warning.SetShaderParameter("clock", time);
        _warning.SetShaderParameter("progress", Math.Clamp((SpellBalance.MeteorLifetime - _remaining) / SpellBalance.MeteorDelay, 0, 1));
        Visible = field.Lifetime > 0;
    }

    internal static Vector3 TrailDirection(float lifetime)
    {
        float progress = Math.Clamp((SpellBalance.MeteorLifetime - lifetime) / SpellBalance.MeteorDelay, 0, 1);
        float descent = 24 * Math.Max(progress, 0.18f);
        return new Vector3(-2.4f, descent, -1.6f).Normalized();
    }

    private void AlignPlume(float lifetime)
    {
        var direction = TrailDirection(lifetime);
        _plume.Rotation = new(MathF.Atan2(direction.Z, direction.Y), 0, -MathF.Atan2(direction.X, direction.Y));
    }
}
