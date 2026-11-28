using Arcana.Core;

namespace Arcana.Client;

/// <summary>Giro visual por el arco corto, independiente de los FPS y del apuntado autoritativo.</summary>
internal sealed class WizardFacing
{
    private const float Step = MathF.Tau / WizardDirectionalAtlas.Directions;
    private float _angle;
    private bool _initialized;
    internal int Direction { get; private set; }

    internal void Reset(V2 facing)
    {
        if (facing.LengthSquared < .000001f) facing = new(0, 1);
        _angle = MathF.Atan2(facing.X, facing.Y);
        Direction = Quantize(_angle); _initialized = true;
    }

    internal int Advance(V2 facing, float dt, bool casting, bool locked = false)
    {
        if (!_initialized) Reset(facing);
        if (locked || facing.LengthSquared < .000001f) return Direction;
        float target = MathF.Atan2(facing.X, facing.Y);
        float delta = Difference(target, _angle);
        float speed = (casting ? 1440f : 900f) * MathF.PI / 180;
        _angle += Math.Clamp(delta, -speed * Math.Max(0, dt), speed * Math.Max(0, dt));
        _angle = MathF.IEEERemainder(_angle, MathF.Tau);
        // Un margen de dos grados evita alternar sprites por ruido de red junto a un límite.
        float current = Direction * Step;
        if (Math.Abs(Difference(_angle, current)) > Step * .5f + MathF.PI / 90)
            Direction = Quantize(_angle);
        return Direction;
    }

    private static int Quantize(float angle) => ((int)MathF.Floor(angle / Step + .5f) % WizardDirectionalAtlas.Directions
        + WizardDirectionalAtlas.Directions) % WizardDirectionalAtlas.Directions;
    private static float Difference(float angle, float from) => MathF.IEEERemainder(angle - from, MathF.Tau);
}
