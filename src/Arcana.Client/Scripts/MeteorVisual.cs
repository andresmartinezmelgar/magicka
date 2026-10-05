using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Caída visual sincronizada con los 1,4 s de aviso del servidor y su radio real.</summary>
public partial class MeteorVisual : Node3D
{
    private static Shader? _rockShader, _tailShader, _warningShader;
    private readonly Node3D _flight = new();
    private MeshInstance3D _rock = null!;
    private ShaderMaterial _tail = null!, _warning = null!;
    private float _remaining;
    internal float Height => _flight.Position.Y;
    internal float Radius { get; private set; }

    internal static Vector3 FlightPosition(float lifetime)
    {
        float remaining = Math.Clamp((lifetime - (SpellBalance.MeteorLifetime - SpellBalance.MeteorDelay)) / SpellBalance.MeteorDelay, 0, 1);
        return new(-2.4f * remaining, 0.65f + 12 * MathF.Pow(remaining, 1.4f), -1.6f * remaining);
    }

    public void Build(FieldSnapshot field)
    {
        Position = ArenaRenderer.World(field.Position); Radius = field.Radius; _remaining = field.Lifetime;
        _rockShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode cull_disabled;
            varying vec3 local_position;
            void vertex() { local_position = VERTEX; }
            void fragment() {
                vec3 p = local_position;
                float cracks = 1.0 - smoothstep(0.015, 0.11, abs(sin(p.x * 8.0 + p.z * 3.0) * sin(p.y * 6.0 - p.z * 5.0)));
                vec3 heat = mix(vec3(0.9, 0.12, 0.015), vec3(1.0, 0.68, 0.15), cracks);
                ALBEDO = mix(vec3(0.12, 0.075, 0.05), heat, cracks * 0.8);
                ROUGHNESS = 0.95;
                EMISSION = heat * cracks * 0.7;
            }
            """ };
        _tailShader ??= new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, cull_disabled, blend_mix, depth_draw_never;
            uniform float clock = 0.0;
            varying float height;
            void vertex() { height = (VERTEX.y + 2.15) / 4.3; }
            void fragment() {
                float taper = 1.0 - height;
                float ribbons = 0.5 + 0.5 * sin(UV.x * 25.1327 + height * 12.0 - clock * 13.0);
                float pulse = 0.5 + 0.5 * sin(height * 20.0 - clock * 16.0);
                ALBEDO = mix(vec3(1.0, 0.2, 0.025), vec3(1.0, 0.8, 0.32), taper * (0.45 + ribbons * 0.45));
                EMISSION = ALBEDO * (0.25 + pulse * 0.13);
                ALPHA = pow(max(taper, 0.0), 0.65) * (0.22 + ribbons * 0.44) * smoothstep(0.0, 0.09, height);
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
        _flight.Rotation = new(-0.1f, 0, 0.17f);
        _rock = Piece(_flight, RockMesh(), Vector3.Zero, new ShaderMaterial { Shader = _rockShader });
        _rock.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        _tail = new ShaderMaterial { Shader = _tailShader };
        Piece(_flight, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.67f, Height = 4.3f, RadialSegments = 12 },
            new(0, 2.45f, 0), _tail);
        _flight.Position = FlightPosition(field.Lifetime);
    }

    private static ArrayMesh RockMesh()
    {
        float t = (1 + MathF.Sqrt(5)) / 2;
        Vector3[] points = [new(-1,t,0),new(1,t,0),new(-1,-t,0),new(1,-t,0),new(0,-1,t),new(0,1,t),
            new(0,-1,-t),new(0,1,-t),new(t,0,-1),new(t,0,1),new(-t,0,-1),new(-t,0,1)];
        int[] triangles = [0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,
            3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1];
        var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        foreach (int index in triangles)
            surface.AddVertex(points[index].Normalized() * (0.76f + 0.12f * MathF.Sin(index * 8.13f)) * new Vector3(1, 1.1f, 0.94f));
        surface.GenerateNormals(); return surface.Commit();
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
        _rock.Rotation += new Vector3(delta * 2.1f, delta * 1.3f, delta * 0.7f);
        _tail.SetShaderParameter("clock", time);
        _warning.SetShaderParameter("clock", time);
        _warning.SetShaderParameter("progress", Math.Clamp((SpellBalance.MeteorLifetime - _remaining) / SpellBalance.MeteorDelay, 0, 1));
        Visible = field.Lifetime > 0;
    }
}
