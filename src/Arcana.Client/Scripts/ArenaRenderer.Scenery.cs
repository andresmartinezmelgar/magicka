using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    internal int SceneryTrees { get; private set; }
    internal int SceneryBuildings { get; private set; }
    internal ArenaTheme SceneryTheme => _map.Theme;
    private static readonly Color[] Leaves = [new("27593c"), new("357548"), new("468453"), new("619552")];
    private static readonly Color[] Facades = [new("bda990"), new("c5b9a4"), new("8fa2a1"), new("c6a180")];
    private static readonly Color[] Roofs = [new("713f39"), new("8f5040"), new("496778"), new("665c67")];

    private void BuildScenery(Node3D root)
    {
        SceneryTrees = SceneryBuildings = 0;
        bool city = _map.Theme == ArenaTheme.City;
        _environment.BackgroundColor = new(city ? "263f4c" : "172f29");
        _environment.AmbientLightColor = new("c3ccd9");
        _environment.AmbientLightEnergy = Ui.IsMobile ? 0.48f : 0.55f;
        _sun.LightColor = new("f4f4f2");
        _sun.LightEnergy = Ui.IsMobile ? 0.90f : 0.95f;
        Mesh(root, new PlaneMesh { Size = new(Math.Max(240, _map.HalfWidth * 2 + 50), Math.Max(200, _map.HalfHeight * 2 + 50)) },
            new(0, -0.18f, 0), new(city ? "4a5952" : "233f2c"));
        BuildTerrain(root);
        BuildBorders(root);
        foreach (var obstacle in _map.Obstacles) BuildLandscapeObstacle(root, obstacle);
        BuildOutskirts(root);
        BuildGroundPlants(root);
    }
    private void BuildTerrain(Node3D root)
    {
        var shader = new Shader { Code = """
            shader_type spatial;
            uniform vec2 point_a;
            uniform vec2 point_b;
            uniform vec2 point_c;
            uniform int theme;
            uniform float ground_contrast = 0.0;
            varying vec2 ground_position;
            void vertex() { ground_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xz; }
            float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
            float ground_noise(vec2 p) {
                vec2 cell = floor(p), blend = fract(p);
                blend = blend * blend * (3.0 - 2.0 * blend);
                return mix(mix(hash(cell),hash(cell+vec2(1.0,0.0)),blend.x),
                    mix(hash(cell+vec2(0.0,1.0)),hash(cell+vec2(1.0,1.0)),blend.x),blend.y);
            }
            float segment_distance(vec2 p, vec2 a, vec2 b) {
                vec2 line = b-a;
                float t = clamp(dot(p-a,line) / max(dot(line,line),0.001),0.0,1.0);
                return length(p-a-line*t);
            }
            void fragment() {
                vec2 world = ground_position;
                float patch = ground_noise(world * 0.32);
                float grain = hash(floor(world * 13.0));
                vec3 grass = mix(vec3(0.19,0.32,0.16),vec3(0.30,0.43,0.22),patch*0.7+grain*0.3);
                float trail = min(segment_distance(world, point_a, point_b),segment_distance(world,point_b,point_c));
                trail = min(trail, abs(world.y));
                vec3 dirt = mix(vec3(0.36,0.29,0.20),vec3(0.48,0.39,0.26),grain);
                vec2 tile = world / (theme == 1 ? 1.0 : 2.0);
                tile.x += mod(floor(tile.y),2.0)*0.5;
                vec2 p = fract(tile);
                float grout = smoothstep(0.025,0.065,min(min(p.x,1.0-p.x),min(p.y,1.0-p.y)));
                float stone_noise = hash(floor(tile));
                if (theme == 1) {
                    vec3 stone = mix(vec3(0.40,0.43,0.44),vec3(0.56,0.54,0.49),stone_noise);
                    vec3 road = mix(vec3(0.24,0.29,0.31),vec3(0.34,0.38,0.39),grain);
                    stone = mix(road,stone,smoothstep(1.9,2.3,abs(world.y)));
                    ALBEDO = mix(vec3(0.23,0.25,0.25),stone,grout);
                } else if (theme == 2) {
                    vec3 stone = mix(vec3(0.41,0.40,0.30),vec3(0.61,0.56,0.40),stone_noise);
                    float moss = smoothstep(0.66,0.89,patch) * smoothstep(1.5,4.0,trail);
                    stone = mix(stone,grass,moss*0.8);
                    float ring = abs(length(world * vec2(0.75,1.0))-9.0);
                    stone = mix(stone,vec3(0.69,0.64,0.47),1.0-smoothstep(0.10,0.20,ring));
                    ALBEDO = mix(vec3(0.20,0.26,0.17),stone,grout);
                } else {
                    if (theme == 3) grass *= vec3(0.80,1.0,1.10);
                    ALBEDO = mix(dirt,grass,smoothstep(1.15,1.75,trail + patch*0.6));
                }
                ALBEDO = mix(ALBEDO, ALBEDO * vec3(0.68, 0.63, 0.72), ground_contrast);
                ROUGHNESS = 0.96;
            }
            """ };
        foreach (var section in _map.Floors)
        {
            var floor = Mesh(root, new PlaneMesh { Size = new(section.Width, section.Height) }, World(section.Center), Colors.White);
            var material = new ShaderMaterial { Shader = shader };
            for (int i = 0; i < 3; i++) material.SetShaderParameter("point_" + (char)('a' + i), new Vector2(_map.ControlPoints[i].X, _map.ControlPoints[i].Y));
            material.SetShaderParameter("theme", (int)_map.Theme); floor.MaterialOverride = material;
            material.SetShaderParameter("ground_contrast", Ui.IsMobile ? 1 : 0.75f);
            Box(root, new(section.Width + 0.3f, 0.18f, section.Height + 0.3f), World(section.Center, -0.11f), new("4c5140"));
        }
    }
    private void BuildBorders(Node3D root)
    {
        bool forest = _map.Theme is ArenaTheme.Forest or ArenaTheme.Sanctuary;
        var stone = new Color(_map.Theme == ArenaTheme.Ruins ? "8a8463" : "7c8277");
        foreach (var edge in _map.Boundary)
        {
            int count = (int)Math.Ceiling(V2.Distance(edge.A, edge.B) / 2);
            bool horizontal = Math.Abs(edge.A.X - edge.B.X) > 0;
            for (int i = 0; i < count; i++)
            {
                var pos = V2.Lerp(edge.A, edge.B, (i + 0.5f) / count);
                if (forest)
                {
                    Box(root, new(0.22f, 0.85f, 0.22f), World(pos, 0.42f), new("65503a"));
                    Box(root, horizontal ? new(1.9f, 0.12f, 0.14f) : new(0.14f, 0.12f, 1.9f), World(pos, 0.48f), new("8e7650"));
                }
                else
                {
                    Box(root, horizontal ? new(1.9f, 0.48f, 0.45f) : new(0.45f, 0.48f, 1.9f), World(pos, 0.23f), stone);
                    if (_map.Theme == ArenaTheme.Ruins && i % 6 == 0)
                        Box(root, new(0.85f, 1.0f, 0.85f), World(pos, 0.5f), stone.Darkened(0.13f));
                }
            }
        }
    }
    private void BuildLandscapeObstacle(Node3D parent, Obstacle obstacle)
    {
        var root = new Node3D { Position = World(obstacle.Position) }; parent.AddChild(root);
        float r = obstacle.Radius, h = obstacle.Height;
        if (_map.Theme == ArenaTheme.Forest)
        { BuildTree(root, V2.Zero, r, h + 0.5f, SceneryTrees % Leaves.Length); return; }
        if (_map.Theme == ArenaTheme.Sanctuary)
        {
            var rock = Mesh(root, new SphereMesh { Radius = r, Height = h, RadialSegments = 10, Rings = 5 }, new(0, h / 2, 0), new("7d8d7a"));
            rock.Scale = new(0.9f, 1, 0.8f);
            Mesh(root, new PrismMesh { Size = new(0.55f, 1.15f, 0.55f) }, new(0, h + 0.15f, 0), new("6bcbd1"), 0.6f);
            Ring(root, r * 0.75f, 0.07f, new(0, 0.04f, 0), new("73aaa1")); return;
        }
        var stone = new Color(_map.Theme == ArenaTheme.City ? "90958c" : "a19877");
        Mesh(root, new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 0.28f, RadialSegments = 12 }, new(0, 0.14f, 0), stone);
        Mesh(root, new CylinderMesh { TopRadius = r * 0.66f, BottomRadius = r * 0.8f, Height = h - 0.2f, RadialSegments = 12 }, new(0, h / 2, 0), stone.Darkened(0.08f));
        Mesh(root, new CylinderMesh { TopRadius = r * 0.93f, BottomRadius = r * 0.93f, Height = 0.28f, RadialSegments = 10 }, new(0, h, 0), stone.Lightened(0.1f));
        if (_map.Theme == ArenaTheme.City)
        {
            Box(root, new(0.55f, 0.7f, 0.55f), new(0, h + 0.32f, 0), new("cfb66b"));
            Box(root, new(0.67f, 0.08f, 0.67f), new(0, h + 0.7f, 0), new("3c4b50"));
        }
        else
        {
            for (int i = 0; i < 5; i++)
            {
                float angle = i * 1.8f;
                var leaf = Mesh(root, new SphereMesh { Radius = r * 0.28f, Height = 0.35f, RadialSegments = 6, Rings = 3 },
                    new(MathF.Cos(angle) * r * 0.78f, 0.55f + i * 0.4f, MathF.Sin(angle) * r * 0.78f), Leaves[i % Leaves.Length]);
                leaf.Scale = new(1, 0.6f, 1);
            }
        }
    }
    private void BuildTree(Node3D parent, V2 position, float radius, float height, int variant)
    {
        SceneryTrees++;
        var root = new Node3D { Position = World(position) }; parent.AddChild(root);
        Mesh(root, new CylinderMesh { TopRadius = radius * 0.12f, BottomRadius = radius * 0.3f, Height = height * 0.8f, RadialSegments = 7 },
            new(0, height * 0.35f, 0), new("674b35"));
        for (int i = 0; i < 3; i++)
        {
            var crown = Mesh(root, new SphereMesh { Radius = radius * (i == 2 ? 0.75f : 0.9f), Height = radius * 1.25f, RadialSegments = 9, Rings = 5 },
                new((i - 1) * radius * 0.24f, height * 0.65f + i * radius * 0.28f, (i % 2) * radius * 0.2f), Leaves[(variant + i) % Leaves.Length]);
            crown.RotationDegrees = new(0, variant * 47 + i * 29, 0);
        }
        Mesh(root, new CylinderMesh { TopRadius = radius * 0.4f, BottomRadius = radius * 0.65f, Height = 0.12f, RadialSegments = 8 }, new(0, 0.02f, 0), new("53683b"));
    }
    private bool OutsideFloor(V2 p, float halfWidth, float halfHeight) => _map.Floors.All(f =>
        Math.Abs(p.X - f.Center.X) > f.Width / 2 + halfWidth || Math.Abs(p.Y - f.Center.Y) > f.Height / 2 + halfHeight);
    private void BuildOutskirts(Node3D root)
    {
        bool city = _map.Theme == ArenaTheme.City;
        var placed = new List<V2>(); int serial = 0;
        foreach (var edge in _map.Boundary)
        {
            var normal = (edge.B - edge.A).Normalized.Perpendicular;
            var middle = V2.Lerp(edge.A, edge.B, 0.5f);
            if (_map.Inside(middle + normal)) normal *= -1;
            int count = Math.Max(1, (int)(V2.Distance(edge.A, edge.B) / (city ? 12 : 5)));
            for (int i = 0; i < count; i++)
            {
                var at = V2.Lerp(edge.A, edge.B, (i + 0.5f) / count) + normal * (city ? 8.5f : 4.4f + (i % 3) * 0.7f);
                float clearance = city ? 5 : 2.9f;
                if (!OutsideFloor(at, clearance, clearance) || placed.Any(p => V2.Distance(p, at) < (city ? 9 : 4.3f))) continue;
                placed.Add(at);
                if (city) BuildHouse(root, at, normal, serial++);
                else
                {
                    BuildTree(root, at, 1.8f + (serial % 3) * 0.3f, 4.3f + (serial % 4) * 0.4f, serial++);
                    if (serial % 2 == 0)
                    {
                        var behind = at + normal * 4.7f;
                        if (OutsideFloor(behind, 2.9f, 2.9f)) BuildTree(root, behind, 2.1f, 5.4f, serial);
                    }
                }
            }
        }
    }
    private void BuildHouse(Node3D parent, V2 at, V2 outward, int variant)
    {
        SceneryBuildings++;
        var root = new Node3D { Position = World(at), Rotation = new(0, MathF.Atan2(outward.X, outward.Y), 0) }; parent.AddChild(root);
        float height = 3.4f + (variant % 3) * 0.6f;
        Box(root, new(6.5f, 0.35f, 6), new(0, 0.1f, 0), new("60645f"));
        Box(root, new(6, height, 5.5f), new(0, height / 2, 0), Facades[variant % Facades.Length]);
        Mesh(root, new PrismMesh { Size = new(6.8f, 1.9f, 6.3f) }, new(0, height + 0.85f, 0), Roofs[variant % Roofs.Length]);
        Box(root, new(0.5f, 2, 0.6f), new(1.8f, height + 1.0f, 1), new("9b8c79"));
        Box(root, new(1.15f, 2.1f, 0.12f), new(0, 1.05f, -2.82f), new("594430"));
        Box(root, new(6.1f, 0.16f, 0.16f), new(0, height - 0.18f, -2.82f), new("67583f"));
        for (int side = -1; side <= 1; side += 2)
        {
            Box(root, new(0.92f, 1.05f, 0.14f), new(side * 1.9f, height * 0.63f, -2.82f), new("384f59"));
            Box(root, new(1.06f, 0.13f, 0.21f), new(side * 1.9f, height * 0.63f - 0.57f, -2.88f), new("817357"));
        }
        // Toldos y cajas quedan junto a las fachadas, fuera del suelo de juego.
        if (variant % 3 == 0)
        {
            Box(root, new(3.8f, 0.13f, 1.1f), new(0, 2.35f, -3.35f), new(variant % 2 == 0 ? "9e654e" : "5a8583"));
            Box(root, new(0.9f, 0.75f, 0.85f), new(2.15f, 0.38f, -3.4f), new("94784c"));
        }
    }
    private void BuildGroundPlants(Node3D root)
    {
        var random = new Random(900 + (int)_map.Id); bool city = _map.Theme == ArenaTheme.City;
        var grass = new List<(Vector3 Position, Color Color, float Angle)>();
        var flowers = new List<(Vector3 Position, Color Color, float Angle)>();
        for (int i = 0; i < (city ? 180 : 700); i++)
        {
            var p = new V2(((float)random.NextDouble() * 2 - 1) * (_map.HalfWidth + 9),
                ((float)random.NextDouble() * 2 - 1) * (_map.HalfHeight + 9));
            if (city && _map.Inside(p) || _map.Obstacles.Any(o => V2.Distance(p, o.Position) < o.Radius + 0.4f) ||
                _map.ControlPoints.Any(c => V2.Distance(c, p) < 3.1f) || Math.Abs(p.Y) < 2) continue;
            grass.Add((World(p, 0.07f), Leaves[i % Leaves.Length], (float)random.NextDouble() * Mathf.Tau));
            if (i % 5 == 0) flowers.Add((World(p, 0.15f), new(i % 2 == 0 ? "d3b65d" : "b09cc3"), 0));
        }
        GroundBatch(root, new BoxMesh { Size = new(0.09f, 0.2f, 0.35f) }, grass);
        GroundBatch(root, new SphereMesh { Radius = 0.075f, Height = 0.10f, RadialSegments = 6, Rings = 3 }, flowers);
    }
    private void GroundBatch(Node3D root, Godot.Mesh mesh, List<(Vector3 Position, Color Color, float Angle)> entries)
    {
        if (entries.Count == 0) return;
        var instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = entries.Count };
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i]; instances.SetInstanceTransform(i, new(new Basis(Vector3.Up, e.Angle), e.Position));
            instances.SetInstanceColor(i, e.Color);
        }
        root.AddChild(new MultiMeshInstance3D { Multimesh = instances, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.White, VertexColorUseAsAlbedo = true, Roughness = 1 } });
    }
}
