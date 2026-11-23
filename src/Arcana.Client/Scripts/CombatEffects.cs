using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Efectos locales. Reutiliza buffers, objetos y mallas con límites fijos; nunca modifica el combate.</summary>
public partial class CombatEffects : Node3D
{
    public float DetailScale { get; set; } = 1;
    public const int ParticleLimit = 320, RingLimit = 24, EmissionBudget = 64;
    private sealed class Spark
    {
        public MeshInstance3D? Node; // Solo los enlaces eléctricos conservan geometría individual.
        public InstanceDrawBatch? Batch;
        public Vector3 Position, Velocity, Size, Scale;
        public float Life, Duration, Gravity, Angle;
        public int Shape;
        public bool FixedSpan;
        public Vector3 FlameOrigin, FlameEnd, FlameForward, FlameSide;
        public float FlameRange, FlameSeed, FlamePower;
    }
    private sealed class Wave(MeshInstance3D node)
    {
        public readonly MeshInstance3D Node = node;
        public float Life, Duration, Start, End;
    }
    private readonly List<Spark> _sparks = [];
    private readonly Stack<Spark> _freeSparks = [];
    private readonly List<Wave> _waves = [];
    private readonly Stack<Wave> _freeWaves = [];
    private readonly Dictionary<(Color Color, bool Stone), StandardMaterial3D> _materials = [];
    private readonly Dictionary<(int Shape, Color Color), InstanceDrawBatch> _particleBatches = [];
    private readonly Random _random = new(37);
    private readonly Godot.Mesh[] _shapes =
    [
        new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 8, Rings = 4 },
        new PrismMesh { Size = Vector3.One },
        new CylinderMesh { TopRadius = 0, BottomRadius = 0.5f, Height = 1, RadialSegments = 6 },
        StoneVisuals.Shape(),
        AreaVisuals.Shape(AreaVisuals.Pattern.LightningLink)
    ];
    private readonly TorusMesh _ring = new() { InnerRadius = 0.94f, OuterRadius = 1, Rings = 32, RingSegments = 6 };
    private readonly SphereMesh _blast = new() { Radius = 1, Height = 0.8f, RadialSegments = 20, Rings = 8 };
    private int _sparkCount, _waveCount, _emittedThisFrame;
    private float _trailClock, _statusClock;
    private V2? _chainFrom;
    private int _chainLinks;
    public int ActiveCount => _sparks.Count + _waves.Count;
    public int PooledCount => _sparkCount + _waveCount;
    internal void PreparePoolBatch()
    {
        for (int i = 0; i < 32 && _sparkCount < ParticleLimit; i++)
        {
            _freeSparks.Push(new()); _sparkCount++;
        }
        for (int i = 0; i < 8 && _waveCount < RingLimit; i++)
        {
            var node = new MeshInstance3D { Mesh = _ring, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node); _freeWaves.Push(new(node)); _waveCount++;
        }
    }
    internal int ActiveParticles => _sparks.Count;
    internal int ActiveRings => _waves.Count;
    internal int EmittedLastFrame { get; private set; }
    public HashSet<string> SeenKinds { get; } = [];
    internal HashSet<int> SeenCharges { get; } = [];

    private StandardMaterial3D Material(Color color, bool stone = false)
    {
        var key = (color, stone);
        if (_materials.TryGetValue(key, out var material)) return material;
        material = new StandardMaterial3D
        {
            AlbedoColor = color, ShadingMode = stone ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded,
            Roughness = 0.97f, VertexColorUseAsAlbedo = stone,
            EmissionEnabled = !stone, Emission = color, EmissionEnergyMultiplier = 0.35f,
            Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled
        };
        _materials.Add(key, material); return material;
    }
    private Spark? Emit(Vector3 position, Color color, Vector3 velocity, float duration = 0.45f,
        int shape = 0, float size = 0.18f, float gravity = 2)
    {
        if (_emittedThisFrame >= Math.Max(16, (int)(EmissionBudget * DetailScale)) ||
            _sparks.Count >= Math.Max(80, (int)(ParticleLimit * DetailScale))) return null;
        Spark spark;
        if (_freeSparks.Count > 0) spark = _freeSparks.Pop();
        else
        {
            if (_sparkCount >= ParticleLimit) return null;
            spark = new(); _sparkCount++;
        }
        spark.Shape = shape; spark.Position = position; spark.Angle = 0;
        spark.Size = shape is 0 or 3 ? Vector3.One * size : new(size, size * 2.2f, size);
        spark.Scale = spark.Size; spark.Velocity = velocity; spark.Gravity = gravity;
        if (shape == 4)
        {
            if (spark.Node == null)
            { spark.Node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(spark.Node); }
            spark.Node.Mesh = _shapes[shape]; spark.Node.MaterialOverride = AreaVisuals.Skin(color);
            spark.Node.Position = position; spark.Node.Rotation = Vector3.Zero; spark.Node.Scale = spark.Size; spark.Node.Visible = true;
            spark.Batch = null;
        }
        else if (shape >= FlameVisuals.Shape)
        {
            if (spark.Node != null) spark.Node.Visible = false;
            spark.Batch = ChannelBatch(shape);
        }
        else
        {
            if (spark.Node != null) spark.Node.Visible = false;
            var key = (shape, color);
            if (!_particleBatches.TryGetValue(key, out var batch))
                _particleBatches[key] = batch = new(this, "ParticleBatch" + _particleBatches.Count,
                    _shapes[shape], Material(color, shape == 3), ParticleLimit);
            spark.Batch = batch;
        }
        spark.FixedSpan = false;
        spark.Life = spark.Duration = duration; _sparks.Add(spark); _emittedThisFrame++; return spark;
    }
    private void Ring(V2 position, Color color, float start, float end, float duration, bool blast = false,
        AreaVisuals.Pattern? pattern = null)
    {
        Wave wave;
        if (_freeWaves.Count > 0) wave = _freeWaves.Pop();
        else
        {
            if (_waveCount >= RingLimit) return;
            var node = new MeshInstance3D { Mesh = _ring, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(node);
            wave = new(node); _waveCount++;
        }
        wave.Node.MaterialOverride = pattern.HasValue ? AreaVisuals.Skin() : Material(color);
        wave.Node.Position = ArenaRenderer.World(position, 0.09f); wave.Node.Rotation = Vector3.Zero;
        wave.Node.Mesh = pattern.HasValue ? AreaVisuals.Shape(pattern.Value) : blast ? _blast : _ring;
        wave.Node.Visible = true; wave.Start = start; wave.End = end;
        wave.Life = wave.Duration = duration; wave.Node.Scale = new(start, 0.5f, start); _waves.Add(wave);
    }
    private Vector3 Direction() => new Vector3((float)_random.NextDouble() * 2 - 1,
        0.3f + (float)_random.NextDouble(), (float)_random.NextDouble() * 2 - 1).Normalized();

    public void Play(GameEvent e, float density = 1)
    {
        if (e.Kind == "passive_chain") { _chainFrom = e.Position; _chainLinks = 0; return; }
        if (e.Kind is not ("cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink" or
            "blink_arrive" or "barrier" or "shield_hit" or "shield_break" or "hurt" or "splash" or "mine_heal" or "mine_place" or "beam_merge" or "beam_refract" or "arcane_burst")) return;
        SeenKinds.Add(e.Kind);
        if (e.Kind == "explosion" && e.Text is "Meteoro" or "Meteorito")
        {
            SeenCharges.Add(e.Text == "Meteoro" ? 3 : 2); MeteorImpact(e, density); return;
        }
        if (e.Kind == "hurt" && e.Text == "Relámpago") { SeenCharges.Add(1); ChainHit(e.Position); return; }
        // Golpes continuos y efectos de estado conservan una respuesta breve, sin multiplicar destellos.
        var strength = e.Kind is "hurt" or "shield_hit" or "shield_break" or "death" or "blink" or "blink_arrive"
            ? new SpellVisualStrength(1) : SpellVisualStrength.For(e.Elements);
        SeenCharges.Add(strength.Charges);
        bool shield = e.Kind is "shield_hit" or "shield_break" || e.Kind == "self" && e.Elements.Contains(Element.Shield);
        bool fire = e.Elements.Contains(Element.Fire), ice = e.Elements.Contains(Element.Cold);
        Color color = e.Kind == "mine_heal" ? Ui.Health : shield ? Ui.Shield : e.Kind is "blink" or "blink_arrive" ? Ui.Energy : Ui.SpellColor(e.Elements);
        int basis = e.Kind switch { "explosion" => 12, "nova" => 10, "shield_break" => 14, "death" => 14, "impact" => 6, "hurt" => 3, _ => 4 };
        int count = Math.Max(1, (int)MathF.Round(basis * strength.BurstDensity * density));
        float speed = e.Kind is "explosion" or "nova" or "shield_break" ? 3.6f : 2.1f;
        bool stone = !shield && e.Elements.Contains(Element.Earth);
        var imbuements = stone ? StoneVisuals.Imbuements(e.Elements) : [];
        if (imbuements.Length > 0) color = new Color(StoneVisuals.AuraColor(imbuements[0]), 0.88f);
        int shape = stone ? 3 : shield || ice ? 1 : fire ? 2 : 0;
        var origin = ArenaRenderer.World(e.Position, e.Kind == "explosion" ? 0.3f : 1.1f);
        color.A = 0.88f;
        for (int i = 0; i < count; i++)
        {
            bool dust = stone && (e.Kind is "impact" or "splash") && i % 4 == 0;
            Element? infusion = !dust && imbuements.Length > 0 && i % 3 == 1 ? imbuements[i / 3 % imbuements.Length] : null;
            Emit(origin, dust ? new Color(0.61f, 0.50f, 0.36f, 0.28f) : infusion is { } element ? StoneVisuals.AuraColor(element) : stone ? StoneVisuals.ChipColor(i) : color,
                Direction() * speed * (dust ? 0.4f : 1),
                strength.Lifetime + (float)_random.NextDouble() * 0.08f, dust ? 0 : infusion is { } imbued ? StoneVisuals.ParticleShape(imbued) : shape,
                strength.SparkSize * (dust ? 2.4f : e.Kind == "explosion" ? 1.2f : 1), stone && !dust && infusion == null ? 5 : 2);
        }
        color.A = 0.42f;
        if (e.Kind is "nova" or "explosion" or "splash" or "mine_heal" or "arcane_burst")
        {
            float radius = e.Radius > 0 ? e.Radius : e.Kind == "nova" ? 4 : 2.4f;
            AreaVisuals.Pattern? pattern = e.Kind == "arcane_burst" ? AreaVisuals.Pattern.ArcaneAura : e.Text == "Berserker" ? AreaVisuals.Pattern.Berserker
                : e.Kind == "nova" && ice ? AreaVisuals.Pattern.FrostPulse : null;
            Ring(e.Position, color, 0.4f, radius, 0.45f, pattern: pattern);
            if (strength.Charges == 3) Ring(e.Position, color, 0.2f, radius * 0.72f, 0.32f);
        }
        else if (e.Kind is "beam_merge" or "beam_refract") Ring(e.Position, color, 0.1f, 0.6f, 0.25f);
        else if (e.Kind is "blink" or "blink_arrive") Ring(e.Position, color, 0.2f, 1.7f, 0.45f);
        else if (shield || e.Kind is "self" or "barrier" or "cast")
            Ring(e.Position, color, 0.4f, shield ? 1.6f : 0.7f + strength.Charges * 0.15f, 0.32f);
    }

    private void MeteorImpact(GameEvent e, float density)
    {
        bool passive = e.Text == "Meteorito";
        var origin = ArenaRenderer.World(e.Position, 0.25f);
        for (int i = 0; i < (passive ? 8 : 10) * density; i++)
            Emit(origin, StoneVisuals.ChipColor(i), Direction() * 4.3f, 0.65f, 3, 0.19f, 7);
        for (int i = 0; i < (passive ? 10 : 14) * density; i++)
            Emit(origin, i % 3 == 0 ? new("ffe1a3") : Ui.ElementColor(Element.Fire),
                Direction() * 4.8f, 0.48f, 2, 0.14f, 3);
        for (int i = 0; i < (passive ? 4 : 6) * density; i++)
            Emit(origin + Direction() * 0.5f, new Color(0.22f, 0.18f, 0.15f, 0.3f),
                Vector3.Up * 1.2f + Direction() * 0.5f, 0.85f, 0, 0.48f, -0.2f);
        float radius = e.Radius > 0 ? e.Radius : 4;
        Ring(e.Position, new Color(1, 0.45f, 0.12f, 0.4f), 0.45f, radius, 0.5f, pattern: AreaVisuals.Pattern.Impact);
        if (passive) return; // Las 22 partículas y la onda sustituyen el presupuesto anterior de la pasiva.
        Ring(e.Position, new Color(0.55f, 0.42f, 0.29f, 0.3f), 0.25f, radius * 0.78f, 0.4f);
        Ring(e.Position, new Color(1, 0.6f, 0.2f, 0.24f), 0.2f, radius * 0.6f, 0.2f, blast: true);
    }

    private void ChainHit(V2 target)
    {
        bool link = _chainFrom is { } from && _chainLinks < 6 && V2.Distance(from, target) is > 0.01f and <= 20.1f;
        var color = new Color("dbd4ff");
        if (link)
        {
            var direction = ArenaRenderer.World(target - _chainFrom!.Value);
            var spark = Emit(ArenaRenderer.World(_chainFrom.Value, 0.85f), color, Vector3.Zero, 0.3f, 4, 1, 0);
            if (spark != null)
            {
                spark.FixedSpan = true; spark.Size = new(direction.Length(), 1, 1);
                spark.Node!.Scale = spark.Size; spark.Node.Rotation = new(0, MathF.Atan2(-direction.Z, direction.X), 0);
            }
        }
        // El impacto y el enlace reemplazan las tres chispas genéricas; no añaden emisiones.
        var bolt = Emit(ArenaRenderer.World(target, 0.35f), color, Vector3.Zero, 0.3f, 4, 1, 0);
        if (bolt != null)
        {
            bolt.FixedSpan = true; bolt.Size = new(1.8f, 1, 1); bolt.Node!.Scale = bolt.Size;
            bolt.Node.Rotation = new(0, Mathf.Pi / 2, Mathf.Pi / 2);
        }
        for (int i = 0; i < (link ? 1 : 2); i++)
            Emit(ArenaRenderer.World(target, 0.8f), color, Direction() * 1.2f, 0.22f, 1, 0.1f, 0);
        if (_chainFrom.HasValue) { _chainFrom = target; _chainLinks++; }
    }

}
