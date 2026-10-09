namespace Arcana.Core;

public enum PracticeStatus { None, Wet, Burning, Chilled, Frozen, Haste, Protection }
public sealed record PracticeTargetDefinition(string Name, V2 Position, PracticeStatus Status, string Keys = "");
public sealed record PracticeBarrierDefinition(int Index, SpellCombination Combination, V2 Position)
{
    public string Name => Combination.Recipe.Healing ? "Mina de vida"
        : Combination.Recipe.Has(Element.Arcane) ? "Mina ofensiva" : "Barrera";
}

/// <summary>Las posiciones y recetas de las estaciones se comparten con el cliente.</summary>
public static class PracticeLayout
{
    public static readonly V2 Respawn = new(-48, -5);
    public static readonly V2 CasterPosition = new(-28, -5);
    public static readonly V2 CasterDirection = new(1, 0);
    public static readonly IReadOnlyList<PracticeTargetDefinition> Targets = Array.AsReadOnly(BuildTargets());
    public static readonly IReadOnlyList<PracticeBarrierDefinition> Barriers = Array.AsReadOnly(
        SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Barrier)
            .Select((c, i) => new PracticeBarrierDefinition(i, c, new(-42 + i % 7 * 14, 18 + i / 7 * 12))).ToArray());
    public static readonly IReadOnlyList<SpellCombination> CasterSpells = Array.AsReadOnly(
        new[] { new SpellCombination("", []) }.Concat(SpellCatalogue.All.Where(c => c.Recipe.Shape != SpellShape.Barrier)).ToArray());

    private static PracticeTargetDefinition[] BuildTargets()
    {
        (string Name, PracticeStatus Status, string Keys)[] values =
        [
            ("Sin estados", PracticeStatus.None, ""), ("Mojado", PracticeStatus.Wet, ""),
            ("Quemado", PracticeStatus.Burning, ""), ("Enfriado", PracticeStatus.Chilled, ""),
            ("Congelado", PracticeStatus.Frozen, ""), ("Celeridad", PracticeStatus.Haste, ""),
            ("Escudo · E", PracticeStatus.Protection, "E"), ("Piedra · ED", PracticeStatus.Protection, "ED"),
            ("Regeneración · EW", PracticeStatus.Protection, "EW"), ("Guarda agua · QE", PracticeStatus.Protection, "QE"),
            ("Guarda frío · ER", PracticeStatus.Protection, "ER"), ("Guarda rayo · EA", PracticeStatus.Protection, "EA"),
            ("Guarda arcano · ES", PracticeStatus.Protection, "ES"), ("Guarda fuego · EF", PracticeStatus.Protection, "EF")
        ];
        return values.Select((v, i) => new PracticeTargetDefinition(v.Name,
            new(-42 + i % 7 * 14, -38 + i / 7 * 14), v.Status, v.Keys)).ToArray();
    }
}
