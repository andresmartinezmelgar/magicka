namespace Arcana.Core;

public static partial class DefenseRules
{
    public const float EnemyRockPower = 0.7f, EnemySprayDuration = 0.9f, EnemyChannelDuration = 1.2f;
    public const float SentinelShield = 280, SentinelShieldDuration = 7, SentinelShieldCooldown = 16;
    public const int CaptainReinforcements = 84;
    public static bool SpellcastingEnemy(MinionKind kind) => kind is MinionKind.Mage or MinionKind.Warlock or MinionKind.Cryomancer;
    public static WizardType EnemyWizardType(MinionKind kind) => kind switch
    {
        MinionKind.Mage or MinionKind.Warlock => WizardType.Pyromancer,
        MinionKind.Cryomancer => WizardType.Cryomancer,
        MinionKind.Shaman => WizardType.Geomancer,
        _ => WizardType.Balanced
    };
    public static float EnemySpellPower(MinionKind kind) => SpellcastingEnemy(kind)
        ? MagePower * (kind == MinionKind.Warlock ? 1.2f : 1) : 1;
    public static float EnemyChannelSeconds(SpellRecipe spell) => spell.Shape == SpellShape.Spray ? EnemySprayDuration : EnemyChannelDuration;

    // Una única selección de recetas para la IA y las cifras del bestiario.
    public static Element[] EnemySpell(MinionKind kind, int turn, float distance, bool wet) => kind switch
    {
        MinionKind.Warlock => turn % 3 == 0
            ? [Element.Earth, Element.Arcane, Element.Arcane]
            : [Element.Earth, Element.Fire, Element.Fire],
        MinionKind.Cryomancer => turn % 3 == 0 && distance < 5.8f
            ? [Element.Cold, Element.Cold, Element.Cold]
            : turn % 3 == 1 ? [Element.Water, Element.Cold, Element.Cold] : [Element.Earth, Element.Cold, Element.Cold],
        MinionKind.Mage => (turn % 4) switch
        {
            0 => [Element.Arcane, Element.Fire],
            1 when !wet => [Element.Lightning, Element.Fire, Element.Fire],
            2 when distance < 5.8f => [Element.Fire, Element.Fire, Element.Fire],
            _ => [Element.Arcane, Element.Arcane, Element.Arcane]
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    public static SpellRecipe[] EnemySpells(MinionKind kind) => !SpellcastingEnemy(kind) ? [] :
        Enumerable.Range(0, 12).SelectMany(turn => new[] { EnemySpell(kind, turn, 4, false), EnemySpell(kind, turn, 9, false) })
            .DistinctBy(elements => string.Join(',', elements)).Select(elements => new SpellRecipe(elements)).ToArray();
}
