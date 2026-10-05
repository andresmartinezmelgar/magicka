using Arcana.Core;

namespace Arcana.Client;

/// <summary>Reproduce posiciones confirmadas entre muestras, sin predecir a través de obstáculos.</summary>
internal sealed class WizardMotion
{
    private readonly record struct Sample(double Time, V2 Position);
    private readonly List<Sample> _samples = new(16);
    private double _clock;
    private float _sampleAge;
    public V2 Position { get; private set; }
    public float DistanceMoved { get; private set; }
    public float WalkCycle { get; private set; }
    public bool Moving => DistanceMoved > 0.00001f;
    private const float Stride = 1.55f;

    public void Advance(V2 target, double sampleTime, float dt, double delay, bool snap = false)
    {
        DistanceMoved = 0;
        _sampleAge += Math.Max(dt, 0);
        if (_samples.Count == 0 || snap || dt > 0.25f || sampleTime < _samples[^1].Time ||
            sampleTime > _samples[^1].Time && _sampleAge > 0.25f ||
            sampleTime - _samples[^1].Time > 0.25 || V2.Distance(target, _samples[^1].Position) > 4 ||
            sampleTime == _samples[^1].Time && target != _samples[^1].Position)
        {
            _samples.Clear(); _samples.Add(new(sampleTime, target));
            Position = target; _clock = sampleTime - delay; WalkCycle = 0; _sampleAge = 0;
            return;
        }
        if (sampleTime > _samples[^1].Time)
        {
            _sampleAge = 0;
            _samples.Add(new(sampleTime, target));
            if (_samples.Count > 16) _samples.RemoveAt(0);
            // Recuperar el reloj tras una pausa sin arrastrar al mago por el mapa.
            if (sampleTime - _clock > delay + 0.1) _clock = sampleTime - delay;
        }
        _clock += Math.Max(dt, 0);
        V2 rendered = _samples[^1].Position;
        if (_clock <= _samples[0].Time) rendered = _samples[0].Position;
        else
            for (int i = 1; i < _samples.Count; i++)
                if (_clock <= _samples[i].Time)
                {
                    var a = _samples[i - 1]; var b = _samples[i];
                    rendered = V2.Lerp(a.Position, b.Position, (float)((_clock - a.Time) / (b.Time - a.Time)));
                    break;
                }
        DistanceMoved = V2.Distance(Position, rendered); Position = rendered;
        if (Moving) WalkCycle = (WalkCycle + DistanceMoved / Stride) % 1;
        else WalkCycle = 0;
    }
}
