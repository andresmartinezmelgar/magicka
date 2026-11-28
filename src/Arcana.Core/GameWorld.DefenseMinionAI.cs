namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool Empowered(DefenseMinion minion)
    {
        foreach (var other in _minions.Values)
            if (other.Kind is MinionKind.Captain or MinionKind.Boss && other.Body.Alive &&
                other.Body.Id != minion.Body.Id && V2.Distance(other.Body.Position, minion.Body.Position) <
                (other.Kind == MinionKind.Boss ? 12 : 8)) return true;
        return false;
    }

    private void TickMinion(DefenseMinion minion, float dt)
    {
        var p = minion.Body;
        if (!p.Alive || MatchEnded) return;
        p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
        p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
        TickStatus(p, dt);
        minion.AuraRepairCooldown = Math.Max(0, minion.AuraRepairCooldown - dt);
        if (!p.Alive) return;
        TickSpecialDefenseShield(minion, dt);
        minion.SpeedBoost = Math.Max(0, minion.SpeedBoost - dt);
        minion.SpeedClock -= dt;
        if (minion.SpeedClock <= 0)
        {
            minion.SpeedClock = 8 + (float)_random.NextDouble() * 14;
            if (minion.Kind != MinionKind.Boss && _random.Next(100) < 35) minion.SpeedBoost = 2 + (float)_random.NextDouble() * 2;
        }
        float power = minion.Strength * DefenseRules.EnemySpellPower(minion.Kind)
            * (Empowered(minion) ? 1.35f : 1);
        float speed = DefenseRules.MinionSpeed(minion.Kind, _defenseDifficulty) * (minion.SpeedBoost > 0 ? DefenseRules.SpeedBoostMultiplier : 1);
        if (p.Equipment.Power != power || p.Equipment.Speed != speed)
            p.Equipment = p.Equipment with { Power = power, Speed = speed };
        if (minion.Kind == MinionKind.Shaman) TickDefenseShaman(minion, dt);
        if (minion.Kind == MinionKind.Oracle) TickOracleShield(minion, dt);
        UpdateDefenseEnemyDefense(minion, dt);
        if (minion.Kind == MinionKind.Boss && HoldColossusFissures(minion)) return;
        if (minion.Windup > 0)
        {
            p.Input.MoveX = p.Input.MoveY = 0;
            if (p.Frozen > 0 || p.StunTime > 0) { minion.Windup = 0; minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty); return; }
            minion.Windup = Math.Max(0, minion.Windup - dt);
            if (minion.Windup <= 0) ResolveDefenseAreaAttack(minion);
            return;
        }
        Wizard? target = DefenseTarget(minion, dt);
        if (target == null) { p.Input.MoveX = p.Input.MoveY = 0; return; }
        V2 goal = target.Position;
        float range = DefenseRules.MinionRange(minion.Kind);
        float distanceToTarget = V2.Distance(p.Position, target.Position);
        bool canAttack = distanceToTarget <= range && Visible(p.Position, target.Position, p.Id);
        minion.NavigationClock -= dt;
        if (!canAttack && minion.NavigationClock <= 0)
        {
            minion.Navigation = SurvivalDirection(p, target);
            minion.NavigationClock = 0.16f + p.Id % 4 * 0.015f;
        }
        var direction = canAttack ? V2.Zero : minion.Navigation;
        // Casters keep distance and strafe instead of running straight at the mage.
        if (minion.Kind == MinionKind.Oracle && distanceToTarget < DefenseRules.OracleRetreatRange)
            direction = OracleRetreat(p, target);
        else if (RangedDefenseEnemy(minion.Kind) && canAttack)
        {
            var away = (p.Position - target.Position).Normalized;
            var flank = away.Perpendicular * (p.Id % 2 == 0 ? 0.52f : -0.52f);
            var tactical = distanceToTarget < 4.5f ? away + flank
                : distanceToTarget < (minion.Kind == MinionKind.Oracle ? 21 : 8.5f) ? flank : V2.Zero;
            if (tactical.LengthSquared > 0.01f && Walkable(p.Position + tactical.Normalized * 1.2f))
                direction = tactical.Normalized;
        }
        V2 separation = DefenseSeparation(minion);
        p.Input.MoveX = direction.X + separation.X * 1.5f;
        p.Input.MoveY = direction.Y + separation.Y * 1.5f;
        p.Input.AimX = target.Position.X; p.Input.AimY = target.Position.Y;
        p.Facing = (target.Position - p.Position).Normalized;
        var oldCell = DefenseCell(p.Position);
        Move(p, dt);
        var newCell = DefenseCell(p.Position);
        if (oldCell != newCell) { DefenseCellList(oldCell).Remove(minion); DefenseCellList(newCell).Add(minion); }
        minion.Attack -= dt;
        if (p.Channel != null)
        {
            minion.ChannelTime -= dt;
            if (minion.ChannelTime <= 0 || p.Frozen > 0 || p.StunTime > 0) { CancelCast(p); minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty); }
            return;
        }
        if (minion.Attack > 0 || p.Frozen > 0 || p.StunTime > 0) return;
        // Los muros de los defensores se pueden romper: nunca bloquean una ruta indefinidamente.
        var blockingWall = _walls.FirstOrDefault(w => w.Health > 0 && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 2.2f);
        if (!canAttack && blockingWall != null)
        {
            float before = blockingWall.Health;
            blockingWall.Health -= 90 * minion.Strength;
            RecordBarrierDamage(p.Id, blockingWall, Math.Min(Math.Max(0, before), before - blockingWall.Health), null, StatisticAttack.EnemyMelee);
            minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
            return;
        }
        if (!canAttack) return;
        minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
        if (DefenseRules.AreaAttackRadius(minion.Kind) > 0)
        {
            if (minion.Kind == MinionKind.Boss && TryBeginColossusFissures(minion, target)) return;
            minion.Windup = DefenseRules.AttackWindup(minion.Kind);
            if (minion.Kind is MinionKind.Captain or MinionKind.Sentinel)
                minion.Attack = Math.Max(2.8f, DefenseRules.AttackPause(minion.Kind, _defenseDifficulty) * 2.7f);
        }
        else if (minion.Kind == MinionKind.Oracle) CastOracleBombardment(minion, target);
        else if (RangedDefenseEnemy(minion.Kind) && minion.Kind != MinionKind.Archer)
            CastDefenseEnemySpell(minion, distanceToTarget);
        else if (minion.Kind == MinionKind.Archer)
        {
            if (_projectiles.Count >= 160) return;
            var recipe = new SpellRecipe([Element.Earth]);
            _statistics.Cast(p.Id, StatisticAttack.Rock, recipe.Elements);
            _projectiles.Add(new Projectile { Id = _nextEntity++, Owner = p.Id, Position = SpellOrigin(p, p.Facing, 0.75f),
                Velocity = p.Facing * 14, Recipe = recipe, Radius = 0.14f, Power = DefenseRules.ArrowPower, Lifetime = 1.3f });
            Emit("cast", p.Position, [Element.Earth], "");
        }
        else { _statistics.Cast(p.Id, StatisticAttack.EnemyMelee); DamageAs(StatisticAttack.EnemyMelee, target, DefenseRules.MinionMeleeAttack(minion.Kind), p.Id); }
    }

    private void TickDefenseShaman(DefenseMinion minion, float dt)
    {
        minion.SupportCooldown -= dt;
        if (minion.SupportCooldown > 0 || minion.Body.Frozen > 0 || minion.Body.StunTime > 0) return;
        minion.SupportCooldown = DefenseRules.ShamanHealInterval;
        Wizard? wounded = null;
        float mostMissing = 0;
        foreach (var ally in _minions.Values)
        {
            var body = ally.Body;
            if (!body.Alive || body.Id == minion.Body.Id || V2.Distance(body.Position, minion.Body.Position) > DefenseRules.ShamanHealRadius ||
                !Visible(body.Position, minion.Body.Position, minion.Body.Id)) continue;
            float missing = 1 - body.Health / body.MaxHealth;
            if (missing > mostMissing) { mostMissing = missing; wounded = body; }
        }
        if (wounded == null) return;
        float before = wounded.Health;
        _statistics.Cast(minion.Body.Id, StatisticAttack.Regeneration);
        wounded.Health = Math.Min(wounded.MaxHealth, wounded.Health + DefenseRules.ShamanHealAmount * minion.Strength);
        _statistics.Heal(minion.Body.Id, wounded, wounded.Health - before, StatisticAttack.Regeneration);
        Emit("mine_heal", wounded.Position, [Element.Life], "");
    }

    private void ResolveDefenseAreaAttack(DefenseMinion minion)
    {
        _statistics.Cast(minion.Body.Id, StatisticAttack.EnemyArea);
        float radius = DefenseRules.AreaAttackRadius(minion.Kind);
        foreach (var defender in _players.Values)
            if (defender.Alive && V2.Distance(defender.Position, minion.Body.Position) <= radius &&
                Visible(minion.Body.Position, defender.Position, minion.Body.Id))
                DamageAs(StatisticAttack.EnemyArea, defender, DefenseRules.MinionMeleeAttack(minion.Kind), minion.Body.Id);
        Emit("nova", minion.Body.Position,
            [minion.Kind is MinionKind.Bomber or MinionKind.Boss ? Element.Fire :
             minion.Kind == MinionKind.Sentinel ? Element.Shield : Element.Earth], "", radius);
        if (minion.Kind == MinionKind.Bomber)
        {
            minion.Body.Health = 0;
            DefenseDeath(minion.Body, minion.Body.Id);
        }
        minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
    }
}
