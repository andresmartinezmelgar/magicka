using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class CombatEffects
{
    private readonly record struct FlameEmitter(Vector3 Origin, Vector3 Center,
        Vector3 Forward, Vector3 Side, float Range, int Charges);
    private readonly List<FlameEmitter> _flameEmitters = [];
    private InstanceDrawBatch? _flameBatch;
    private float _flameClock = 1f / 30;
    private int _activeFlames, _flameCursor;
    internal int ActiveFlames => _activeFlames;
    internal Func<BeamSnapshot, Vector3>? FlameOrigin { get; set; }

    internal void PrepareFlamethrower()
    {
        // El margen incluye la curvatura del shader: el lote no se recorta al girar la cámara.
        _flameBatch ??= new(this, "FlamethrowerBatch", FlameVisuals.Mesh(), FlameVisuals.Material(),
            ParticleLimit, custom: true, boundsPadding: .18f);
    }

    private void EmitFlamethrowers(WorldSnapshot snapshot, float dt)
    {
        _flameEmitters.Clear();
        foreach (var beam in snapshot.Beams)
        {
            if (!FlameVisuals.Matches(beam) || V2.Distance(beam.From, beam.To) < .05f) continue;
            var origin = FlameOrigin?.Invoke(beam) ?? ArenaRenderer.World(beam.From, 1);
            var end = ArenaRenderer.World(beam.To, origin.Y);
            var path = end - origin;
            if (path.LengthSquared() < .0025f) continue;
            var forward = ArenaRenderer.World(beam.To - beam.From).Normalized();
            var side = Vector3.Up.Cross(forward).Normalized();
            int charges = SpellVisualStrength.For(beam.Elements).Charges;
            SeenCharges.Add(charges);
            _flameEmitters.Add(new(origin, ArenaRenderer.World(beam.From, origin.Y), forward, side,
                V2.Distance(beam.From, beam.To), charges));
        }
        if (_flameEmitters.Count == 0) { _flameClock = 1f / 30; return; }
        _flameClock += dt;
        int ticks = Math.Min(2, (int)(_flameClock * 30));
        if (ticks == 0) return;
        _flameClock %= 1f / 30;
        int budget = Math.Max(8, (int)(32 * DetailScale));
        int limit = Math.Max(48, (int)(FlameVisuals.MaximumParticles * DetailScale));
        int start = _flameCursor % _flameEmitters.Count, emitted = 0;
        // Una pasada por emisor antes de repetir: una horda no monopoliza el presupuesto.
        for (int pass = 0; pass < 18 && emitted < budget && _activeFlames < limit; pass++)
        for (int i = 0; i < _flameEmitters.Count && emitted < budget && _activeFlames < limit; i++)
        {
            var emitter = _flameEmitters[(start + i) % _flameEmitters.Count];
            int count = Math.Max(1, (int)MathF.Ceiling(emitter.Charges * 3 * ticks * DetailScale));
            if (pass >= count) continue;
            float seed = (float)_random.NextDouble();
            var spark = Emit(emitter.Origin, Colors.White, Vector3.Zero,
                (.34f + emitter.Charges * .035f) * (.9f + seed * .2f), FlameVisuals.Shape, .1f, 0);
            if (spark == null) { pass = 18; break; }
            float spread = (float)_random.NextDouble() * 2 - 1;
            // La apertura procede del cono de daño; concentrar las muestras en el centro
            // mantiene un núcleo denso y deja llamas sueltas cerca del borde.
            float angle = spread * Math.Abs(spread) * MathF.Acos(SpellBalance.ChannelConeDot) * .90f;
            var ray = emitter.Forward * MathF.Cos(angle) + emitter.Side * MathF.Sin(angle);
            spark.FlameOrigin = emitter.Origin;
            spark.FlameEnd = emitter.Center + ray * (emitter.Range * .97f);
            spark.FlameForward = (spark.FlameEnd - spark.FlameOrigin).Normalized();
            spark.FlameSide = Vector3.Up.Cross(spark.FlameForward).Normalized();
            spark.FlameRange = emitter.Range; spark.FlameSeed = seed;
            spark.FlamePower = emitter.Charges / 3f;
            _activeFlames++; emitted++;
            AdvanceFlame(spark);
        }
        _flameCursor = (start + Math.Min(_flameEmitters.Count, emitted)) % _flameEmitters.Count;
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
        // Ni el nacimiento invade al lanzador ni la punta añade alcance visual.
        length = Math.Min(length, 2 * Math.Min(travel + .035f, span - travel));
        p.Scale = new(width, width * (.7f + p.FlameSeed * .25f), Math.Max(.001f, length));
    }

    private static bool HasFlameChannel(WorldSnapshot snapshot, int owner)
    {
        foreach (var beam in snapshot.Beams)
            if (beam.Owner == owner && FlameVisuals.Matches(beam)) return true;
        return false;
    }

    internal bool FlameBoundsValid()
    {
        foreach (var p in _sparks)
        {
            if (p.Shape != FlameVisuals.Shape) continue;
            float along = (p.Position - p.FlameOrigin).Dot(p.FlameForward);
            float span = p.FlameOrigin.DistanceTo(p.FlameEnd);
            if (!p.Position.IsFinite() || along < -.001f || along + p.Scale.Z * .5f > span + .001f)
                return false;
        }
        return true;
    }
}
