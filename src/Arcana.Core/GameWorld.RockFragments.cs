namespace Arcana.Core;

public sealed partial class GameWorld
{
    private readonly List<Projectile> _pendingRockFragments = [];

    private void FragmentRock(Projectile rock)
    {
        if (rock.IsRockFragment || rock.Recipe.Shape != SpellShape.Rock || rock.RockFragmentation <= 0) return;
        int total = CombatAffixes.RockFragmentCount(rock.RockFragmentation);
        int available = Math.Max(0, CombatAffixes.RockFragmentProjectileLimit - _projectiles.Count - _pendingRockFragments.Count);
        var forward = rock.Velocity.Normalized;
        if (forward.LengthSquared < .5f) forward = new(1, 0);
        // Se añaden al terminar la iteración de impactos. El ángulo no consume azar de botín o horda.
        for (int i = 0; i < Math.Min(total, available); i++)
        {
            var direction = forward.Rotated(MathF.Tau * i / total);
            _pendingRockFragments.Add(new Projectile
            {
                Id = _nextEntity++, Owner = rock.Owner, Position = rock.Position,
                Velocity = direction * CombatAffixes.RockFragmentSpeed, Recipe = rock.Recipe,
                Radius = CombatAffixes.RockFragmentRadius, Power = rock.Power * CombatAffixes.RockFragmentImpactScale,
                Lifetime = CombatAffixes.RockFragmentLifetime, IsRockFragment = true
            });
        }
    }
}
