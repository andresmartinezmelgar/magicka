namespace Arcana.Core;

public sealed partial class GameWorld
{
    private float _specialSpawnCooldown;

    private void TryPromoteDefenseSpecial(DefenseMinion minion)
    {
        if (_specialSpawnCooldown > 0 || DefenseRules.SpecialLimit(_defenseDifficulty) == 0 ||
            _minions.Values.Count(m => m.Body.Alive && m.SpecialAbility != DefenseSpecialAbility.None) >= DefenseRules.SpecialLimit(_defenseDifficulty) ||
            _random.NextDouble() >= DefenseRules.SpecialSpawnChance) return;
        PromoteDefenseSpecial(minion, _random.Next(2) == 0
            ? DefenseSpecialAbility.FrontalShield : DefenseSpecialAbility.DeathExplosion);
        _specialSpawnCooldown = DefenseRules.SpecialSpawnGap + (float)_random.NextDouble() * DefenseRules.SpecialSpawnGapVariation;
    }

    private void PromoteDefenseSpecial(DefenseMinion minion, DefenseSpecialAbility ability)
    {
        if (minion.SpecialAbility != DefenseSpecialAbility.None || ability == DefenseSpecialAbility.None || !Enum.IsDefined(ability)) return;
        var body = minion.Body;
        float oldMaximum = body.MaxHealth;
        minion.SpecialAbility = ability;
        body.Health *= DefenseRules.SpecialHealthMultiplier; body.MaxHealth *= DefenseRules.SpecialHealthMultiplier;
        minion.Strength *= DefenseRules.SpecialDamageMultiplier;
        body.Equipment = body.Equipment with { Power = body.Equipment.Power * DefenseRules.SpecialDamageMultiplier };
        if (ability == DefenseSpecialAbility.FrontalShield) minion.SpecialShieldCooldown = 2;
        _statistics.Add(StatisticGroup.Difficulty, _defenseDifficulty, StatisticMeasure.SpawnHealth, body.MaxHealth - oldMaximum);
        UpdateDefenseEnemyDetails(minion, spawned: false);
        Emit("defense_special", body.Position, [Element.Lightning], DefenseRules.MinionName(minion.Kind) + " especial · " + DefenseRules.SpecialName(ability));
    }

    private static void TickSpecialDefenseShield(DefenseMinion minion, float dt)
    {
        if (minion.SpecialAbility != DefenseSpecialAbility.FrontalShield) return;
        minion.SpecialShieldCooldown = Math.Max(0, minion.SpecialShieldCooldown - dt);
        minion.SpecialShieldSeconds = Math.Max(0, minion.SpecialShieldSeconds - dt);
        if (minion.Body.Frozen > 0 || minion.Body.StunTime > 0)
        {
            minion.SpecialShieldSeconds = 0;
            minion.SpecialShieldCooldown = Math.Max(2, minion.SpecialShieldCooldown);
            return;
        }
        if (minion.SpecialShieldCooldown <= 0)
        {
            minion.SpecialShieldSeconds = DefenseRules.SpecialShieldDuration;
            minion.SpecialShieldCooldown = DefenseRules.SpecialShieldCycle;
        }
    }

    private float SpecialFrontalShieldMultiplier(Wizard target, V2 impactDirection)
    {
        // Solo impactos con dirección: las auras y quemaduras no tienen un frente.
        if (!IsDefense || impactDirection.LengthSquared < 0.0001f || target.Frozen > 0 || target.StunTime > 0 ||
            !_minions.TryGetValue(target.Id, out var minion) || minion.SpecialAbility != DefenseSpecialAbility.FrontalShield ||
            minion.SpecialShieldSeconds <= 0) return 1;
        return V2.Dot(target.Facing.Normalized, impactDirection.Normalized * -1) >= DefenseRules.SpecialShieldFrontDot
            ? DefenseRules.SpecialShieldDamageMultiplier : 1;
    }

    private void AddSpecialDeathExplosion(DefenseMinion minion)
    {
        // CleanupDefense se ejecuta fuera de la iteración de áreas. Nunca se
        // aplica daño si no podemos incluir el aviso en el estado del cliente.
        if (minion.SpecialAbility != DefenseSpecialAbility.DeathExplosion) return;
        _fields.RemoveAll(f => f.Lifetime <= 0);
        if (_fields.Count >= 40) return;
        float boss = minion.Kind == MinionKind.Boss ? 1.35f : 1;
        _fields.Add(new Field
        {
            Id = _nextEntity++, Owner = minion.Body.Id, Position = minion.Body.Position,
            Kind = DefenseRules.SpecialExplosionKind, Recipe = new([Element.Lightning, Element.Fire]),
            Radius = DefenseRules.SpecialExplosionRadius * boss, Power = DefenseRules.SpecialExplosionDamage * boss,
            Delay = DefenseRules.SpecialExplosionDelay, Lifetime = DefenseRules.SpecialExplosionDelay + 0.2f, TriggerRadius = 0
        });
    }

    private void ResolveSpecialDeathExplosion(Field field)
    {
        foreach (var defender in _players.Values)
            if (defender.Alive && V2.Distance(defender.Position, field.Position) <= field.Radius &&
                Visible(field.Position, defender.Position, field.Owner))
                DamageAs(StatisticAttack.EnemyArea, defender, field.Power, field.Owner, field.Recipe.Elements);
        Emit("explosion", field.Position, field.Recipe.Elements, "Detonación final", field.Radius, field.Owner);
        field.Lifetime = 0;
    }
}
