namespace Arcana.Core;

public sealed partial class GameWorld
{
    // Compartido por la sala, independiente de la vida de los ejemplares y de la conexión de cada jugador.
    private readonly Dictionary<MinionKind, DefenseEnemySnapshot> _defenseBestiary = [];

    private void DiscoverDefenseEnemy(DefenseMinion enemy)
    {
        var kind = enemy.Kind;
        float attack = enemy.Strength * (kind == MinionKind.Oracle ? DefenseRules.OracleStrikeDamage : kind == MinionKind.Archer ? SpellBalance.Earth * DefenseRules.ArrowPower
            : DefenseRules.MinionMeleeAttack(kind));
        float maximum = attack;
        if (DefenseRules.SpellcastingEnemy(kind))
        {
            var spell = new SpellRecipe(DefenseRules.EnemySpell(kind, 0, 4, false));
            var damage = BestiaryCatalogue.SpellDamage(kind, spell);
            attack = enemy.Strength * damage.Minimum;
            maximum = enemy.Strength * damage.Maximum;
        }
        _defenseBestiary.TryGetValue(kind, out var previous);
        _defenseBestiary[kind] = new(kind, _defenseDifficulty, enemy.Body.MaxHealth, attack, maximum,
            DefenseRules.AttackPause(kind, _defenseDifficulty), Wizard.BaseMovementSpeed * enemy.Body.Equipment.Speed, DefenseRules.MinionRange(kind),
            (previous?.Spawned ?? 0) + 1, previous?.Killed ?? 0);
    }
}
