namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool Ready(Wizard p) => p.Alive && p.Frozen <= 0 && p.StunTime <= 0 && p.ActionCooldown <= 0 && !MatchEnded;

    private static void UpdateFacing(Wizard p)
    {
        var aim = new V2(p.Input.AimX, p.Input.AimY) - p.Position;
        if (aim.Length > 0.05f) p.Facing = aim.Normalized;
    }

    private static void CancelCast(Wizard p)
    { p.Channel = p.Charging = null; p.ChargeTime = 0; p.BeamTicks = 0; p.BotWallTarget = 0; p.ArcaneEchoActive = false; }

    private void BeginCast(Wizard p)
    {
        if (!Ready(p)) return;
        CancelCast(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        _statistics.Cast(p.Id, MatchStatisticsCollector.Shape(spell.Shape), spell.Elements);
        if (spell.Shape == SpellShape.Rock)
        {
            p.Charging = spell; p.ChargeTime = 0;
        }
        else if (spell.Shape == SpellShape.Shards)
        {
            int count = 3 + spell.Count(Element.Cold);
            bool launched = false;
            for (int i = 0; i < count; i++) launched |= SpawnProjectile(p, spell,
                p.Facing.Rotated((i - (count - 1) / 2f) * 0.11f), SpellBalance.ShardPower, 22, 0.16f);
            if (launched) Emit("cast", p.Position, spell.Elements, "");
            p.ActionCooldown = 0.5f;
        }
        else if (spell.Shape == SpellShape.Barrier)
        {
            CreateBarrier(p, spell); p.ActionCooldown = 0.7f;
        }
        else
        {
            p.Channel = spell;
            Emit("cast", p.Position, spell.Elements, "");
        }
    }

    private void Release(Wizard p)
    {
        if (p.Charging != null)
        {
            if (SpawnProjectile(p, p.Charging, p.Facing, SpellBalance.RockMinimum + p.ChargeTime, 13 + p.ChargeTime * 9,
                0.24f + p.Charging.Count(Element.Earth) * 0.13f)) Emit("cast", p.Position, p.Charging.Elements, "");
            p.ActionCooldown = 0.35f;
        }
        CancelCast(p);
    }

    private bool SpawnProjectile(Wizard p, SpellRecipe spell, V2 direction, float power, float speed, float radius)
    {
        if (_projectiles.Count >= 128) return false;
        _projectiles.Add(new Projectile
        {
            Id = _nextEntity++, Owner = p.Id, Position = SpellOrigin(p, direction, 0.85f, radius),
            Velocity = direction * speed, Recipe = spell, Power = power, Radius = radius, Lifetime = 2.5f * p.Equipment.Range,
            RockFragmentation = spell.Shape == SpellShape.Rock ? p.Equipment.RockFragmentation : 0
        });
        return true;
    }

    private V2 SpellOrigin(Wizard caster, V2 direction, float distance, float radius = 0)
    {
        var to = caster.Position + direction * distance;
        float? nearest = Map.BoundaryHit(caster.Position, to, radius);
        foreach (var obstacle in Map.Obstacles)
            if (Geometry.SegmentCircle(caster.Position, to, obstacle.Position, obstacle.Radius + radius) is float t && (nearest == null || t < nearest))
                nearest = t;
        foreach (var wall in _walls)
            if (wall.Health > 0 && !PassesOwnShield(wall, caster.Id) && Geometry.SegmentCapsule(caster.Position, to, wall.A, wall.B, radius + 0.2f) is float t && (nearest == null || t < nearest))
                nearest = t;
        return nearest is float hit ? V2.Lerp(caster.Position, to, Math.Max(0, hit - 0.01f)) : to;
    }

    private V2 RayEnd(V2 from, V2 direction, float range, out Wall? hitWall, int owner = 0, BeamPart[]? parts = null)
    {
        hitWall = null;
        if (!Map.Inside(from)) return from;
        var to = from + direction * range;
        float best = Map.BoundaryHit(from, to) ?? 1;
        foreach (var o in Map.Obstacles)
            if (Geometry.SegmentCircle(from, to, o.Position, o.Radius) is float t && t < best) best = t;
        // Segmento de pared contra rayo mediante productos cruzados.
        foreach (var w in _walls)
        {
            if (!(w.Health > 0) || PassesOwnShield(w, owner, parts)) continue;
            var r = to - from; var s = w.B - w.A;
            float cross = r.X * s.Y - r.Y * s.X;
            if (MathF.Abs(cross) < 0.0001f) continue;
            var a = w.A - from;
            float t = (a.X * s.Y - a.Y * s.X) / cross;
            float u = (a.X * r.Y - a.Y * r.X) / cross;
            if (t >= 0 && t < best && u >= 0 && u <= 1) { best = t; hitWall = w; }
        }
        return from + (to - from) * best;
    }

    private void Channel(Wizard p, float dt)
    {
        var spell = p.Channel!;
        var from = SpellOrigin(p, p.Facing, 0.7f);
        var end = RayEnd(from, p.Facing, spell.Range * p.Equipment.Range, out var wall, p.Id);
        if (wall != null) DamageWall(wall, spell, p.Id, 110 * spell.Power * dt);
        // Los rayos rectos se resuelven juntos en TickChannels. Este método mantiene chorros y cadenas.
        if (spell.Shape != SpellShape.Beam)
        {
            var targets = _channelTargets;
            targets.Clear();
            float range = V2.Distance(end, from) + 0.1f;
            int order = 0;
            foreach (var q in _combatants.Values)
            {
                int index = order++;
                if (q.Id == p.Id || !q.Alive || spell.Shape == SpellShape.Lightning && !spell.Healing && Allied(p, q)) continue;
                float distance = V2.Distance(q.Position, from);
                if (distance <= range && V2.Dot((q.Position - from).Normalized, p.Facing) >
                    (spell.Shape == SpellShape.Lightning ? SpellBalance.LightningConeDot : SpellBalance.ChannelConeDot) && Visible(from, q.Position, p.Id))
                    targets.Add((q, distance, index));
            }
            targets.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Order.CompareTo(b.Order));
            if (spell.Shape == SpellShape.Lightning)
            {
                // Mojado hace peligroso incluso preparar un rayo sobre uno mismo.
                if (p.Wet > 0)
                {
                    RecordCastInterruption(p, SpellInterruptionCause.WetLightning, p.Id);
                    Hit(p, spell, dt, p.Id, V2.Zero);
                    CancelCast(p);
                }
                V2 last = from;
                for (int i = 0; i < Math.Min(3, targets.Count); i++)
                {
                    var target = targets[i].Target;
                    Hit(target, spell, dt, p.Id, p.Facing);
                    _beams.Add(new(p.Id, last, target.Position, spell.Elements, spell.Shape));
                    last = target.Position;
                }
                if (targets.Count > 0 && p.Equipment.LightningChains > 0)
                    ChainEquipmentLightning(p, spell, targets.Take(3).Select(t => t.Target).ToArray(), dt);
                if (targets.Count == 0) _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
                return;
            }
            foreach (var target in targets) Hit(target.Target, spell, dt, p.Id, p.Facing);
        }
        _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
    }

    private bool Visible(V2 from, V2 to, int owner = 0)
    {
        float distance = V2.Distance(from, to);
        var end = RayEnd(from, (to - from).Normalized, distance, out _, owner);
        return V2.Distance(end, to) < 0.2f;
    }
}
