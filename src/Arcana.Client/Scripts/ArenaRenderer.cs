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
    internal event Action<GameEvent>? AudioEvent;
    private bool _quietAudio;
    internal void SetEffectsVolume(float volume) => _audio.SetVolume(volume);
    internal void SetAudioSuspended(bool suspended) => _audio.SetSuspended(suspended);
    internal void SetAudioQuiet(bool quiet)
    {
        if (quiet && !_quietAudio) _audio.StopAll();
        _quietAudio = quiet;
    }
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
        _audio.Enabled = AudioEnabled;
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
        _lastEvent = 0; _lastSnapshot = null; _visibleSource = _visibleState = null;
    }

    public void Render(WorldSnapshot snapshot, int localId, double delta, bool playing, EquipmentOutfit? outfitPreview = null)
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
            if (visual.Visible) visual.Update(p, dt, (float)_time, Camera, snapshot.Time, InterpolationDelay,
                p.Id == localId ? outfitPreview : null);
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
        var visibleState = VisibleState(snapshot);
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
                _effects.Play(e, CombatEffects.FriendlyParticleDensity(visibleState, e.ActorId, e.Position, e.Position, e.Radius));
                if (e.Kind is "cast" or "self" or "nova" or "barrier")
                {
                    // El evento no contiene ID de autor; su posición es la del lanzador.
                    foreach (var p in snapshot.Players)
                        if (p.Alive && V2.Distance(p.Position, e.Position) < 0.15f && _wizards.TryGetValue(p.Id, out var caster)) caster.PlayCastPose(e.Elements);
                }
                if (AudioEnabled && !_quietAudio && local != null)
                {
                    float distance = V2.Distance(e.Position, local.Position);
                    if (distance < 18)
                    {
                        _audio.PlayEvent(e, distance);
                        AudioEvent?.Invoke(e);
                    }
                }
            }
            _lastEvent = Math.Max(_lastEvent, e.Id);
        }
        _effects.Billboard = Camera.GlobalBasis;
        _effects.Advance(visibleState, dt, playing);
        if (playing && AudioEnabled && !_quietAudio && local != null && snapshot.MatchEnded && previous is { MatchEnded: false })
            _audio.PlayResult(AdaptiveAudioMix.Won(snapshot, local));
    }

    private WorldSnapshot? _visibleSource, _visibleState;
    private (V2 Center, float HalfX, float HalfY) _visibleBounds;
    private Predicate<WizardSnapshot>? _visiblePlayerFilter;
    private Predicate<ProjectileSnapshot>? _visibleProjectileFilter;
    private Predicate<WallSnapshot>? _visibleWallFilter;
    private Predicate<FieldSnapshot>? _visibleFieldFilter;
    private Predicate<BeamSnapshot>? _visibleBeamFilter;
}
