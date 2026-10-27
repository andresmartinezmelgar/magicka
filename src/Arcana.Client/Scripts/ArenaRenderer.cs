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
    private sealed class BeamVisual(Node3D root, MeshInstance3D mesh, CylinderMesh shape, ShaderMaterial skin)
    {
        internal readonly Node3D Root = root;
        internal readonly MeshInstance3D Mesh = mesh;
        internal readonly CylinderMesh Shape = shape;
        internal readonly ShaderMaterial Skin = skin;
        internal float Radius = -1, EndRadius = -1, Span = -1, Power = -1;
        internal int Profile = -1, Charges = -1;
        internal Color? Color, Accent;
        internal Material? Material;
    }
    private readonly List<BeamVisual> _beamPool = [];
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
    private bool _mapPrepared;
    internal ArenaMapId CurrentMap => _map.Id;
    private bool _fresh;
    private readonly SpellAudio _audio = new();
    private long _lastEvent;
    private double _time;
    private float _zoom = 27;
    private int _beamUsed, _beamMeshUsed;
    internal int ActiveBeamSegments => _beamUsed;
    internal int BeamGeometryUpdates { get; private set; }
    private Godot.Environment _environment = null!;
    private DirectionalLight3D _sun = null!;
    public bool AudioEnabled { get; set; } = true;
    public bool ShowControlPoints { get; set; } = true;
    public double InterpolationDelay { get; set; } = 1 / 60.0;

    public override void _Ready()
    {
        AddChild(_effects);
        _effects.EnhancedSpells = SpellEffectQuality.Policy.Enhanced;
        if (SpellEffectQuality.Policy.LegacyOnly) _effects.DetailScale = Math.Min(_effects.DetailScale, .6f);
        _effects.FlameOrigin = FlameOrigin;
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
            LightEnergy = 1.10f, ShadowEnabled = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal, DirectionalShadowMaxDistance = 70
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
    internal Vector3? PreviewFocus { get; set; }
    public void Clear()
    {
        _audio.StopAll();
        ClearDefenseVisuals();
        foreach (var v in _wizards.Values) v.QueueFree(); _wizards.Clear();
        foreach (var v in _projectiles.Values) v.QueueFree(); _projectiles.Clear();
        foreach (var v in _walls.Values) v.QueueFree(); _walls.Clear();
        foreach (var v in _fields.Values) v.QueueFree(); _fields.Clear();
        foreach (var v in _pickups.Values) v.QueueFree(); _pickups.Clear();
        _effects.Clear();
        foreach (var point in _points.Values) point.Reset();
        foreach (var beam in _beamPool) beam.Root.Visible = false;
        foreach (var batch in _simpleBeamBatches.Values) batch.Clear();
        foreach (var batch in _beamCoreBatches.Values) batch.Clear();
        _beamUsed = _beamMeshUsed = 0; BeamGeometryUpdates = 0;
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

    private void BuildMap()
    {
        foreach (var step in PrepareMap(_map, true)) step();
    }
    internal IEnumerable<Action> PrepareMap(ArenaDefinition definition, bool force = false)
    {
        if (!force && _map.Id == definition.Id && _map.SurvivalMap == definition.SurvivalMap && _mapPrepared) yield break;
        Node3D? rootMap = null;
        yield return () =>
        {
            Clear(); _map = definition; _mapPrepared = false;
            if (_mapRoot != null) { RemoveChild(_mapRoot); _mapRoot.QueueFree(); }
            _survivalSectors.Clear();
            _preparedSurvivalSectors.Clear(); _survivalWindow = null; SurvivalSectorBuilds = 0;
            _points.Clear(); _portals.Clear(); _frontlineSpawns.Clear(); _practiceCasterLabel = null;
            rootMap = new Node3D { Name = "MapGeometry" }; _mapRoot = rootMap; AddChild(rootMap);
        };
        // La enumeración prepara trabajos; los nodos se crean al ejecutar cada paso.
        if (definition.Id != ArenaMapId.Bastion)
            foreach (var step in PrepareScenery(() => rootMap!, definition)) yield return step;
        yield return () => { BuildMapDetails(rootMap!); _mapPrepared = true; };
    }
    private void BuildMapDetails(Node3D rootMap)
    {
        if (_map.IsPractice) { BuildPracticeMap(rootMap); return; }
        if (_map.Id == ArenaMapId.Bastion) { BuildFrontline(rootMap); return; }
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
        var definition = ArenaMap.Get(snapshot);
        if (_map.Id != definition.Id || _map.SurvivalMap != definition.SurvivalMap || !_mapPrepared)
        { Clear(); _map = definition; BuildMap(); }
        bool first = _lastSnapshot == null;
        var previous = _lastSnapshot;
        float dt = (float)delta; _time += delta;
        _effects.EnhancedSpells = SpellEffectQuality.Policy.Enhanced;
        if (SpellEffectQuality.Policy.LegacyOnly) _effects.DetailScale = Math.Min(_effects.DetailScale, .6f);
        _fresh = !ReferenceEquals(snapshot, _lastSnapshot); _lastSnapshot = snapshot;
        var local = snapshot.Players.FirstOrDefault(p => p.Id == localId);
        UpdateSurvivalGeometry(PreviewFocus is { } preview ? new(preview.X, preview.Z) : local?.Position ?? V2.Zero);
        foreach (var p in snapshot.Players)
        {
            if (!_wizards.TryGetValue(p.Id, out var visual))
            {
                visual = new WizardVisual(); AddChild(visual); visual.Build(p, p.Id == localId); _wizards[p.Id] = visual;
            }
            visual.Visible = NearSurvival(p.Position);
            if (visual.Visible) visual.Update(p, dt, (float)_time, Camera, snapshot.Time, InterpolationDelay);
        }
        if (_fresh) RemoveMissing(_wizards, snapshot.Players, static p => p.Id);
        RenderDefense(snapshot, localId, dt);
        float followX = _map.Id == ArenaMapId.Classic ? 7 : _map.HalfWidth - 8;
        float followY = _map.Id == ArenaMapId.Classic ? 4 : _map.HalfHeight - 7;
        var visiblePosition = local != null ? _wizards[local.Id].Position : Vector3.Zero;
        var focus = PreviewFocus ?? (playing && local != null ? new Vector3(Math.Clamp(visiblePosition.X, -followX, followX), 0,
            Math.Clamp(visiblePosition.Z, -followY, followY)) : new Vector3(-4, 0, 0));
        var desired = focus + new Vector3(0, 27, 21);
        var previousLocal = previous?.Players.FirstOrDefault(p => p.Id == localId);
        bool respawned = local is { Alive: true } && previousLocal != null &&
            (!previousLocal.Alive || previousLocal.Deaths != local.Deaths);
        bool teleported = _map.Id == ArenaMapId.Bastion && previousLocal != null && local != null && V2.Distance(previousLocal.Position, local.Position) > 12;
        Camera.Position = playing && (first || respawned || teleported) ? desired : Camera.Position.Lerp(desired, 1 - Mathf.Exp(-3 * dt));
        Camera.LookAt(Camera.Position - new Vector3(0, 27, 21));
        Camera.Size = Mathf.Lerp(Camera.Size, playing ? _zoom : 32, 1 - Mathf.Exp(-5 * dt));
        foreach (var visual in _points.Values) visual.Visible = ShowControlPoints && snapshot.Mode == GameMode.Warfare;
        foreach (var point in snapshot.Points)
        {
            var visual = _points[point.Id];
            visual.Update(point, dt, (float)_time);
        }
        UpdateFrontline(snapshot, (float)_time);
        if (_practiceCasterLabel != null && _practiceCasterLabel.Text != PracticeCasterDescription)
            _practiceCasterLabel.Text = PracticeCasterDescription;
        var visibleState = snapshot.Defense == null ? snapshot : snapshot with
        {
            Players = snapshot.Players.Where(p => NearSurvival(p.Position)).ToArray(),
            Projectiles = snapshot.Projectiles.Where(p => NearSurvival(p.Position)).ToArray(),
            Walls = snapshot.Walls.Where(p => NearSurvivalSegment(p.A, p.B)).ToArray(),
            Fields = snapshot.Fields.Where(p => NearSurvival(p.Position, p.Radius + 4)).ToArray(),
            Beams = snapshot.Beams.Where(p => NearSurvivalSegment(p.From, p.To)).ToArray()
        };
        DrawProjectiles(visibleState, dt);
        DrawWalls(visibleState, dt);
        DrawFields(visibleState, dt);
        foreach (var item in snapshot.Pickups ?? [])
        {
            if (!_pickups.TryGetValue(item.Id, out var visual))
            { visual = new RelicVisual(); AddChild(visual); visual.Build(item); _pickups[item.Id] = visual; }
            visual.Update(item, (float)_time);
        }
        if (_fresh) RemoveMissing(_pickups, snapshot.Pickups ?? [], static p => p.Id);
        _beamUsed = _beamMeshUsed = 0; BeamGeometryUpdates = 0;
        foreach (var batch in _simpleBeamBatches.Values) batch.Begin();
        foreach (var batch in _beamCoreBatches.Values) batch.Begin();
        // Dibuja primero todos los canales; el presupuesto de detalle nunca oculta un rayo.
        foreach (var beam in visibleState.Beams) DrawBeam(beam, false);
        foreach (var beam in visibleState.Beams) DrawBeam(beam, true);
        foreach (var batch in _simpleBeamBatches.Values) batch.Commit();
        foreach (var batch in _beamCoreBatches.Values) batch.Commit();
        for (int i = _beamMeshUsed; i < _beamPool.Count; i++) _beamPool[i].Root.Visible = false;
        foreach (var e in snapshot.Events)
        {
            if (e.Id <= _lastEvent) continue;
            if (playing && (NearSurvival(e.Position) || e.Kind == "passive_chain"))
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
        _effects.Billboard = Camera.GlobalBasis;
        _effects.Advance(visibleState, dt, playing);
    }

    private void RemoveMissing<T, TState>(Dictionary<int, T> nodes, TState[] present, Func<TState, int> id) where T : Node
    {
        _present.Clear(); _removed.Clear();
        foreach (var item in present) _present.Add(id(item));
        foreach (int key in nodes.Keys) if (!_present.Contains(key)) _removed.Add(key);
        foreach (int key in _removed) { nodes[key].QueueFree(); nodes.Remove(key); }
    }

    private void DrawProjectiles(WorldSnapshot snapshot, float dt)
    {
        foreach (var projectile in snapshot.Projectiles)
        {
            if (!_projectiles.TryGetValue(projectile.Id, out var root))
            {
                root = new Node3D { Position = World(projectile.Position, 0.9f) }; AddChild(root);
                bool stone = projectile.Elements.Contains(Element.Earth);
                bool ice = !stone && projectile.Elements.Contains(Element.Cold);
                var imbuements = stone ? StoneVisuals.Imbuements(projectile.Elements) : [];
                var color = ice ? ElementChannelVisuals.IceBody : Ui.SpellColor(projectile.Elements);
                if (stone)
                {
                    root.AddChild(new MeshInstance3D { Name = "StoneCore", Mesh = StoneVisuals.Shape(projectile.Id),
                        Scale = Vector3.One * projectile.Radius, MaterialOverride = StoneVisuals.Surface(projectile.Elements) });
                    if (StoneVisuals.Aura(projectile.Elements) is { } aura)
                        root.AddChild(new MeshInstance3D { Name = "StoneAura", Mesh = StoneVisuals.Shape(projectile.Id),
                            Scale = Vector3.One * (projectile.Radius + 0.10f + (projectile.Elements.Length - 2) * 0.025f),
                            MaterialOverride = aura, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                }
                else if (ice)
                {
                    var core = Mesh(root, ElementChannelVisuals.LegacyIce, Vector3.Zero, color, .35f);
                    core.Name = "IceCore";
                    core.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                }
                else Mesh(root, new SphereMesh { Radius = projectile.Radius, Height = projectile.Radius * 2 }, Vector3.Zero, color, 0.35f);
                var strength = SpellVisualStrength.For(projectile.Elements);
                for (int i = 0; i < strength.Charges - 1; i++)
                {
                    color = imbuements.Length > 0 ? StoneVisuals.AuraColor(imbuements[i % imbuements.Length]) :
                        ice ? ElementChannelVisuals.IceBody : Ui.SpellColor(projectile.Elements);
                    color.A = imbuements.Length > 0 ? 0.62f : 0.3f;
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
            if (projectile.Elements.Contains(Element.Cold) && !projectile.Elements.Contains(Element.Earth))
            {
                var core = root.GetNode<MeshInstance3D>("IceCore");
                Godot.Mesh mesh = _effects.EnhancedSpells ? ElementChannelVisuals.Mesh(ElementChannelVisuals.Ice) : ElementChannelVisuals.LegacyIce;
                Godot.Material material = _effects.EnhancedSpells ? ElementChannelVisuals.ProjectileIceMaterial() : Material(ElementChannelVisuals.IceBody, .35f);
                if (core.Mesh != mesh) core.Mesh = mesh;
                if (core.MaterialOverride != material) core.MaterialOverride = material;
                core.Basis = _effects.EnhancedSpells
                    ? (root.GlobalBasis.Inverse() * Camera.GlobalBasis * new Basis(Vector3.Back, (float)_time * 2 + projectile.Id)).Scaled(Vector3.One * .38f)
                    : Basis.Identity;
            }
        }
        if (_fresh || snapshot.Defense != null) RemoveMissing(_projectiles, snapshot.Projectiles, static p => p.Id);
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
        if (_fresh || snapshot.Defense != null) RemoveMissing(_walls, snapshot.Walls, static p => p.Id);
    }
    private void DrawFields(WorldSnapshot snapshot, float delta)
    {
        foreach (var field in snapshot.Fields)
        {
            if (field.Kind == "oracle_bombardment")
            {
                if (!_fields.TryGetValue(field.Id, out var existingStrike))
                {
                    var strike = new OracleStrikeVisual(); AddChild(strike); strike.Build(field);
                    _fields[field.Id] = existingStrike = strike;
                }
                ((OracleStrikeVisual)existingStrike).Update(field, (float)_time);
                continue;
            }
            if (field.Kind is "meteor" or "defense_meteor")
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
                var area = new AreaFieldVisual(); AddChild(area); area.Build(field);
                _fields[field.Id] = root = area;
            }
            ((AreaFieldVisual)root).Update(field, (float)_time);
        }
        if (_fresh || snapshot.Defense != null) RemoveMissing(_fields, snapshot.Fields, static p => p.Id);
    }

}
