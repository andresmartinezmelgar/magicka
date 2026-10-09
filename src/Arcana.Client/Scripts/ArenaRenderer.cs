using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Arena 3D y magos con sprites. Solo dibuja el estado del servidor.</summary>
public partial class ArenaRenderer : Node3D
{
    public Camera3D Camera { get; private set; } = null!;
    private readonly Dictionary<int, WizardVisual> _wizards = [];
    internal IReadOnlyDictionary<int, WizardVisual> Wizards => _wizards;
    private readonly Dictionary<int, Node3D> _projectiles = [];
    internal IReadOnlyDictionary<int, Node3D> ProjectileVisuals => _projectiles;
    private readonly Dictionary<int, BarrierVisual> _walls = [];
    internal IReadOnlyDictionary<int, BarrierVisual> Walls => _walls;
    private readonly Dictionary<int, Node3D> _fields = [];
    internal IReadOnlyDictionary<int, Node3D> Fields => _fields;
    private readonly List<(Node3D Root, MeshInstance3D Mesh, CylinderMesh Shape, ShaderMaterial Skin)> _beamPool = [];
    private readonly CombatEffects _effects = new();
    public CombatEffects Effects => _effects;
    private readonly Dictionary<int, CapturePointVisual> _points = [];
    private readonly Dictionary<int, RelicVisual> _pickups = [];
    internal IReadOnlyDictionary<int, RelicVisual> Pickups => _pickups;
    internal IReadOnlyDictionary<int, CapturePointVisual> Points => _points;
    private readonly Dictionary<(Color Color, float Glow, bool Unshaded), StandardMaterial3D> _materials = [];
    private readonly HashSet<int> _present = [];
    private readonly List<int> _removed = [];
    private WorldSnapshot? _lastSnapshot;
    private ArenaDefinition _map = ArenaMap.Get(ArenaMapId.Classic);
    private Node3D? _mapRoot;
    internal ArenaMapId CurrentMap => _map.Id;
    private bool _fresh;
    private readonly SpellAudio _audio = new();
    private long _lastEvent;
    private double _time;
    private float _zoom = 27;
    private int _beamUsed;
    internal int ActiveBeamSegments => _beamUsed;
    private Godot.Environment _environment = null!;
    private DirectionalLight3D _sun = null!;
    public bool AudioEnabled { get; set; } = true;
    public bool ShowControlPoints { get; set; } = true;
    public double InterpolationDelay { get; set; } = 1 / 60.0;

    public override void _Ready()
    {
        AddChild(_effects);
        AddChild(_audio);
        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("090f1b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("bac7db"), AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        _environment = env.Environment; AddChild(env);
        var light = new DirectionalLight3D
        {
            RotationDegrees = new(-53, -28, 0), LightColor = new Color("fff0dd"),
            LightEnergy = 1.10f, ShadowEnabled = true
        };
        _sun = light; AddChild(light);
        Camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = _zoom,
            Position = new(0, 27, 21), Current = true, Far = 600 };
        AddChild(Camera); Camera.LookAt(Vector3.Zero);
        BuildMap();
    }

    public static Vector3 World(V2 p, float height = 0) => new(p.X, height, p.Y);
    public V2 Cursor(Vector2 screen)
    {
        var origin = Camera.ProjectRayOrigin(screen); var direction = Camera.ProjectRayNormal(screen);
        if (Mathf.Abs(direction.Y) < 0.001f) return V2.Zero;
        var p = origin + direction * (-origin.Y / direction.Y);
        return new(Math.Clamp(p.X, -_map.HalfWidth, _map.HalfWidth), Math.Clamp(p.Z, -_map.HalfHeight, _map.HalfHeight));
    }
    public void Zoom(float delta) => _zoom = Math.Clamp(_zoom + delta, 17, 34);
    internal void SetPreviewZoom(float size) => _zoom = Math.Clamp(size, 10, Math.Max(34, Math.Max(_map.HalfHeight * 2, _map.HalfWidth * 2) + 10));
    internal float CurrentZoom => _zoom;
    public void Clear()
    {
        _audio.StopAll();
        foreach (var v in _wizards.Values) v.QueueFree(); _wizards.Clear();
        foreach (var v in _projectiles.Values) v.QueueFree(); _projectiles.Clear();
        foreach (var v in _walls.Values) v.QueueFree(); _walls.Clear();
        foreach (var v in _fields.Values) v.QueueFree(); _fields.Clear();
        foreach (var v in _pickups.Values) v.QueueFree(); _pickups.Clear();
        _effects.Clear();
        foreach (var point in _points.Values) point.Reset();
        foreach (var beam in _beamPool) beam.Root.Visible = false;
        _beamUsed = 0;
        _lastEvent = 0; _lastSnapshot = null;
    }

    private StandardMaterial3D Material(Color color, float glow = 0, bool unshaded = false)
    {
        var key = (color, glow, unshaded);
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
    private MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 position, Color color, float glow = 0, bool unshaded = false)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = Material(color, glow, unshaded) };
        parent.AddChild(node); return node;
    }
    private MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color) => Mesh(parent, new BoxMesh { Size = size }, pos, color);
    private MeshInstance3D Ring(Node3D parent, float radius, float width, Vector3 pos, Color color) =>
        Mesh(parent, new TorusMesh { InnerRadius = radius - width, OuterRadius = radius, Rings = 48, RingSegments = 8 }, pos, color, 0.6f);

    private void GroundRing(Node3D parent, float radius, float width, Color color)
    {
        var border = Mesh(parent, new TorusMesh { InnerRadius = Math.Max(0.01f, radius - width - 0.07f),
            OuterRadius = radius + 0.07f, Rings = 48, RingSegments = 8 }, new(0, 0.07f, 0), new("09111e"), unshaded: true);
        var ring = Mesh(parent, new TorusMesh { InnerRadius = Math.Max(0.01f, radius - width),
            OuterRadius = radius, Rings = 48, RingSegments = 8 }, new(0, 0.14f, 0), color, unshaded: true);
        border.Scale = ring.Scale = new(1, 0.25f, 1);
        border.CastShadow = ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
    }

    private void BuildMap()
    {
        if (_mapRoot != null) { RemoveChild(_mapRoot); _mapRoot.QueueFree(); }
        _points.Clear();
        var rootMap = new Node3D { Name = "MapGeometry" }; _mapRoot = rootMap; AddChild(rootMap);
        BuildScenery(rootMap);
        _practiceCasterLabel = null;
        if (_map.IsPractice) { BuildPracticeMap(rootMap); return; }
        for (int team = 0; team < 2; team++)
        {
            var color = team == 0 ? Ui.Blue : Ui.Red;
            float x = _map.Spawn(team, 0).X;
            Ring(rootMap, 2.7f, 0.10f, new(x, 0.05f, 0), color);
            Ring(rootMap, 2.5f, 0.045f, new(x, 0.05f, 0), color);
            for (int i = -1; i <= 1; i += 2)
            {
                Mesh(rootMap, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.5f, Height = 1.2f }, new(x, 0.6f, i * 3.5f), new("424e58"));
                Mesh(rootMap, new SphereMesh { Radius = 0.24f, Height = 0.48f }, new(x, 1.4f, i * 3.5f), color, 1.1f);
            }
        }
        foreach (var p in _map.ControlPoints.Select((position, id) => (position, id)))
        {
            var visual = new CapturePointVisual(); rootMap.AddChild(visual); visual.Build(p.id, p.position, _map.Id); _points[p.id] = visual;
        }
        BuildFrontline(rootMap);
    }

    public void Render(WorldSnapshot snapshot, int localId, double delta, bool playing)
    {
        if (_map.Id != snapshot.Map) { Clear(); _map = ArenaMap.Get(snapshot.Map); BuildMap(); }
        bool first = _lastSnapshot == null;
        var previous = _lastSnapshot;
        float dt = (float)delta; _time += delta;
        _fresh = !ReferenceEquals(snapshot, _lastSnapshot); _lastSnapshot = snapshot;
        foreach (var p in snapshot.Players)
        {
            if (!_wizards.TryGetValue(p.Id, out var visual))
            {
                visual = new WizardVisual(); AddChild(visual); visual.Build(p, p.Id == localId); _wizards[p.Id] = visual;
            }
            visual.Update(p, dt, (float)_time, Camera, snapshot.Time, InterpolationDelay);
        }
        if (_fresh) RemoveMissing(_wizards, snapshot.Players.Select(p => p.Id));
        var local = snapshot.Players.FirstOrDefault(p => p.Id == localId);
        float followX = _map.Id == ArenaMapId.Classic ? 7 : _map.HalfWidth - 8;
        float followY = _map.Id == ArenaMapId.Classic ? 4 : _map.HalfHeight - 7;
        var visiblePosition = local != null ? _wizards[local.Id].Position : Vector3.Zero;
        var focus = playing && local != null ? new Vector3(Math.Clamp(visiblePosition.X, -followX, followX), 0,
            Math.Clamp(visiblePosition.Z, -followY, followY)) : new Vector3(-4, 0, 0);
        var desired = focus + new Vector3(0, 27, 21);
        var previousLocal = previous?.Players.FirstOrDefault(p => p.Id == localId);
        bool respawned = local is { Alive: true } && previousLocal != null &&
            (!previousLocal.Alive || previousLocal.Deaths != local.Deaths);
        Camera.Position = playing && (first || respawned) ? desired : Camera.Position.Lerp(desired, 1 - Mathf.Exp(-3 * dt));
        Camera.LookAt(Camera.Position - new Vector3(0, 27, 21));
        Camera.Size = Mathf.Lerp(Camera.Size, playing ? _zoom : 32, 1 - Mathf.Exp(-5 * dt));
        foreach (var visual in _points.Values) visual.Visible = ShowControlPoints && snapshot.Mode == GameMode.Warfare;
        foreach (var point in snapshot.Points)
        {
            var visual = _points[point.Id];
            visual.Update(point, dt, (float)_time);
        }
        UpdateFrontline(snapshot, (float)_time);
        if (_practiceCasterLabel != null) _practiceCasterLabel.Text = PracticeCasterDescription;
        DrawProjectiles(snapshot, dt);
        DrawWalls(snapshot, dt);
        DrawFields(snapshot, dt);
        foreach (var item in snapshot.Pickups ?? [])
        {
            if (!_pickups.TryGetValue(item.Id, out var visual))
            { visual = new RelicVisual(); AddChild(visual); visual.Build(item); _pickups[item.Id] = visual; }
            visual.Update(item, (float)_time);
        }
        if (_fresh) RemoveMissing(_pickups, (snapshot.Pickups ?? []).Select(i => i.Id));
        _beamUsed = 0;
        // Dibuja primero todos los canales; el presupuesto de detalle nunca oculta un rayo.
        foreach (var beam in snapshot.Beams) DrawBeam(beam, false);
        foreach (var beam in snapshot.Beams) DrawBeam(beam, true);
        for (int i = _beamUsed; i < _beamPool.Count; i++) _beamPool[i].Root.Visible = false;
        foreach (var e in snapshot.Events.Where(e => e.Id > _lastEvent))
        {
            if (playing)
            {
                _effects.Play(e);
                if (e.Kind is "cast" or "self" or "nova" or "barrier")
                {
                    // El evento no contiene ID de autor; su posición es la del lanzador.
                    foreach (var p in snapshot.Players)
                        if (p.Alive && V2.Distance(p.Position, e.Position) < 0.15f && _wizards.TryGetValue(p.Id, out var caster)) caster.PlayCastPose(e.Elements);
                }
                if (e.Kind is "cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink" &&
                    AudioEnabled && local != null && V2.Distance(e.Position, local.Position) < 15) _audio.PlayEvent(e);
            }
            _lastEvent = Math.Max(_lastEvent, e.Id);
        }
        _effects.Advance(snapshot, dt, playing);
    }

    private void RemoveMissing<T>(Dictionary<int, T> nodes, IEnumerable<int> present) where T : Node
    {
        _present.Clear(); _removed.Clear();
        foreach (int id in present) _present.Add(id);
        foreach (int id in nodes.Keys) if (!_present.Contains(id)) _removed.Add(id);
        foreach (int id in _removed) { nodes[id].QueueFree(); nodes.Remove(id); }
    }

    private void DrawProjectiles(WorldSnapshot snapshot, float dt)
    {
        foreach (var projectile in snapshot.Projectiles)
        {
            if (!_projectiles.TryGetValue(projectile.Id, out var root))
            {
                root = new Node3D { Position = World(projectile.Position, 0.9f) }; AddChild(root);
                bool ice = projectile.Elements.Contains(Element.Cold);
                var color = Ui.SpellColor(projectile.Elements);
                Mesh(root, ice ? new PrismMesh { Size = new(0.3f, 0.9f, 0.3f) }
                    : new SphereMesh { Radius = projectile.Radius, Height = projectile.Radius * 2 }, Vector3.Zero, color, 0.35f);
                var strength = SpellVisualStrength.For(projectile.Elements);
                color.A = 0.3f;
                for (int i = 0; i < strength.Charges - 1; i++)
                {
                    float radius = projectile.Radius + 0.09f + i * 0.045f;
                    var accent = Mesh(root, new TorusMesh { InnerRadius = radius - 0.025f, OuterRadius = radius,
                        Rings = 24, RingSegments = 5 }, Vector3.Zero, color, 0.25f);
                    accent.Rotation = new(0.9f + i * 0.7f, 0, i * 0.6f);
                    accent.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                _projectiles[projectile.Id] = root;
            }
            root.Position = root.Position.Lerp(World(projectile.Position, 0.9f), 1 - Mathf.Exp(-35 * dt));
            root.Rotation += new Vector3(dt * 4, dt * 3, dt * 2);
        }
        if (_fresh) RemoveMissing(_projectiles, snapshot.Projectiles.Select(p => p.Id));
    }
    private void DrawWalls(WorldSnapshot snapshot, float delta)
    {
        foreach (var wall in snapshot.Walls)
        {
            if (!_walls.TryGetValue(wall.Id, out var visual))
            {
                visual = new BarrierVisual(); AddChild(visual); visual.Build(wall); _walls[wall.Id] = visual;
            }
            visual.Update(wall, delta, (float)_time);
        }
        if (_fresh) RemoveMissing(_walls, snapshot.Walls.Select(w => w.Id));
    }
    private void DrawFields(WorldSnapshot snapshot, float delta)
    {
        foreach (var field in snapshot.Fields)
        {
            if (field.Kind == "meteor")
            {
                if (!_fields.TryGetValue(field.Id, out var existing))
                {
                    var meteor = new MeteorVisual(); AddChild(meteor); meteor.Build(field);
                    _fields[field.Id] = existing = meteor;
                }
                ((MeteorVisual)existing).Update(field, delta, (float)_time);
                continue;
            }
            if (!_fields.TryGetValue(field.Id, out var root))
            {
                root = new Node3D { Position = World(field.Position) }; AddChild(root);
                Color color = Ui.SpellColor(field.Elements);
                bool mine = field.Kind is "mine" or "healing_mine";
                if (field.Kind == "healing_mine") color = Ui.Health;
                int charges = SpellVisualStrength.For(field.Elements).Charges;
                GroundRing(root, field.Radius, mine ? 0.09f : 0.15f, color);
                if (mine)
                {
                    GroundRing(root, field.TriggerRadius, 0.12f, color);
                    var core = Mesh(root, new PrismMesh { Size = new(0.4f, 0.55f, 0.4f) }, new(0, 0.36f, 0), color, unshaded: true);
                    core.Name = "MineCore";
                    root.AddChild(new Label3D { Text = field.Kind == "healing_mine" ? "+" : "!", Position = new(0, 0.9f, 0),
                        FontSize = 50, PixelSize = 0.009f, Modulate = color, OutlineSize = 10, OutlineModulate = new("060a12"),
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                }
                var fill = color; fill.A = 0.32f + charges * 0.04f;
                float radius = mine ? field.TriggerRadius : field.Radius;
                var floor = Mesh(root, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.012f, RadialSegments = 48 },
                    new(0, 0.031f, 0), new(new Color("09111e"), 0.48f), unshaded: true);
                var area = Mesh(root, new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.012f, RadialSegments = 48 },
                    new(0, 0.048f, 0), fill, unshaded: true);
                floor.CastShadow = area.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                int marks = charges == 1 ? 0 : charges == 2 ? 3 : 6;
                var accent = color;
                for (int i = 0; i < marks; i++)
                {
                    float angle = i * Mathf.Tau / marks;
                    var glyph = Mesh(root,new BoxMesh { Size = new(0.1f,0.035f,0.24f) },
                        new(Mathf.Cos(angle)*radius*0.82f,0.11f,Mathf.Sin(angle)*radius*0.82f),accent,unshaded:true);
                    glyph.Rotation = new(0,-angle,0); glyph.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                _fields[field.Id] = root;
            }
            root.Scale = Vector3.One * (1 + Mathf.Sin((float)_time * 6) * 0.02f);
            if (root.GetNodeOrNull<MeshInstance3D>("MineCore") is { } mineCore)
                mineCore.Visible = field.Armed || (int)(_time * 10) % 2 == 0;
        }
        if (_fresh) RemoveMissing(_fields, snapshot.Fields.Select(f => f.Id));
    }

}
