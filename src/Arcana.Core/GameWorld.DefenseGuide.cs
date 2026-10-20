namespace Arcana.Core;

public sealed partial class GameWorld
{
    // Compartido por la sala, independiente de la vida de los ejemplares y de la conexión de cada jugador.
    private readonly Dictionary<MinionKind, DefenseEnemySnapshot> _defenseBestiary = [];

    private void DiscoverDefenseEnemy(DefenseMinion enemy)
    {
        var kind = enemy.Kind;
        float attack = enemy.Strength * (kind == MinionKind.Archer ? SpellBalance.Earth * DefenseRules.ArrowPower
            : kind is MinionKind.Mage or MinionKind.Warlock or MinionKind.Cryomancer ?
                (SpellBalance.Arcane + SpellBalance.Fire * WizardTypes.Bonus) * DefenseRules.MagePower
            : DefenseRules.MinionMeleeAttack(kind));
        float maximum = attack;
        if (kind is MinionKind.Mage or MinionKind.Warlock or MinionKind.Cryomancer)
        {
            maximum *= SpellBalance.BeamDamageMultiplier(DefenseRules.MageChannelDuration);
            attack *= SpellBalance.BeamMinimumDamage;
        }
        _defenseBestiary.TryGetValue(kind, out var previous);
        _defenseBestiary[kind] = new(kind, _defenseDifficulty, enemy.Body.MaxHealth, attack, maximum,
            DefenseRules.AttackDelay(_defenseDifficulty), 6.2f * enemy.Body.Equipment.Speed, DefenseRules.MinionRange(kind),
            (previous?.Spawned ?? 0) + 1, previous?.Killed ?? 0);
    }
}
