using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Efectos locales. Reutiliza nodos y mallas con límites fijos; nunca modifica el combate.</summary>
public partial class CombatEffects : Node3D
{
    public float DetailScale { get; set; } = 1;
    public const int ParticleLimit = 320, RingLimit = 24, EmissionBudget = 64;
    private sealed class Spark(MeshInstance3D node)
    {
        public readonly MeshInstance3D Node = node;
        public Vector3 Velocity, Size;
        public float Life, Duration, Gravity;
        public bool FixedSpan;
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
            var node = new MeshInstance3D { Mesh = _shapes[0], Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(node); _freeSparks.Push(new(node)); _sparkCount++;
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
            var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(node);
            spark = new(node); _sparkCount++;
        }
        spark.Node.Mesh = _shapes[shape]; spark.Node.MaterialOverride = shape == 4 ? AreaVisuals.Skin(color) : Material(color, shape == 3);
        spark.Node.Position = position; spark.Node.Rotation = Vector3.Zero; spark.Node.Visible = true;
        spark.Size = shape is 0 or 3 ? Vector3.One * size : new(size, size * 2.2f, size);
        spark.Node.Scale = spark.Size; spark.Velocity = velocity; spark.Gravity = gravity;
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

    public void Play(GameEvent e)
    {
        if (e.Kind == "passive_chain") { _chainFrom = e.Position; _chainLinks = 0; return; }
        if (e.Kind is not ("cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink" or
            "blink_arrive" or "barrier" or "shield_hit" or "shield_break" or "hurt" or "splash" or "mine_heal" or "mine_place" or "beam_merge" or "beam_refract")) return;
        SeenKinds.Add(e.Kind);
        if (e.Kind == "explosion" && e.Text is "Meteoro" or "Meteorito")
        {
            SeenCharges.Add(e.Text == "Meteoro" ? 3 : 2); MeteorImpact(e); return;
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
        int count = (int)MathF.Round(basis * strength.BurstDensity);
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
        if (e.Kind is "nova" or "explosion" or "splash" or "mine_heal")
        {
            float radius = e.Radius > 0 ? e.Radius : e.Kind == "nova" ? 4 : 2.4f;
            AreaVisuals.Pattern? pattern = e.Text == "Berserker" ? AreaVisuals.Pattern.Berserker
                : e.Kind == "nova" && ice ? AreaVisuals.Pattern.FrostPulse : null;
            Ring(e.Position, color, 0.4f, radius, 0.45f, pattern: pattern);
            if (strength.Charges == 3) Ring(e.Position, color, 0.2f, radius * 0.72f, 0.32f);
        }
        else if (e.Kind is "beam_merge" or "beam_refract") Ring(e.Position, color, 0.1f, 0.6f, 0.25f);
        else if (e.Kind is "blink" or "blink_arrive") Ring(e.Position, color, 0.2f, 1.7f, 0.45f);
        else if (shield || e.Kind is "self" or "barrier" or "cast")
            Ring(e.Position, color, 0.4f, shield ? 1.6f : 0.7f + strength.Charges * 0.15f, 0.32f);
    }

    private void MeteorImpact(GameEvent e)
    {
        bool passive = e.Text == "Meteorito";
        var origin = ArenaRenderer.World(e.Position, 0.25f);
        for (int i = 0; i < (passive ? 8 : 10); i++)
            Emit(origin, StoneVisuals.ChipColor(i), Direction() * 4.3f, 0.65f, 3, 0.19f, 7);
        for (int i = 0; i < (passive ? 10 : 14); i++)
            Emit(origin, i % 3 == 0 ? new("ffe1a3") : Ui.ElementColor(Element.Fire),
                Direction() * 4.8f, 0.48f, 2, 0.14f, 3);
        for (int i = 0; i < (passive ? 4 : 6); i++)
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
                spark.Node.Scale = spark.Size; spark.Node.Rotation = new(0, MathF.Atan2(-direction.Z, direction.X), 0);
            }
        }
        // El impacto y el enlace reemplazan las tres chispas genéricas; no añaden emisiones.
        var bolt = Emit(ArenaRenderer.World(target, 0.35f), color, Vector3.Zero, 0.3f, 4, 1, 0);
        if (bolt != null)
        {
            bolt.FixedSpan = true; bolt.Size = new(1.8f, 1, 1); bolt.Node.Scale = bolt.Size;
            bolt.Node.Rotation = new(0, Mathf.Pi / 2, Mathf.Pi / 2);
        }
        for (int i = 0; i < (link ? 1 : 2); i++)
            Emit(ArenaRenderer.World(target, 0.8f), color, Direction() * 1.2f, 0.22f, 1, 0.1f, 0);
        if (_chainFrom.HasValue) { _chainFrom = target; _chainLinks++; }
    }

    public void Advance(WorldSnapshot snapshot, float delta, bool playing)
    {
        float dt = Math.Clamp(delta, 0, 0.05f);
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i]; p.Life -= dt;
            if (p.Life <= 0)
            {
                p.Node.Visible = false; _freeSparks.Push(p); _sparks[i] = _sparks[^1]; _sparks.RemoveAt(_sparks.Count - 1); continue;
            }
            if (p.FixedSpan)
                p.Node.Scale = new(p.Size.X, 1, Math.Max(0.04f, p.Life / p.Duration));
            else
            {
                p.Velocity -= Vector3.Up * p.Gravity * dt; p.Node.Position += p.Velocity * dt;
                p.Node.Scale = p.Size * Math.Max(0.04f, p.Life / p.Duration); p.Node.RotateY(dt * 4);
            }
        }
        for (int i = _waves.Count - 1; i >= 0; i--)
        {
            var w = _waves[i]; w.Life -= dt;
            if (w.Life <= 0)
            {
                w.Node.Visible = false; _freeWaves.Push(w); _waves[i] = _waves[^1]; _waves.RemoveAt(_waves.Count - 1); continue;
            }
            float radius = Mathf.Lerp(w.Start, w.End, 1 - w.Life / w.Duration);
            w.Node.Scale = new(radius, Math.Max(0.03f, w.Life / w.Duration) * 0.6f, radius);
        }
        if (!playing) { FinishFrame(); return; }
        _trailClock += dt; _statusClock += dt;
        if (_trailClock >= 0.065f)
        {
            _trailClock %= 0.065f;
            foreach (var p in snapshot.Projectiles)
            {
                bool stone = p.Elements.Contains(Element.Earth);
                var imbuements = stone ? StoneVisuals.Imbuements(p.Elements) : [];
                var strength = SpellVisualStrength.For(p.Elements); SeenCharges.Add(strength.Charges);
                var color = Ui.SpellColor(p.Elements);
                var forward = ArenaRenderer.World(p.Direction);
                var side = forward.Cross(Vector3.Up);
                for (int i = 0; i < strength.TrailSamples; i++)
                {
                    float offset = (i - (strength.TrailSamples - 1) * 0.5f) * 0.085f;
                    Element? infusion = imbuements.Length > 0 && i > 0 ? imbuements[(i - 1) % imbuements.Length] : null;
                    Emit(ArenaRenderer.World(p.Position, 0.9f) + side * offset,
                        infusion is { } element ? StoneVisuals.AuraColor(element) : stone ? StoneVisuals.ChipColor(i) : color,
                        forward * -1.3f + side * offset, strength.Lifetime,
                        infusion is { } imbued ? StoneVisuals.ParticleShape(imbued) : stone ? 3 : p.Elements.Contains(Element.Cold) ? 1 : 0,
                        strength.SparkSize, stone && infusion == null ? 3 : 0);
                }
            }
            foreach (var b in snapshot.Beams)
            {
                var strength = SpellVisualStrength.For(b.Elements); SeenCharges.Add(strength.Charges);
                bool cone = b.Shape == SpellShape.Spray;
                bool fire = cone && b.Elements.Contains(Element.Fire);
                bool frost = cone && b.Elements.Contains(Element.Cold);
                bool water = cone && b.Elements.Contains(Element.Water);
                var forward = ArenaRenderer.World(b.To - b.From).Normalized();
                var side = forward.Cross(Vector3.Up).Normalized();
                var baseColor = Ui.SpellColor(b.Elements);
                int samples = strength.ChannelSamples + (cone ? strength.Charges : 0);
                for (int i = 0; i < samples; i++)
                {
                    float t = (i + (float)_random.NextDouble()) / samples;
                    float width = cone ? V2.Distance(b.From, b.To) * 0.18f * t : 0;
                    float lateral = ((float)_random.NextDouble() * 2 - 1) * width;
                    var at = ArenaRenderer.World(V2.Lerp(b.From, b.To, t), 1)
                        + side * lateral;
                    Color accent = fire ? new Color("ffd78b") : frost ? new Color("d5f5ff")
                        : water ? new Color("92eaff") : baseColor;
                    Color particleColor = i % 3 == 0 ? accent : baseColor;
                    Emit(at, particleColor, forward * (cone ? 1.55f : 0.35f)
                        + side * (cone ? lateral * 0.38f : 0) + Vector3.Up * (fire ? 0.45f : 0.06f),
                        cone ? 0.30f : 0.20f,
                        fire ? 2 : frost ? 1 : 0,
                        strength.SparkSize * (cone ? 0.92f : 0.7f), fire ? -0.15f : 0);
                }
            }
            foreach (var meteor in snapshot.Fields)
            {
                if (meteor.Kind != "meteor") continue;
                var at = ArenaRenderer.World(meteor.Position) + MeteorVisual.FlightPosition(meteor.Lifetime);
                var trail = MeteorVisual.TrailDirection(meteor.Lifetime);
                for (int i = 0; i < 3; i++)
                    Emit(at + trail * (0.6f + i * 0.8f) + Direction() * 0.25f,
                        i == 2 ? new Color(0.23f, 0.19f, 0.16f, 0.26f) : i == 0 ? new("ffe4ad") : Ui.ElementColor(Element.Fire),
                        trail * 0.9f, i == 2 ? 0.55f : 0.38f, i == 2 ? 0 : 2, i == 2 ? 0.28f : 0.12f, 0);
            }
        }
        if (_statusClock >= 0.11f)
        {
            _statusClock %= 0.11f;
            foreach (var p in snapshot.Players)
            {
                if (!p.Alive) continue;
                var at = ArenaRenderer.World(p.Position, 0.55f);
                if (p.Burning > 0) for (int i = 0; i < 3; i++) Emit(at + Direction() * 0.35f, Ui.ElementColor(Element.Fire), Vector3.Up * 1.5f, 0.55f, 2, 0.24f, -0.3f);
                if (p.Wet > 0) Emit(at + Vector3.Up * 0.9f + Direction() * 0.3f, Ui.ElementColor(Element.Water), Vector3.Down * 1.6f, 0.5f, 0, 0.16f);
                if (p.Chill > 20 || p.Frozen > 0) Emit(at + Direction() * 0.5f, Ui.ElementColor(Element.Cold), Direction() * 0.6f, 0.6f, 1, 0.15f, 0.2f);
                if (p.Haste > 0) Emit(at, Ui.Energy, -ArenaRenderer.World(p.Facing) * 1.8f, 0.4f, 0, 0.24f, 0);
                if (p.RegenerationRate > 0) Emit(at + Direction() * 0.4f, Ui.Health, Vector3.Up * 1.3f, 0.7f, 0, 0.20f, -0.2f);
                if (p.Casting)
                {
                    var elements = p.CastingElements is { Length: > 0 } ? p.CastingElements : p.Selected;
                    if (elements.Length == 0) continue;
                    var strength = SpellVisualStrength.For(elements); SeenCharges.Add(strength.Charges);
                    var imbuements = elements.Contains(Element.Earth) ? StoneVisuals.Imbuements(elements) : [];
                    for (int i = 0; i < strength.ChannelSamples; i++)
                    {
                        Element? infusion = imbuements.Length > 0 && i > 0 ? imbuements[(i - 1) % imbuements.Length] : null;
                        Emit(at + Vector3.Up * 0.55f + Direction() * 0.22f,
                            infusion is { } element ? StoneVisuals.AuraColor(element) : elements.Contains(Element.Earth) ? StoneVisuals.ChipColor(i) : Ui.SpellColor(elements),
                            Direction() * 0.4f, 0.24f, infusion is { } imbued ? StoneVisuals.ParticleShape(imbued) : elements.Contains(Element.Earth) ? 3 : 0,
                            strength.SparkSize * 0.75f, 0);
                    }
                }
            }
            foreach (var field in snapshot.Fields)
            {
                if (field.Kind is not ("burning_ground" or "frost_ground" or "water_pool")) continue;
                var strength = SpellVisualStrength.For(field.Elements);
                for (int i = 0; i < strength.ChannelSamples; i++)
                {
                    var at = ArenaRenderer.World(field.Position, 0.15f) + Direction() * (field.Radius * 0.7f);
                    Emit(at, Ui.SpellColor(field.Elements), Vector3.Up * (field.Kind == "burning_ground" ? 1.4f : 0.35f),
                        0.35f, field.Kind == "burning_ground" ? 2 : field.Kind == "frost_ground" ? 1 : 0, strength.SparkSize, 0);
                }
            }
            foreach (var wall in snapshot.Walls)
            {
                if (wall.Health <= 0 || wall.Lifetime <= 0) continue;
                bool fire = wall.Elements.Contains(Element.Fire), water = wall.Elements.Contains(Element.Water);
                bool steam = fire && water, ice = water && wall.Elements.Contains(Element.Cold);
                var at = ArenaRenderer.World(V2.Lerp(wall.A, wall.B, (float)_random.NextDouble()), 0.4f);
                if (steam)
                    Emit(at + Vector3.Up * 0.6f, new Color(0.73f, 0.85f, 0.88f, 0.3f), Vector3.Up * 0.8f, 0.55f, 0, 0.22f, 0);
                else if (fire)
                    Emit(at + Vector3.Up * (wall.Elements.Contains(Element.Earth) ? 1.1f : 0.4f), Ui.ElementColor(Element.Fire), Vector3.Up * 1.5f, 0.4f, 2, 0.12f, 0);
                if (water && !steam && !ice)
                    Emit(at - Vector3.Up * 0.25f, Ui.IsMobile ? Ui.ElementColor(Element.Cold) : new("a0e6ff"), Direction() * 0.75f, 0.28f, 0, 0.08f, 2);
                if (wall.Elements.Contains(Element.Lightning))
                    Emit(at + Vector3.Up * 0.6f, Ui.IsMobile ? Ui.ElementColor(Element.Lightning) : new("c9cdff"), Direction() * 1.7f, 0.16f, 1, 0.055f, 0);
            }
        }
        FinishFrame();
    }
    private void FinishFrame()
    {
        EmittedLastFrame = _emittedThisFrame; _emittedThisFrame = 0;
        // No unir tandas distintas si una base de red descartó la cabecera de origen.
        _chainFrom = null; _chainLinks = 0;
    }
    public void Clear()
    {
        foreach (var p in _sparks) { p.Node.Visible = false; _freeSparks.Push(p); } _sparks.Clear();
        foreach (var w in _waves) { w.Node.Visible = false; _freeWaves.Push(w); } _waves.Clear();
        _trailClock = _statusClock = 0; _emittedThisFrame = EmittedLastFrame = 0;
        _chainFrom = null; _chainLinks = 0;
        SeenKinds.Clear(); SeenCharges.Clear();
    }
}
