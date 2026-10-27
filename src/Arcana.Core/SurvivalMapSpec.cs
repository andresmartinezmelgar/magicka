namespace Arcana.Core;

/// <summary>La misma semilla y versión reconstruyen el mapa en servidor, cliente y guardado.</summary>
public sealed record SurvivalMapSpec(int Seed, int Version = 1)
{
    public static SurvivalMapSpec Legacy { get; } = new(0, 0);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid => Version == 1 || Version == 0 && Seed == 0;

    internal static SurvivalMapSpec New(SurvivalMapSpec? previous = null)
    {
        int seed;
        do seed = Random.Shared.Next(); while (previous?.Seed == seed);
        return new(seed);
    }
}

internal static class SurvivalMapCache
{
    // No retener indefinidamente mapas y cuadrículas de navegación de partidas terminadas.
    private const int Limit = 16;
    private static readonly Dictionary<SurvivalMapSpec, ArenaDefinition> Maps = [];
    private static readonly Queue<SurvivalMapSpec> Order = [];

    internal static ArenaDefinition Get(SurvivalMapSpec spec)
    {
        if (!spec.Valid) throw new ArgumentException("Versión de mapa de supervivencia incompatible.", nameof(spec));
        if (spec.Version == 0) return ArenaMaps.Get(ArenaMapId.Bastion);
        lock (Maps)
        {
            if (Maps.TryGetValue(spec, out var map)) return map;
            map = SurvivalLayout.Create(spec);
            if (Maps.Count == Limit) Maps.Remove(Order.Dequeue());
            Maps.Add(spec, map); Order.Enqueue(spec);
            return map;
        }
    }
}
