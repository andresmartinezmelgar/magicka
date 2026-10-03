using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Recursos 3D originales, generados con mallas de Godot. Solo dibuja el estado del servidor.</summary>
public partial class ArenaRenderer : Node3D
{
    public Camera3D Camera { get; private set; } = null!;
    private readonly Dictionary<int, WizardVisual> _wizards = [];
    private readonly Dictionary<int, MeshInstance3D> _projectiles = [];
    private readonly Dictionary<int, MeshInstance3D> _walls = [];
    private readonly Dictionary<int, Node3D> _fields = [];
    private readonly List<(Node3D Root, MeshInstance3D Mesh, CylinderMesh Shape)> _beamPool = [];
    private readonly List<Burst> _bursts = [];
    private readonly Dictionary<int, (Node3D Root, MeshInstance3D Ring, MeshInstance3D Crystal)> _points = [];
    private readonly Dictionary<string, StandardMaterial3D> _materials = [];
    private readonly SpellAudio _audio = new();
    private long _lastEvent;
    private double _time;
    private float _zoom = 27;
    private int _beamUsed;
    private bool _playing;

    public override void _Ready()
    {
        AddChild(_audio);
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("090f1b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("91abc9"), AmbientLightEnergy = 0.75f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(env);
        var light = new DirectionalLight3D
        {
            RotationDegrees = new(-53, -28, 0), LightColor = new Color("ffdfa9"),
            LightEnergy = 1.35f, ShadowEnabled = true
        };
        AddChild(light);
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = _zoom,
            Position = new(0, 27, 21), Current = true, Far = 180 };
        AddChild(Camera); Camera.LookAt(Vector3.Zero);
        BuildMap();
    }

    public static Vector3 World(V2 p, float height = 0) => new(p.X, height, p.Y);
    public V2 Cursor(Vector2 screen)
    {
        var origin = Camera.ProjectRayOrigin(screen); var direction = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(direction.Y) < 0.001f) return V2.Zero;
        var p = origin + direction * (-origin.Y / direction.Y);
        return new(Math.Clamp(p.X, -ArenaMap.HalfWidth, ArenaMap.HalfWidth), Math.Clamp(p.Z, -ArenaMap.HalfHeight, ArenaMap.HalfHeight));
    }
    public void Zoom(float delta) => _zoom = Math.Clamp(_zoom + delta, 17, 34);
    public void Clear()
    {
        foreach (var v in _wizards.Values) v.QueueFree(); _wizards.Clear();
        foreach (var v in _projectiles.Values) v.QueueFree(); _projectiles.Clear();
        foreach (var v in _walls.Values) v.QueueFree(); _walls.Clear();
        foreach (var v in _fields.Values) v.QueueFree(); _fields.Clear();
        foreach (var b in _bursts) b.Root.QueueFree(); _bursts.Clear();
        foreach (var beam in _beamPool) beam.Root.Visible = false;
        _lastEvent = 0; _playing = false;
    }

    private StandardMaterial3D Material(Color color, float glow = 0, bool unshaded = false)
    {
        string key = color.ToHtml() + ":" + glow + ":" + unshaded;
        if (_materials.TryGetValue(key, out var existing)) return existing;
        var material = new StandardMaterial3D
        {
            AlbedoColor = color, Roughness = 0.82f,
            Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            EmissionEnabled = glow > 0, Emission = color, EmissionEnergyMultiplier = glow,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _materials[key] = material; return material;
    }
    private MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 position, Color color, float glow = 0)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = Material(color, glow) };
        parent.AddChild(node); return node;
    }
    private MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color) => Mesh(parent, new BoxMesh { Size = size }, pos, color);
    private MeshInstance3D Ring(Node3D parent, float radius, float width, Vector3 pos, Color color) =>
        Mesh(parent, new TorusMesh { InnerRadius = radius - width, OuterRadius = radius, Rings = 48, RingSegments = 8 }, pos, color, 0.6f);

    private void BuildMap()
    {
        Mesh(this, new PlaneMesh { Size = new(180, 180) }, new(0, -1.2f, 0), new Color("101d26"));
        var floor = Mesh(this, new PlaneMesh { Size = new(44, 32) }, Vector3.Zero, new Color("536363"));
        floor.MaterialOverride = new ShaderMaterial
        {
            Shader = new Shader { Code = """
                shader_type spatial;
                void fragment() {
                    vec2 tile = UV * vec2(22.0, 16.0);
                    vec2 p = fract(tile);
                    float edge = smoothstep(0.012, 0.036, min(min(p.x, 1.0-p.x), min(p.y, 1.0-p.y)));
                    float noise = fract(sin(dot(floor(tile), vec2(127.1,311.7))) * 43758.5453);
                    vec3 stone = mix(vec3(0.20,0.26,0.27), vec3(0.31,0.37,0.34), noise);
                    ALBEDO = mix(vec3(0.105,0.14,0.15), stone, edge);
                    ROUGHNESS = 0.92;
                }
                """ }
        };
        Box(this, new(45, 0.8f, 33), new(0, -0.45f, 0), new("273b3e"));
        var stone = new Color("697371");
        for (int i = -21; i <= 21; i += 2)
        {
            Box(this, new(1.9f, 0.55f, 0.6f), new(i, 0.25f, -16.3f), stone);
            Box(this, new(1.9f, 0.55f, 0.6f), new(i, 0.25f, 16.3f), stone);
        }
        for (int i = -15; i <= 15; i += 2)
        {
            Box(this, new(0.6f, 0.55f, 1.9f), new(-22.3f, 0.25f, i), stone);
            Box(this, new(0.6f, 0.55f, 1.9f), new(22.3f, 0.25f, i), stone);
        }
        foreach (var obstacle in ArenaMap.Obstacles)
        {
            var root = new Node3D { Position = World(obstacle.Position) }; AddChild(root);
            Mesh(root, new CylinderMesh { TopRadius = obstacle.Radius, BottomRadius = obstacle.Radius + 0.05f, Height = 0.25f, RadialSegments = 10 }, new(0, 0.13f, 0), new("778282"));
            Mesh(root, new CylinderMesh { TopRadius = obstacle.Radius * 0.75f, BottomRadius = obstacle.Radius * 0.88f, Height = obstacle.Height, RadialSegments = 8 }, new(0, obstacle.Height / 2 + 0.2f, 0), stone);
            Mesh(root, new CylinderMesh { TopRadius = obstacle.Radius * 0.98f, BottomRadius = obstacle.Radius, Height = 0.3f, RadialSegments = 8 }, new(0, obstacle.Height + 0.25f, 0), new("89908a"));
            var crystal = Mesh(root, new PrismMesh { Size = new(0.46f, 0.9f, 0.46f) }, new(0, obstacle.Height + 0.85f, 0), new("8eb4d1"), 0.7f);
            crystal.RotationDegrees = new(0, 45, 0);
        }
        for (int team = 0; team < 2; team++)
        {
            var color = team == 0 ? Ui.Blue : Ui.Red;
            float x = team == 0 ? -18 : 18;
            Ring(this, 2.7f, 0.10f, new(x, 0.05f, 0), color);
            Ring(this, 2.5f, 0.045f, new(x, 0.05f, 0), color);
            for (int i = -1; i <= 1; i += 2)
            {
                Mesh(this, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.5f, Height = 1.2f }, new(x, 0.6f, i * 3.5f), new("424e58"));
                Mesh(this, new SphereMesh { Radius = 0.24f, Height = 0.48f }, new(x, 1.4f, i * 3.5f), color, 1.1f);
            }
        }
        foreach (var p in ArenaMap.ControlPoints.Select((position, id) => (position, id)))
        {
            var root = new Node3D { Position = World(p.position) }; AddChild(root);
            var ring = Ring(root, 3, 0.07f, new(0, 0.04f, 0), new("789094"));
            Ring(root, 0.7f, 0.05f, new(0, 0.05f, 0), new("789094"));
            var crystal = Mesh(root, new PrismMesh { Size = new(0.38f, 0.6f, 0.38f) }, new(0, 0.42f, 0), Ui.Gold, 0.7f);
            _points[p.id] = (root, ring, crystal);
        }
        // Árboles y ruinas decorativos se quedan fuera de la zona de colisión.
        var random = new Random(36);
        for (int i = 0; i < 36; i++)
        {
            float x = (float)random.NextDouble() * 70 - 35;
            float z = (float)random.NextDouble() * 54 - 27;
            if (Mathf.Abs(x) < 24 && Mathf.Abs(z) < 18) continue;
            float height = 2.5f + (float)random.NextDouble() * 3;
            Mesh(this, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.4f, Height = height }, new(x, height / 2 - 1, z), new("283b36"));
            Mesh(this, new CylinderMesh { TopRadius = 0, BottomRadius = 2.1f, Height = height }, new(x, height - 1, z), new("1d3632"));
        }
    }

    public void Render(WorldSnapshot snapshot, int localId, double delta, bool playing)
    {
        float dt = (float)delta; _time += delta; _playing = playing;
        foreach (var p in snapshot.Players)
        {
            if (!_wizards.TryGetValue(p.Id, out var visual))
            {
                visual = new WizardVisual(); AddChild(visual); visual.Build(p, p.Id == localId); _wizards[p.Id] = visual;
            }
            visual.Update(p, dt, (float)_time, Camera);
        }
        RemoveMissing(_wizards, snapshot.Players.Select(p => p.Id));
        var local = snapshot.Players.FirstOrDefault(p => p.Id == localId);
        var focus = playing && local != null ? new Vector3(Math.Clamp(local.Position.X, -7, 7), 0, Math.Clamp(local.Position.Y, -4, 4)) : new Vector3(-4, 0, 0);
        var desired = focus + new Vector3(0, 27, 21);
        Camera.Position = Camera.Position.Lerp(desired, 1 - Mathf.Exp(-3 * dt));
        Camera.LookAt(Camera.Position - new Vector3(0, 27, 21));
        Camera.Size = Mathf.Lerp(Camera.Size, playing ? _zoom : 32, 1 - Mathf.Exp(-5 * dt));
        foreach (var point in snapshot.Points)
        {
            var visuals = _points[point.Id];
            visuals.Root.Visible = snapshot.Mode == GameMode.Warfare;
            var color = point.Owner == 0 ? Ui.Blue : point.Owner == 1 ? Ui.Red : new Color("839999");
            visuals.Ring.MaterialOverride = Material(color, 0.5f);
            visuals.Crystal.MaterialOverride = Material(color, 0.8f);
            visuals.Crystal.Rotation = new(0, (float)_time * 0.7f, 0);
            visuals.Crystal.Position = new(0, 0.6f + Mathf.Sin((float)_time * 2 + point.Id) * 0.07f, 0);
        }
        DrawProjectiles(snapshot, dt);
        DrawWalls(snapshot);
        DrawFields(snapshot);
        _beamUsed = 0;
        foreach (var beam in snapshot.Beams) DrawBeam(beam);
        for (int i = _beamUsed; i < _beamPool.Count; i++) _beamPool[i].Root.Visible = false;
        foreach (var e in snapshot.Events.Where(e => e.Id > _lastEvent))
        {
            if (playing && e.Kind is "cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink")
            {
                if (_bursts.Count < 40) CreateBurst(e);
                if (local != null && V2.Distance(e.Position, local.Position) < 15) _audio.PlayEvent(e);
            }
            _lastEvent = Math.Max(_lastEvent, e.Id);
        }
        TickBursts(dt);
    }

    private static void RemoveMissing<T>(Dictionary<int, T> nodes, IEnumerable<int> present) where T : Node
    {
        var ids = present.ToHashSet();
        foreach (int id in nodes.Keys.Where(id => !ids.Contains(id)).ToArray()) { nodes[id].QueueFree(); nodes.Remove(id); }
    }

    private void DrawProjectiles(WorldSnapshot snapshot, float dt)
    {
        foreach (var projectile in snapshot.Projectiles)
        {
            if (!_projectiles.TryGetValue(projectile.Id, out var mesh))
            {
                bool ice = projectile.Elements.Contains(Element.Cold);
                mesh = Mesh(this, ice ? new PrismMesh { Size = new(0.3f, 0.9f, 0.3f) } : new SphereMesh { Radius = projectile.Radius, Height = projectile.Radius * 2 },
                    World(projectile.Position, 0.9f), Ui.SpellColor(projectile.Elements), 0.5f);
                _projectiles[projectile.Id] = mesh;
            }
            mesh.Position = mesh.Position.Lerp(World(projectile.Position, 0.9f), 1 - Mathf.Exp(-35 * dt));
            mesh.Rotation += new Vector3(dt * 9, dt * 5, dt * 4);
        }
        RemoveMissing(_projectiles, snapshot.Projectiles.Select(p => p.Id));
    }
    private void DrawWalls(WorldSnapshot snapshot)
    {
        foreach (var wall in snapshot.Walls)
        {
            if (!_walls.TryGetValue(wall.Id, out var mesh))
            {
                var color = Ui.SpellColor(wall.Elements);
                bool stone = wall.Elements.Contains(Element.Earth);
                color.A = stone ? 1 : 0.44f;
                mesh = Mesh(this, new BoxMesh { Size = new(0.28f, stone ? 1.7f : 2.3f, V2.Distance(wall.A, wall.B)) },
                    World((wall.A + wall.B) / 2, stone ? 0.85f : 1.15f), color, stone ? 0 : 0.6f);
                var direction = wall.B - wall.A; mesh.Rotation = new(0, Mathf.Atan2(direction.X, direction.Y), 0);
                _walls[wall.Id] = mesh;
            }
            mesh.Visible = wall.Lifetime > 1 || ((int)(_time * 9) % 2 == 0);
        }
        RemoveMissing(_walls, snapshot.Walls.Select(w => w.Id));
    }
    private void DrawFields(WorldSnapshot snapshot)
    {
        foreach (var field in snapshot.Fields)
        {
            if (!_fields.TryGetValue(field.Id, out var root))
            {
                root = new Node3D { Position = World(field.Position) }; AddChild(root);
                Color color = field.Kind == "meteor" ? new("ff8062") : Ui.SpellColor(field.Elements);
                Ring(root, field.Radius, 0.10f, new(0, 0.08f, 0), color);
                var fill = color; fill.A = 0.12f;
                Mesh(root, new CylinderMesh { TopRadius = field.Radius, BottomRadius = field.Radius, Height = 0.02f, RadialSegments = 48 }, new(0, 0.035f, 0), fill, 0.4f);
                if (field.Kind == "meteor")
                {
                    var meteor = Mesh(root, new SphereMesh { Radius = 0.7f, Height = 1.4f }, new(0, 12, 0), new("ffb47a"), 1.1f);
                    meteor.Name = "Meteor";
                }
                _fields[field.Id] = root;
            }
            root.Scale = Vector3.One * (1 + Mathf.Sin((float)_time * 6) * 0.02f);
            if (field.Kind == "meteor" && root.GetNodeOrNull<MeshInstance3D>("Meteor") is { } meteorNode)
            {
                meteorNode.Position = new(0, Math.Max(0.7f, field.Lifetime / 1.5f * 12), 0);
                meteorNode.RotateY(0.07f);
            }
        }
        RemoveMissing(_fields, snapshot.Fields.Select(f => f.Id));
    }

    private void BeamSegment(Vector3 from, Vector3 to, Color color, float radius, float endRadius = -1)
    {
        if (_beamUsed >= 160 || from.DistanceTo(to) < 0.05f) return;
        if (_beamUsed == _beamPool.Count)
        {
            var root = new Node3D(); AddChild(root);
            var shape = new CylinderMesh { RadialSegments = 8 };
            var mesh = Mesh(root, shape, Vector3.Zero, color, 1);
            mesh.RotationDegrees = new(90, 0, 0); _beamPool.Add((root, mesh, shape));
        }
        var beam = _beamPool[_beamUsed++];
        float length = from.DistanceTo(to);
        beam.Root.Visible = true; beam.Root.Position = from; beam.Root.LookAt(to);
        beam.Shape.Height = length; beam.Shape.TopRadius = radius; beam.Shape.BottomRadius = endRadius < 0 ? radius : endRadius;
        beam.Mesh.Position = new(0, 0, -length / 2); beam.Mesh.MaterialOverride = Material(color, 0.85f, true);
    }
    private void DrawBeam(BeamSnapshot beam)
    {
        Color color = Ui.SpellColor(beam.Elements);
        var from = World(beam.From, 1); var to = World(beam.To, 1);
        if (beam.Shape == SpellShape.Lightning)
        {
            const int segments = 8; Vector3 previous = from;
            var side = (to - from).Normalized().Cross(Vector3.Up);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                var next = from.Lerp(to, t) + side * (i == segments ? 0 : Mathf.Sin((float)_time * 43 + i * 7 + beam.Owner) * 0.35f);
                BeamSegment(previous, next, color, 0.052f); previous = next;
            }
        }
        else if (beam.Shape is SpellShape.Spray or SpellShape.Force)
        {
            color.A = beam.Shape == SpellShape.Force ? 0.055f : 0.11f;
            BeamSegment(from, to, color, 0.09f, from.DistanceTo(to) * 0.28f);
            Color bright = color; bright.A = 0.48f;
            var direction = (to - from).Normalized(); var side = direction.Cross(Vector3.Up);
            for (int i = 0; i < 8; i++)
            {
                float t = Mathf.PosMod((float)_time * 1.8f + i * 0.137f, 1);
                var p = from.Lerp(to, t) + side * (Mathf.Sin(i * 4.23f + (float)_time * 5) * t * 1.2f);
                BeamSegment(p, p + direction * 0.32f, bright, 0.035f + 0.06f * t);
            }
        }
        else
        {
            var glow = color; glow.A = 0.2f;
            BeamSegment(from, to, glow, 0.16f);
            BeamSegment(from, to, color.Lightened(0.35f), 0.042f);
        }
    }

    private sealed class Burst(Node3D root, List<(MeshInstance3D Mesh, Vector3 Velocity)> particles, float duration)
    {
        public Node3D Root = root;
        public List<(MeshInstance3D Mesh, Vector3 Velocity)> Particles = particles;
        public float Remaining = duration;
        public float Duration = duration;
    }
    private void CreateBurst(GameEvent e)
    {
        var root = new Node3D { Position = World(e.Position, 0.9f) }; AddChild(root);
        var particles = new List<(MeshInstance3D, Vector3)>();
        var random = new Random((int)e.Id);
        int count = e.Kind is "explosion" or "death" or "nova" ? 18 : 6;
        float speed = e.Kind is "explosion" or "nova" ? 6 : 2.2f;
        Color color = Ui.SpellColor(e.Elements);
        for (int i = 0; i < count; i++)
        {
            var mesh = Mesh(root, new SphereMesh { Radius = 0.075f, Height = 0.15f, RadialSegments = 8, Rings = 4 }, Vector3.Zero, color, 0.7f);
            var direction = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 1.4f, (float)random.NextDouble() * 2 - 1).Normalized();
            particles.Add((mesh, direction * speed));
        }
        _bursts.Add(new(root, particles, 0.65f));
    }
    private void TickBursts(float dt)
    {
        foreach (var b in _bursts)
        {
            b.Remaining -= dt;
            foreach (var particle in b.Particles)
            {
                particle.Mesh.Position += particle.Velocity * dt;
                particle.Mesh.Scale = Vector3.One * Math.Max(0.02f, b.Remaining / b.Duration);
            }
        }
        foreach (var b in _bursts.Where(b => b.Remaining <= 0).ToArray()) { b.Root.QueueFree(); _bursts.Remove(b); }
    }
}
