namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TickProjectiles(float dt)
    {
        _pendingRockFragments.Clear();
        foreach (var projectile in _projectiles)
        {
            float travel = projectile.IsRockFragment ? Math.Min(dt, Math.Max(0, projectile.Lifetime)) : dt;
            projectile.Lifetime -= dt;
            var from = projectile.Position; var to = from + projectile.Velocity * travel;
            var boundary = Map.BoundaryHit(from, to, projectile.Radius);
            float nearest = boundary ?? 1; Wizard? target = null; Wall? wallHit = null; bool obstacleHit = boundary != null;
            foreach (var o in Map.Obstacles)
                if (Geometry.SegmentCircle(from, to, o.Position, o.Radius + projectile.Radius) is float t && t < nearest)
                { nearest = t; obstacleHit = true; target = null; wallHit = null; }
            foreach (var w in _walls)
                if (w.Health > 0 && !PassesOwnShield(w, projectile.Owner) && Geometry.SegmentCapsule(from, to, w.A, w.B, projectile.Radius + 0.2f) is float t && t < nearest)
                { nearest = t; wallHit = w; obstacleHit = false; target = null; }
            foreach (var q in _combatants.Values)
                if (q.Alive && q.Id != projectile.Owner &&
                    !(IsDefense && !projectile.Recipe.Healing && TryCombatSource(projectile.Owner, out var caster) && Allied(caster, q)) &&
                    Geometry.SegmentCircle(from, to, q.Position, (IsDefense && _minions.TryGetValue(q.Id, out var minion) && minion.Kind == MinionKind.Boss ? 1.4f : ArenaMap.WizardRadius) + projectile.Radius) is float t && t < nearest)
                { nearest = t; target = q; obstacleHit = false; wallHit = null; }
            projectile.Position = V2.Lerp(from, to, nearest);
            if (target != null)
            {
                Hit(target, projectile.Recipe, projectile.Power, projectile.Owner, projectile.Velocity.Normalized);
                projectile.Lifetime = 0;
            }
            if (wallHit != null)
            {
                DamageWall(wallHit, projectile.Recipe, projectile.Owner, 170 * projectile.Power * projectile.Recipe.Power);
                if (wallHit.Recipe.Elements.Length == 1)
                {
                    var normal = (wallHit.B - wallHit.A).Perpendicular.Normalized;
                    projectile.Velocity -= normal * (2 * V2.Dot(projectile.Velocity, normal));
                    projectile.Position += projectile.Velocity.Normalized * (projectile.Radius + 0.4f);
                    projectile.Owner = wallHit.Owner; projectile.Power *= 0.8f;
                }
                else projectile.Lifetime = 0;
            }
            if (obstacleHit || !Map.Inside(projectile.Position)) projectile.Lifetime = 0;
            if ((target != null || obstacleHit || wallHit != null && projectile.Lifetime <= 0) && Map.Inside(projectile.Position))
            {
                ProjectileSplash(projectile, target?.Id ?? -1);
                if (wallHit == null || wallHit.Recipe.Elements.Length != 1) FragmentRock(projectile);
            }
            else if (projectile.IsRockFragment && projectile.Lifetime <= 0 && Map.Inside(projectile.Position))
                ProjectileSplash(projectile, -1);
            if (projectile.Lifetime <= 0)
                Emit("impact", projectile.Position, projectile.Recipe.Elements, "");
        }
        _projectiles.RemoveAll(p => p.Lifetime <= 0);
        _projectiles.AddRange(_pendingRockFragments);
        _pendingRockFragments.Clear();
    }

    private void TickWallsAndFields(float dt)
    {
        foreach (var w in _walls)
        {
            w.Lifetime -= dt;
            if (w.Health <= 0 || w.Lifetime <= 0) continue;
            if (w.Recipe.Has(Element.Fire) || w.Recipe.Has(Element.Cold) || w.Recipe.Has(Element.Lightning) || w.Recipe.Has(Element.Water))
                foreach (var p in _combatants.Values)
                    if (p.Alive && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 1)
                        HitAs(StatisticAttack.Barrier, p, w.Recipe, dt, w.Owner, (p.Position - (w.A + w.B) / 2).Normalized);
        }
        _walls.RemoveAll(w => w.Lifetime <= 0 || w.Health <= 0);
        foreach (var f in _fields)
        {
            f.Lifetime -= dt; f.Delay -= dt;
            if (f.Lifetime <= 0 || f.Delay > 0) continue;
            if (f.Kind == DefenseRules.ColossusFissureKind)
            {
                ResolveColossusFissures(f);
                continue;
            }
            if (f.Kind == "equipment_water_pool")
            {
                foreach (var target in _combatants.Values)
                    if (target.Alive && target.Invulnerable <= 0 && AcceptsDefenseElement(target, Element.Water) && V2.Distance(target.Position, f.Position) < f.Radius &&
                        Visible(f.Position, target.Position, f.Owner))
                    { target.Wet = Math.Max(target.Wet, 1); target.Burning = 0; ApplyWaterSlow(target, f.Owner); }
                continue;
            }
            if (f.Kind is "burning_ground" or "frost_ground" or "water_pool")
            {
                foreach (var p in _combatants.Values)
                    if (p.Alive && V2.Distance(p.Position, f.Position) < f.Radius && Visible(f.Position, p.Position, f.Owner))
                        Hit(p, f.Recipe, dt * SpellBalance.GroundPower * (f.IsRockFragmentArea ? CombatAffixes.RockFragmentGroundScale : 1),
                            f.Owner, (p.Position - f.Position).Normalized);
                continue;
            }
            if (f.Kind == "defense_meteor")
            {
                foreach (var minion in _minions.Values)
                    if (minion.Body.Alive && V2.Distance(minion.Body.Position, f.Position) < f.Radius && Visible(f.Position, minion.Body.Position, f.Owner))
                        DamageAs(StatisticAttack.AutoMeteor, minion.Body, f.Power, f.Owner, [Element.Earth, Element.Fire]);
                Emit("explosion", f.Position, [Element.Earth, Element.Fire], "Meteorito", f.Radius, f.Owner);
                f.Lifetime = 0; continue;
            }
            if (f.Kind == "oracle_bombardment")
            {
                foreach (var defender in _players.Values)
                    if (defender.Alive && V2.Distance(defender.Position, f.Position) < f.Radius && Visible(f.Position, defender.Position, f.Owner))
                        DamageAs(StatisticAttack.OracleBombardment, defender, f.Power, f.Owner, f.Recipe.Elements);
                Emit("explosion", f.Position, f.Recipe.Elements, "Bombardeo rúnico", f.Radius);
                f.Lifetime = 0; continue;
            }
            bool trigger = f.Kind == "meteor" || _combatants.Values.Any(p => EligibleForMine(p, f) && V2.Distance(p.Position, f.Position) < f.TriggerRadius);
            if (!trigger) continue;
            foreach (var p in _combatants.Values)
            {
                if (!p.Alive || !(V2.Distance(p.Position, f.Position) < f.Radius) || !Visible(f.Position, p.Position, f.Owner)) continue;
                if (IsDefense && f.Kind == "meteor" && TryCombatSource(f.Owner, out var caster) && Allied(caster, p)) continue;
                if (f.Kind == "healing_mine")
                {
                    if (TryCombatSource(f.Owner, out var owner) && Allied(owner, p))
                        HealAs(StatisticAttack.Mine, p, SpellBalance.MineHeal * f.Recipe.Count(Element.Life) * HealingPower(owner.Id), owner.Id);
                }
                else HitAs(f.Kind == "meteor" ? StatisticAttack.Meteor : StatisticAttack.Mine, p, f.Recipe, f.Kind == "meteor" ? SpellBalance.MeteorPower : SpellBalance.MinePower, f.Owner, (p.Position - f.Position).Normalized);
            }
            Emit(f.Kind == "healing_mine" ? "mine_heal" : "explosion", f.Position, f.Recipe.Elements, f.Kind == "meteor" ? "Meteoro" : "", f.Radius, IsDefense ? f.Owner : 0);
            f.Lifetime = 0;
        }
        _fields.RemoveAll(f => f.Lifetime <= 0);
    }
}
