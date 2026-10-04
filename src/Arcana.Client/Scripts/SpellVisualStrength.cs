using Arcana.Core;

namespace Arcana.Client;

/// <summary>Detalle local según el número de elementos, incluidos los repetidos; nunca aumenta daño ni alcance.</summary>
internal readonly record struct SpellVisualStrength(int Charges)
{
    public static SpellVisualStrength For(Element[] elements) => new(Math.Clamp(elements.Length, 1, 3));
    public float BurstDensity => Charges switch { 1 => 1, 2 => 1.8f, _ => 2.8f };
    public int TrailSamples => Charges switch { 1 => 1, 2 => 2, _ => 4 };
    public int ChannelSamples => Charges;
    public int SprayStrands => Charges switch { 1 => 4, 2 => 7, _ => 11 };
    public int LightningBranches => Charges switch { 1 => 0, 2 => 1, _ => 3 };
    public float SparkSize => 0.11f + Charges * 0.02f;
    public float Lifetime => 0.22f + Charges * 0.06f;
    public float BeamRadius => 0.06f + Charges * 0.025f;
}
