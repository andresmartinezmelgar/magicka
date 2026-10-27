namespace Arcana.Core;

/// <summary>Aspecto estable por semilla, independiente del azar del terreno, la horda y el botín.</summary>
public static class SurvivalBiomes
{
    private static readonly ArenaTheme[] Themes = [ArenaTheme.Forest, ArenaTheme.City, ArenaTheme.Ruins,
        ArenaTheme.Sanctuary, ArenaTheme.Desert, ArenaTheme.Frozen, ArenaTheme.Volcanic, ArenaTheme.Crystal,
        ArenaTheme.Marsh, ArenaTheme.Fungal];
    public static IReadOnlyList<ArenaTheme> Available { get; } = Array.AsReadOnly(Themes);

    public static ArenaTheme ThemeFor(SurvivalMapSpec? map)
    {
        if (map is not { Version: >= 2 }) return ArenaTheme.Sanctuary;
        uint hash = unchecked((uint)map.Seed ^ 0xa24baed5u);
        hash ^= hash >> 16; hash = unchecked(hash * 0x7feb352du);
        hash ^= hash >> 15; hash = unchecked(hash * 0x846ca68bu); hash ^= hash >> 16;
        return Themes[hash % (uint)Themes.Length];
    }

    public static string Name(ArenaTheme theme) => theme switch
    {
        ArenaTheme.Forest => "Bosque ancestral", ArenaTheme.City => "Ciudad olvidada",
        ArenaTheme.Ruins => "Ruinas cubiertas de musgo", ArenaTheme.Sanctuary => "Santuario lunar",
        ArenaTheme.Desert => "Oasis de arenisca", ArenaTheme.Frozen => "Fortaleza de hielo",
        ArenaTheme.Volcanic => "Caldera de obsidiana", ArenaTheme.Crystal => "Gruta de cristal",
        ArenaTheme.Marsh => "Marisma encantada", ArenaTheme.Fungal => "Jardín de hongos",
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };
}
