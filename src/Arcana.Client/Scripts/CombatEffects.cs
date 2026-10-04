using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Efectos locales. Reutiliza nodos y mallas con límites fijos; nunca modifica el combate.</summary>
public partial class CombatEffects : Node3D
{
    public const int ParticleLimit = 384, RingLimit = 32;
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
    private int _sparkCount, _waveCount;
    private float _trailClock, _statusClock;
    public int ActiveCount => _sparks.Count + _waves.Count;
    public int PooledCount => _sparkCount + _waveCount;
    public HashSet<string> SeenKinds { get; } = [];

    private StandardMaterial3D Material(Color color)
    {
        if (_materials.TryGetValue(color, out var material)) return material;
        material = new StandardMaterial3D
        {
            AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.8f,
            Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled
        };
        _materials.Add(color, material); return material;
    }
    private void Emit(Vector3 position, Color color, Vector3 velocity, float duration = 0.45f,
        int shape = 0, float size = 0.18f, float gravity = 2)
    {
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
        spark.Life = spark.Duration = duration; _sparks.Add(spark);
    }
    private void Ring(V2 position, Color color, float start, float end, float duration)
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
        wave.Node.Visible = true; wave.Start = start; wave.End = end;
        wave.Life = wave.Duration = duration; wave.Node.Scale = new(start, 0.5f, start); _waves.Add(wave);
    }
    private Vector3 Direction() => new Vector3((float)_random.NextDouble() * 2 - 1,
        0.3f + (float)_random.NextDouble(), (float)_random.NextDouble() * 2 - 1).Normalized();

    public void Play(GameEvent e)
    {
        if (e.Kind is not ("cast" or "self" or "nova" or "impact" or "explosion" or "death" or "blink" or
            "blink_arrive" or "barrier" or "shield_hit" or "shield_break" or "hurt")) return;
        SeenKinds.Add(e.Kind);
        bool shield = e.Kind is "shield_hit" or "shield_break" || e.Kind == "self" && e.Elements.Contains(Element.Shield);
        bool fire = e.Elements.Contains(Element.Fire), ice = e.Elements.Contains(Element.Cold);
        Color color = shield ? Ui.Shield : e.Kind is "blink" or "blink_arrive" ? Ui.Energy : Ui.SpellColor(e.Elements);
        int count = e.Kind switch { "explosion" => 30, "nova" => 24, "shield_break" => 22, "death" => 20, "impact" => 16, "hurt" => 4, _ => 8 };
        float speed = e.Kind is "explosion" or "nova" or "shield_break" ? 5.5f : 2.8f;
        int shape = shield || ice || e.Elements.Contains(Element.Earth) ? 1 : fire ? 2 : 0;
        var origin = ArenaRenderer.World(e.Position, e.Kind == "explosion" ? 0.3f : 1.1f);
        for (int i = 0; i < count; i++) Emit(origin, color, Direction() * speed, 0.55f + (float)_random.NextDouble() * 0.3f, shape, e.Kind == "explosion" ? 0.28f : 0.17f);
        if (e.Kind is "nova" or "explosion") Ring(e.Position, color, 0.4f, e.Kind == "nova" ? 4 : 5, 0.6f);
        else if (e.Kind is "blink" or "blink_arrive") Ring(e.Position, color, 0.2f, 1.7f, 0.45f);
        else if (shield || e.Kind is "self" or "barrier" or "cast") Ring(e.Position, color, 0.4f, shield ? 1.6f : 1.1f, 0.4f);
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
        if (!playing) return;
        _trailClock += dt; _statusClock += dt;
        if (_trailClock >= 0.045f)
        {
            _trailClock %= 0.045f;
            foreach (var p in snapshot.Projectiles)
            {
                var color = Ui.SpellColor(p.Elements);
                Emit(ArenaRenderer.World(p.Position, 0.9f), color, ArenaRenderer.World(p.Direction) * -1.3f,
                    0.35f, p.Elements.Contains(Element.Cold) ? 1 : 0, 0.20f, 0);
            }
            foreach (var b in snapshot.Beams)
            {
                float t = (float)_random.NextDouble();
                Emit(ArenaRenderer.World(V2.Lerp(b.From, b.To, t), 1), Ui.SpellColor(b.Elements), Direction() * 0.5f, 0.25f, 0, 0.12f, 0);
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
                if (p.Casting && WizardTypes.Affinity(p.WizardType) is { } element)
                    Emit(at + Vector3.Up * 1.35f, Ui.ElementColor(element), Direction(), 0.45f, 0, 0.16f, 0);
            }
        }
    }
    public void Clear()
    {
        foreach (var p in _sparks) { p.Node.Visible = false; _freeSparks.Push(p); } _sparks.Clear();
        foreach (var w in _waves) { w.Node.Visible = false; _freeWaves.Push(w); } _waves.Clear();
        _trailClock = _statusClock = 0; SeenKinds.Clear();
    }
}
