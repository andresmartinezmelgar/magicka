using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Efectos locales. Reutiliza nodos y mallas con límites fijos; nunca modifica el combate.</summary>
public partial class CombatEffects : Node3D
{
    public const int ParticleLimit = 320, RingLimit = 24, EmissionBudget = 64;
    private sealed class Spark(MeshInstance3D node)
    {
        public readonly MeshInstance3D Node = node;
        public Vector3 Velocity, Size;
        public float Life, Duration, Gravity;
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
    private readonly Dictionary<Color, StandardMaterial3D> _materials = [];
    private readonly Random _random = new(37);
    private readonly Godot.Mesh[] _shapes =
    [
        new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 8, Rings = 4 },
        new PrismMesh { Size = Vector3.One },
        new CylinderMesh { TopRadius = 0, BottomRadius = 0.5f, Height = 1, RadialSegments = 6 }
    ];
    private readonly TorusMesh _ring = new() { InnerRadius = 0.94f, OuterRadius = 1, Rings = 32, RingSegments = 6 };
    private readonly SphereMesh _blast = new() { Radius = 1, Height = 0.8f, RadialSegments = 20, Rings = 8 };
    private int _sparkCount, _waveCount, _emittedThisFrame;
    private float _trailClock, _statusClock;
    public int ActiveCount => _sparks.Count + _waves.Count;
    public int PooledCount => _sparkCount + _waveCount;
    internal int ActiveParticles => _sparks.Count;
    internal int ActiveRings => _waves.Count;
    internal int EmittedLastFrame { get; private set; }
    public HashSet<string> SeenKinds { get; } = [];
    internal HashSet<int> SeenCharges { get; } = [];

    private StandardMaterial3D Material(Color color)
    {
        if (_materials.TryGetValue(color, out var material)) return material;
        material = new StandardMaterial3D
        {
            AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.35f,
            Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled
        };
        _materials.Add(color, material); return material;
    }
    private void Emit(Vector3 position, Color color, Vector3 velocity, float duration = 0.45f,
        int shape = 0, float size = 0.18f, float gravity = 2)
    {
        if (_emittedThisFrame >= EmissionBudget) return;
        Spark spark;
        if (_freeSparks.Count > 0) spark = _freeSparks.Pop();
        else
        {
            if (_sparkCount >= ParticleLimit) return;
            var node = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(node);
            spark = new(node); _sparkCount++;
        }
        spark.Node.Mesh = _shapes[shape]; spark.Node.MaterialOverride = Material(color);
        spark.Node.Position = position; spark.Node.Rotation = Vector3.Zero; spark.Node.Visible = true;
        spark.Size = shape == 0 ? Vector3.One * size : new(size, size * 2.2f, size);
        spark.Node.Scale = spark.Size; spark.Velocity = velocity; spark.Gravity = gravity;
        spark.Life = spark.Duration = duration; _sparks.Add(spark); _emittedThisFrame++;
    }
    private void Ring(V2 position, Color color, float start, float end, float duration, bool blast = false)
    {
        Wave wave;
        if (_freeWaves.Count > 0) wave = _freeWaves.Pop();
        else
        {
            if (_waveCount >= RingLimit) return;
            var node = new MeshInstance3D { Mesh = _ring, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; AddChild(node);
            wave = new(node); _waveCount++;
        }
        wave.Node.MaterialOverride = Material(color); wave.Node.Position = ArenaRenderer.World(position, 0.09f);
        wave.Node.Mesh = blast ? _blast : _ring;
        wave.Node.Visible = true; wave.Start = start; wave.End = end;
        wave.Life = wave.Duration = duration; wave.Node.Scale = new(start, 0.5f, start); _waves.Add(wave);
    }
    private Vector3 Direction() => new Vector3((float)_random.NextDouble() * 2 - 1,
        0.3f + (float)_random.NextDouble(), (float)_random.NextDouble() * 2 - 1).Normalized();

    public void Play(GameEvent e)
    {
        if (e.Kind is not ("cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink" or
            "blink_arrive" or "barrier" or "shield_hit" or "shield_break" or "hurt" or "splash" or "mine_heal" or "mine_place" or "beam_merge" or "beam_refract")) return;
        SeenKinds.Add(e.Kind);
        if (e.Kind == "explosion" && e.Text == "Meteoro")
        {
            SeenCharges.Add(3); MeteorImpact(e); return;
        }
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
        int shape = shield || ice || e.Elements.Contains(Element.Earth) ? 1 : fire ? 2 : 0;
        var origin = ArenaRenderer.World(e.Position, e.Kind == "explosion" ? 0.3f : 1.1f);
        color.A = 0.88f;
        for (int i = 0; i < count; i++) Emit(origin, color, Direction() * speed,
            strength.Lifetime + (float)_random.NextDouble() * 0.08f, shape, strength.SparkSize * (e.Kind == "explosion" ? 1.2f : 1));
        color.A = 0.42f;
        if (e.Kind is "nova" or "explosion" or "splash" or "mine_heal")
        {
            float radius = e.Radius > 0 ? e.Radius : e.Kind == "nova" ? 4 : 2.4f;
            Ring(e.Position, color, 0.4f, radius, 0.45f);
            if (strength.Charges == 3) Ring(e.Position, color, 0.2f, radius * 0.72f, 0.32f);
        }
        else if (e.Kind is "beam_merge" or "beam_refract") Ring(e.Position, color, 0.1f, 0.6f, 0.25f);
        else if (e.Kind is "blink" or "blink_arrive") Ring(e.Position, color, 0.2f, 1.7f, 0.45f);
        else if (shield || e.Kind is "self" or "barrier" or "cast")
            Ring(e.Position, color, 0.4f, shield ? 1.6f : 0.7f + strength.Charges * 0.15f, 0.32f);
    }

    private void MeteorImpact(GameEvent e)
    {
        var origin = ArenaRenderer.World(e.Position, 0.25f);
        for (int i = 0; i < 10; i++)
            Emit(origin, new("665044"), Direction() * 4.3f, 0.65f, 1, 0.19f, 7);
        for (int i = 0; i < 14; i++)
            Emit(origin, i % 3 == 0 ? new("ffe1a3") : Ui.ElementColor(Element.Fire),
                Direction() * 4.8f, 0.48f, 2, 0.14f, 3);
        for (int i = 0; i < 6; i++)
            Emit(origin + Direction() * 0.5f, new Color(0.22f, 0.18f, 0.15f, 0.3f),
                Vector3.Up * 1.2f + Direction() * 0.5f, 0.85f, 0, 0.48f, -0.2f);
        float radius = e.Radius > 0 ? e.Radius : 4;
        Ring(e.Position, new Color(1, 0.45f, 0.12f, 0.4f), 0.45f, radius, 0.5f);
        Ring(e.Position, new Color(0.55f, 0.42f, 0.29f, 0.3f), 0.25f, radius * 0.78f, 0.4f);
        Ring(e.Position, new Color(1, 0.6f, 0.2f, 0.24f), 0.2f, radius * 0.6f, 0.2f, blast: true);
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
            p.Velocity -= Vector3.Up * p.Gravity * dt; p.Node.Position += p.Velocity * dt;
            p.Node.Scale = p.Size * Math.Max(0.04f, p.Life / p.Duration); p.Node.RotateY(dt * 4);
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
                var strength = SpellVisualStrength.For(p.Elements); SeenCharges.Add(strength.Charges);
                var color = Ui.SpellColor(p.Elements);
                var forward = ArenaRenderer.World(p.Direction);
                var side = forward.Cross(Vector3.Up);
                for (int i = 0; i < strength.TrailSamples; i++)
                {
                    float offset = (i - (strength.TrailSamples - 1) * 0.5f) * 0.085f;
                    Emit(ArenaRenderer.World(p.Position, 0.9f) + side * offset, color,
                        forward * -1.3f + side * offset, strength.Lifetime,
                        p.Elements.Contains(Element.Cold) ? 1 : 0, strength.SparkSize, 0);
                }
            }
            foreach (var b in snapshot.Beams)
            {
                var strength = SpellVisualStrength.For(b.Elements); SeenCharges.Add(strength.Charges);
                for (int i = 0; i < strength.ChannelSamples; i++)
                {
                    float t = (i + (float)_random.NextDouble()) / strength.ChannelSamples;
                    Emit(ArenaRenderer.World(V2.Lerp(b.From, b.To, t), 1), Ui.SpellColor(b.Elements),
                        Direction() * 0.35f, 0.20f, 0, strength.SparkSize * 0.7f, 0);
                }
            }
            foreach (var meteor in snapshot.Fields.Where(f => f.Kind == "meteor"))
            {
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
                    for (int i = 0; i < strength.ChannelSamples; i++)
                        Emit(at + Vector3.Up * 0.55f + Direction() * 0.22f, Ui.SpellColor(elements),
                            Direction() * 0.4f, 0.24f, 0, strength.SparkSize * 0.75f, 0);
                }
            }
            foreach (var field in snapshot.Fields.Where(f => f.Kind is "burning_ground" or "frost_ground" or "water_pool"))
            {
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
                var spell = new SpellRecipe(wall.Elements);
                var at = ArenaRenderer.World(V2.Lerp(wall.A, wall.B, (float)_random.NextDouble()), 0.4f);
                if (spell.Steam)
                    Emit(at + Vector3.Up * 0.6f, new Color(0.73f, 0.85f, 0.88f, 0.3f), Vector3.Up * 0.8f, 0.55f, 0, 0.22f, 0);
                else if (spell.Has(Element.Fire))
                    Emit(at + Vector3.Up * (spell.Has(Element.Earth) ? 1.1f : 0.4f), Ui.ElementColor(Element.Fire), Vector3.Up * 1.5f, 0.4f, 2, 0.12f, 0);
                if (spell.Has(Element.Water) && !spell.Steam && !spell.Ice)
                    Emit(at - Vector3.Up * 0.25f, new("a0e6ff"), Direction() * 0.75f, 0.28f, 0, 0.08f, 2);
                if (spell.Has(Element.Lightning))
                    Emit(at + Vector3.Up * 0.6f, new("c9cdff"), Direction() * 1.7f, 0.16f, 1, 0.055f, 0);
            }
        }
        FinishFrame();
    }
    private void FinishFrame() { EmittedLastFrame = _emittedThisFrame; _emittedThisFrame = 0; }
    public void Clear()
    {
        foreach (var p in _sparks) { p.Node.Visible = false; _freeSparks.Push(p); } _sparks.Clear();
        foreach (var w in _waves) { w.Node.Visible = false; _freeWaves.Push(w); } _waves.Clear();
        _trailClock = _statusClock = 0; _emittedThisFrame = EmittedLastFrame = 0;
        SeenKinds.Clear(); SeenCharges.Clear();
    }
}
