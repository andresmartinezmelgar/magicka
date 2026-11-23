namespace Arcana.Core;

public sealed partial class GameWorld
{
    public void Step()
    {
        if (DefensePaused) return;
        Tick++;
        const float dt = StepSeconds;
        _beams.Clear();
        int expired = 0;
        while (expired < _events.Count && Tick - _events[expired].Tick > 90) expired++;
        if (expired > 0) _events.RemoveRange(0, expired);
        if (MatchEnded)
        {
            if (_arcaneBursts.Count > 0) { ResolveArcaneBursts(); CleanupDefense(); }
            if (IsDefense) return;
            RestartIn -= dt;
            if (RestartIn <= 0) ResetMatch();
            return;
        }
        int humans = 0, bots = 0;
        foreach (var player in _players.Values) { if (player.Bot) bots++; else humans++; }
        _statistics.Step(humans, bots, _minions.Count);
        if (IsDefense) PlanDefenseBots(dt);
        TickPlayers(dt);
        TickDefense(dt);
        TickPickups(dt);
        TickFrontline(dt);
        // Todos se mueven antes de resolver los canales: cruzar rayos no depende del orden de los jugadores.
        TickChannels(dt);
        TickProjectiles(dt);
        TickWallsAndFields(dt);
        ResolveArcaneBursts();
        TickAutomaticRocks();
        PruneLightningContacts();
        if (Options.Mode == GameMode.Warfare && !Map.IsPractice) TickCapture(dt);
        CleanupDefense();
        CheckVictory();
    }

    private void TickPlayers(float dt)
    {
        foreach (var p in _players.Values)
        {
            p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
            p.RingCooldown = Math.Max(0, p.RingCooldown - dt);
            p.ReprisalCooldown = Math.Max(0, p.ReprisalCooldown - dt);
            p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
            p.Invulnerable = p.Invulnerable <= dt + 0.00001f ? 0 : p.Invulnerable - dt;
            for (int i = 0; i < 4; i++) p.MagickCooldowns[i] = Math.Max(0, p.MagickCooldowns[i] - dt);
            if (!p.Alive)
            {
                if (IsDefense && p.Deaths >= DefenseRules.PlayerLives) continue;
                p.Respawn -= dt;
                if (p.Respawn <= 0) Respawn(p);
                continue;
            }
            if (p.Bot) { if (IsDefense) ThinkDefenseBot(p, dt); else ThinkBot(p, dt); }
            else if (IsDefense && _defenders[p.Id].ShopOpen) ThinkDefenseAssistance(p, dt);
            if (Time - p.LastInput > 0.6)
            {
                InterruptCast(p, SpellInterruptionCause.InputTimeout);
                p.Input.MoveTo = false; p.Input.MoveX = p.Input.MoveY = 0;
            }
            TickStatus(p, dt);
            if (!p.Alive) continue;
            TickRing(p);
            UpdateFacing(p);
            Move(p, dt);
            if (p.Charging != null && p.Frozen <= 0 && p.StunTime <= 0)
            {
                p.ChargeTime = Math.Min(SpellBalance.ChargeSeconds, p.ChargeTime + dt * p.Equipment.RockChargeSpeed);
                if (p.ChargeTime >= SpellBalance.ChargeSeconds) Release(p);
            }
        }
    }

    private void TickStatus(Wizard p, float dt)
    {
        _statistics.Status(p, dt);
        TickElementSelectionUsage(p);
        p.Wet = Math.Max(0, p.Wet - dt);
        p.StunTime = Math.Max(0, p.StunTime - dt);
        p.WaterSlowTime = Math.Max(0, p.WaterSlowTime - dt);
        if (p.WaterSlowTime <= 0 || p.Wet <= 0) { p.WaterSlowTime = 0; p.WaterSlowRank = 0; }
        if (IsDefense && p.Role == CombatantRole.Player)
            p.FreezeRecovery = p.Frozen > 0 ? DefenseRules.FreezeRecoverySeconds : Math.Max(0, p.FreezeRecovery - dt);
        p.Frozen = Math.Max(0, p.Frozen - dt);
        p.Chill = Math.Max(0, p.Chill - 10 * dt);
        p.Haste = Math.Max(0, p.Haste - dt);
        p.Energy = Math.Min(100, p.Energy + (1.8f + RelicBuffs.Energy(p)) * p.Equipment.EnergyRegeneration * dt);
        p.ShieldTime = Math.Max(0, p.ShieldTime - dt);
        p.WardTime -= dt;
        if (p.ShieldTime <= 0) p.Shield = p.ShieldMax = p.RegenerationRate = 0;
        if (p.Shield <= 0) p.RegenerationRate = 0;
        HealAs(StatisticAttack.Regeneration, p, (ActiveShieldRegeneration(p) + RelicBuffs.Regeneration(p)) * dt, p.Id);
        if (p.WardTime <= 0) p.Ward = [];
        if (p.Burning > 0)
        {
            p.Burning = Math.Max(0, p.Burning - dt);
            DamageAs(StatisticAttack.Burn, p, SpellBalance.BurnPerSecond * dt * p.BurnMultiplier * WizardTypes.Incoming(p.WizardType, Element.Fire) * Resistance(p, Element.Fire), p.BurnOwner, [Element.Fire]);
        }
    }

    private static float ActiveShieldRegeneration(Wizard player) => player.Shield > 0 && player.ShieldTime > 0
        ? player.RegenerationRate * RelicBuffs.Healing(player) * player.Equipment.Healing : 0;

    private void Move(Wizard p, float dt)
    {
        var move = new V2(p.Input.MoveX, p.Input.MoveY);
        if (move.Length < 0.1f && p.Input.MoveTo)
        {
            var goal = new V2(p.Input.TargetX, p.Input.TargetY);
            move = goal - p.Position;
            if (move.Length < 0.18f) { move = V2.Zero; p.Input.MoveTo = false; }
            else if (Map.Id != ArenaMapId.Classic) move = NavigationDirection(p, goal, dt);
            move = move.Normalized;
        }
        else if (p.Bot || move.LengthSquared > 1) move = move.Normalized;
        float speed = Wizard.BaseMovementSpeed * RelicBuffs.Speed(p) * p.Equipment.Speed * DefenseSpeed(p) * (p.Haste > 0 ? 1.65f : 1) * (1 - p.Chill * 0.005f) * WaterSlowMultiplier(p);
        if (p.Channel != null) speed *= 0.48f;
        if (p.Charging != null) speed *= 0.35f;
        if (p.Ward.Contains(Element.Earth)) speed *= 0.78f;
        if (p.Frozen > 0 || p.StunTime > 0)
        {
            speed = 0;
            InterruptCast(p, p.Frozen > 0 ? SpellInterruptionCause.Frozen : SpellInterruptionCause.ElectricStun,
                duration: p.Frozen > 0 ? p.Frozen : p.StunTime);
        }
        // El joystick conserva su intensidad; flechas diagonales y rutas no superan la velocidad máxima.
        var walking = move * (speed * dt);
        var delta = walking + p.Knockback * dt;
        p.Knockback *= MathF.Max(0, 1 - 8 * dt);
        var before = p.Position;
        MoveWithCollisions(p, delta, walking);
        _statistics.Actor(p.Id, StatisticMeasure.Distance, V2.Distance(before, p.Position));
    }

    private bool ClearMovement(V2 from, V2 to) => Map.ClearPath(from, to) && ClearWalls(from, to);

    private bool ClearWalls(V2 from, V2 to)
    {
        foreach (var wall in _walls)
            if (wall.Health > 0 && Geometry.SegmentCapsule(from, to, wall.A, wall.B, ArenaMap.WizardRadius + 0.18f) != null) return false;
        return true;
    }

    public bool Walkable(V2 position)
    {
        if (!Map.Walkable(position)) return false;
        foreach (var wall in _walls)
            if (wall.Health > 0 && Geometry.DistanceToSegment(position, wall.A, wall.B) < ArenaMap.WizardRadius + 0.18f) return false;
        return true;
    }
}
