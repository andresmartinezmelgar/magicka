namespace Arcana.Core;

public sealed record SpellShortcutPreset
{
    public bool Automatic { get; init; } = true;
    public Element[] Elements { get; init; } = [];
    public CastMode Mode { get; init; } = CastMode.Forward;

    internal SpellShortcutPreset Normalize(CastMode fallback)
    {
        var valid = Elements is { Length: >= 1 and <= 3 } && new SpellRecipe(Elements).Elements.SequenceEqual(Elements);
        return this with
        {
            Automatic = Automatic || !valid,
            Elements = valid ? Elements.ToArray() : [],
            Mode = Enum.IsDefined(Mode) ? Mode : fallback
        };
    }
}

public sealed record SpellShortcutSettings
{
    public SpellShortcutPreset Defense { get; init; } = new() { Mode = CastMode.Self };
    public SpellShortcutPreset Attack { get; init; } = new();

    public SpellShortcutSettings Normalize() => new()
    {
        Defense = Defense?.Normalize(CastMode.Self) ?? new() { Mode = CastMode.Self },
        Attack = Attack?.Normalize(CastMode.Forward) ?? new()
    };
}
