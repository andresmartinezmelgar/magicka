using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class CombatEffects
{
    private readonly record struct FlameEmitter(Vector3 Origin, Vector3 Center,
        Vector3 Forward, Vector3 Side, float Range, int Charges, int Shape, bool Cone);
    private readonly List<FlameEmitter> _flameEmitters = [];
    private readonly Dictionary<int, InstanceDrawBatch> _channelBatches = [];
    private InstanceDrawBatch? _flameBatch;
    private float _flameClock = 1f / 30;
    private int _activeFlames, _activeChannels, _activeSteam, _flameCursor;
    private bool _enhancedSpells = true;
    internal int ActiveFlames => _activeFlames;
    internal int ActiveElementParticles => _activeChannels;
    internal Func<BeamSnapshot, Vector3>? FlameOrigin { get; set; }
    internal Basis Billboard { get; set; } = Basis.Identity;
    internal bool EnhancedSpells
    {
        get => _enhancedSpells;
        set
        {
            if (_enhancedSpells == value) return;
            _enhancedSpells = value;
            // Cambiar de perfil no duplica emisiones ni mantiene una nube costosa al caer los FPS.
            ClearChannelParticles(); ClearWaterJets();
        }
    }

    internal void PrepareFlamethrower() => _flameBatch ??= new(this, "FlamethrowerBatch",
        FlameVisuals.Mesh(), FlameVisuals.Material(), ParticleLimit, custom: true, boundsPadding: .18f);

    internal void PrepareElementChannels()
    {
        PrepareFlamethrower();
        foreach (int shape in new[] { ElementChannelVisuals.Ice, ElementChannelVisuals.Water, ElementChannelVisuals.Steam })
            ChannelBatch(shape);
        PrepareWaterJets();
    }

    private InstanceDrawBatch ChannelBatch(int shape)
    {
        if (shape == FlameVisuals.Shape) { PrepareFlamethrower(); return _flameBatch!; }
        if (!_channelBatches.TryGetValue(shape, out var batch))
        {
            string name = shape switch { ElementChannelVisuals.Ice => "IceCrystalBatch",
                ElementChannelVisuals.Water => "WaterDropBatch", _ => "SteamBatch" };
            _channelBatches[shape] = batch = new(this, name, ElementChannelVisuals.Mesh(shape),
                ElementChannelVisuals.Material(shape), ParticleLimit, custom: true);
        }
        return batch;
    }

    private bool UsesChannelParticles(BeamSnapshot beam) => FlameVisuals.Matches(beam) ||
        EnhancedSpells && ElementChannelVisuals.ParticleShape(beam) >= FlameVisuals.Shape;

    private void EmitElementChannels(WorldSnapshot snapshot, float dt)
    {
        _flameEmitters.Clear();
        foreach (var beam in snapshot.Beams)
        {
            if (!UsesChannelParticles(beam) || V2.Distance(beam.From, beam.To) < .05f) continue;
            var origin = FlameOrigin?.Invoke(beam) ?? ArenaRenderer.World(beam.From, 1);
            var end = ArenaRenderer.World(beam.To, origin.Y);
            if (end.DistanceSquaredTo(origin) < .0025f) continue;
            var forward = ArenaRenderer.World(beam.To - beam.From).Normalized();
            var side = Vector3.Up.Cross(forward).Normalized();
            int charges = SpellVisualStrength.For(beam.Elements).Charges;
            SeenCharges.Add(charges);
            _flameEmitters.Add(new(origin, ArenaRenderer.World(beam.From, origin.Y), forward, side,
                V2.Distance(beam.From, beam.To), charges, ElementChannelVisuals.ParticleShape(beam), beam.Shape == SpellShape.Spray));
        }
        float rate = EnhancedSpells ? 45 : 30;
        if (_flameEmitters.Count == 0) { _flameClock = 1 / rate; return; }
        _flameClock += dt;
        int ticks = Math.Min(2, (int)(_flameClock * rate));
        if (ticks == 0) return;
        _flameClock %= 1 / rate;
        int budget = Math.Max(8, (int)(32 * DetailScale));
        int limit = Math.Max(48, (int)((EnhancedSpells ? FlameVisuals.MaximumParticles : 128) * DetailScale));
        int steamLimit = Math.Max(24, (int)(64 * DetailScale));
        int start = _flameCursor % _flameEmitters.Count, emitted = 0;
        // Una emisión por autor antes de repetir; el límite es común a todos los elementos.
        for (int pass = 0; pass < 18 && emitted < budget && _activeChannels < limit; pass++)
        for (int i = 0; i < _flameEmitters.Count && emitted < budget && _activeChannels < limit; i++)
        {
            var emitter = _flameEmitters[(start + i) % _flameEmitters.Count];
            int samples = emitter.Shape == ElementChannelVisuals.Steam ? Math.Min(2, emitter.Charges) : emitter.Charges * 3;
            int count = Math.Max(1, (int)MathF.Ceiling(samples * ticks * DetailScale));
            if (pass >= count || emitter.Shape == ElementChannelVisuals.Steam && _activeSteam >= steamLimit) continue;
            float seed = (float)_random.NextDouble();
            float life = emitter.Shape switch { ElementChannelVisuals.Ice => .56f,
                ElementChannelVisuals.Water => .52f, ElementChannelVisuals.Steam => .65f, _ => .34f + emitter.Charges * .035f };
            var spark = Emit(emitter.Origin, Colors.White, Vector3.Zero,
                life * (.9f + seed * .2f), emitter.Shape, .1f, 0);
            if (spark == null) { pass = 18; break; }
            float spread = (float)_random.NextDouble() * 2 - 1;
            float angle = emitter.Cone ? spread * Math.Abs(spread) * MathF.Acos(SpellBalance.ChannelConeDot) * .90f : 0;
            var ray = emitter.Forward * MathF.Cos(angle) + emitter.Side * MathF.Sin(angle);
            spark.FlameOrigin = emitter.Origin;
            spark.FlameEnd = emitter.Center + ray * (emitter.Range * .97f);
            spark.FlameForward = (spark.FlameEnd - spark.FlameOrigin).Normalized();
            spark.FlameSide = Vector3.Up.Cross(spark.FlameForward).Normalized();
            spark.FlameRange = emitter.Range; spark.FlameSeed = seed; spark.FlamePower = emitter.Charges / 3f;
            _activeChannels++; emitted++;
            if (spark.Shape == FlameVisuals.Shape) _activeFlames++;
            if (spark.Shape == ElementChannelVisuals.Steam) _activeSteam++;
            AdvanceChannel(spark);
        }
        _flameCursor = (start + Math.Min(_flameEmitters.Count, emitted)) % _flameEmitters.Count;
    }

    private static void AdvanceChannel(Spark p)
    {
        if (p.Shape == FlameVisuals.Shape) { AdvanceFlame(p); return; }
        float age = Math.Clamp(1 - p.Life / p.Duration, 0, 1);
        float span = p.FlameOrigin.DistanceTo(p.FlameEnd);
        p.Position = p.FlameOrigin.Lerp(p.FlameEnd, age);
        float size;
        switch (p.Shape)
        {
            case ElementChannelVisuals.Ice:
                p.Position += Vector3.Down * (.12f * age * age);
                size = (p.FlameSeed > .65f ? .18f + p.FlameSeed * .07f : .045f + p.FlameSeed * .04f) * Math.Min(1, age * 18);
                size = Math.Min(size, Math.Max(.001f, span * (1 - age) * .65f));
                p.Scale = new(size, size * (p.FlameSeed > .65f ? 1 : 1.5f), size * .6f);
                break;
            case ElementChannelVisuals.Water:
                p.Position += Vector3.Down * (.65f * age * age) + p.FlameSide * (MathF.Sin(p.FlameSeed * 12) * age * .12f);
                size = (.022f + p.FlameSeed * .07f) * (1 + age * .5f);
                if (p.FlameSeed > .94f) size *= 1.7f;
                size = Math.Min(size, Math.Max(.001f, span * (1 - age) * .5f));
                p.Scale = new(size * 2.7f, size * 1.6f, 1);
                break;
            default:
                p.Position += Vector3.Up * (.55f * age * age);
                size = Math.Min(.15f + age * .55f, Math.Max(.001f, span * (1 - age) * .8f));
                p.Scale = new(size * 2, size * 2, 1);
                break;
        }
    }

    private static void AdvanceFlame(Spark p)
    {
        float age = Math.Clamp(1 - p.Life / p.Duration, 0, 1);
        float span = p.FlameOrigin.DistanceTo(p.FlameEnd), travel = span * age;
        float wobble = MathF.Sin(age * 8 + p.FlameSeed * Mathf.Tau) * p.FlameRange * .02f * age * (1 - age);
        p.Position = p.FlameOrigin.Lerp(p.FlameEnd, age) +
            p.FlameSide * wobble + Vector3.Up * (age * age * .24f);
        float width = (.10f + age * p.FlameRange * .09f) * (.85f + p.FlameSeed * .3f);
        float length = (.45f + age * 1.2f) * (.85f + p.FlameSeed * .3f);
        length = Math.Min(length, 2 * Math.Min(travel + .035f, span - travel));
        p.Scale = new(width, width * (.7f + p.FlameSeed * .25f), Math.Max(.001f, length));
    }

    private bool HasElementChannel(WorldSnapshot snapshot, int owner)
    {
        foreach (var beam in snapshot.Beams)
            if (beam.Owner == owner && UsesChannelParticles(beam)) return true;
        return false;
    }

    private void ClearChannelParticles()
    {
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var p = _sparks[i]; if (p.Shape < FlameVisuals.Shape) continue;
            if (p.Node != null) p.Node.Visible = false;
            _freeSparks.Push(p); _sparks[i] = _sparks[^1]; _sparks.RemoveAt(_sparks.Count - 1);
        }
        _flameBatch?.Clear(); foreach (var batch in _channelBatches.Values) batch.Clear();
        _activeChannels = _activeFlames = _activeSteam = _flameCursor = 0;
        _flameClock = 1f / 30; _flameEmitters.Clear();
    }

    internal bool FlameBoundsValid()
    {
        foreach (var p in _sparks)
        {
            if (p.Shape != FlameVisuals.Shape) continue;
            float along = (p.Position - p.FlameOrigin).Dot(p.FlameForward);
            float span = p.FlameOrigin.DistanceTo(p.FlameEnd);
            if (!p.Position.IsFinite() || along < -.001f || along + p.Scale.Z * .5f > span + .001f) return false;
        }
        return true;
    }
}
