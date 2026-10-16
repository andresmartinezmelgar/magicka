namespace Arcana.Core;

public sealed partial class GameWorld
{
    private readonly record struct BeamPart(int Owner, SpellRecipe Recipe, float Scale = 1, float DamageMultiplier = 1);
    private readonly record struct CastRay(Wizard Caster, V2 From, V2 Direction, V2 End, BeamPart Part);
    private readonly List<CastRay> _castRays = [];
    private readonly List<(int A, int B, V2 At, float Distance)> _beamMeetings = [];
    private readonly HashSet<int> _joinedRays = [];

    private void TickChannels(float dt)
    {
        var rays = _castRays;
        rays.Clear();
        foreach (var player in _orderedPlayers)
        {
            if (!player.Alive || player.Frozen > 0 || player.Channel == null) continue;
            if (player.Channel.Shape != SpellShape.Beam) { Channel(player, dt); continue; }
            float multiplier = 1;
            if (SpellBalance.Damage(player.Channel) > 0)
            {
                player.BeamTicks = Math.Min((int)(SpellBalance.BeamChargeSeconds / StepSeconds + 0.5f), player.BeamTicks + 1);
                multiplier = SpellBalance.BeamDamageMultiplier(player.BeamTicks * StepSeconds *
                    (player.Channel.Has(Element.Arcane) ? player.Equipment.BeamChargeSpeed : 1));
            }
            var from = SpellOrigin(player, player.Facing, 0.7f);
            var end = BeamEnd(from, player.Facing, player.Channel.Range * player.Equipment.Range, player.Id, out _, out _, player.Id);
            rays.Add(new(player, from, player.Facing, end, new(player.Id, player.Channel, DamageMultiplier: multiplier)));
        }
        rays.RemoveAll(ray => !ray.Caster.Alive || ray.Caster.Frozen > 0 || ray.Caster.Channel == null);

        if (rays.Count == 0) return;
        var meetings = _beamMeetings;
        meetings.Clear();
        for (int a = 0; a < rays.Count; a++)
        for (int b = a + 1; b < rays.Count; b++)
            if (Geometry.SegmentMeeting(rays[a].From, rays[a].End, rays[b].From, rays[b].End, out var at) &&
                V2.Distance(at, rays[a].From) > 0.08f && V2.Distance(at, rays[b].From) > 0.08f)
                meetings.Add((a, b, at, V2.Distance(at, rays[a].From) + V2.Distance(at, rays[b].From)));

        // Un canal participa en una unión por paso. Las refracciones tienen como máximo dos rebotes.
        var used = _joinedRays;
        used.Clear();
        meetings.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
            : a.A != b.A ? a.A.CompareTo(b.A) : a.B.CompareTo(b.B));
        foreach (var meeting in meetings)
        {
            if (used.Contains(meeting.A) || used.Contains(meeting.B)) continue;
            used.Add(meeting.A); used.Add(meeting.B);
            var a = rays[meeting.A]; var b = rays[meeting.B];
            AddBeam(a.Caster.Id, a.From, meeting.At, [a.Part], "direct");
            AddBeam(b.Caster.Id, b.From, meeting.At, [b.Part], "direct");
            var direction = a.Direction + b.Direction;
            if (direction.Length < 0.12f)
            {
                var axis = a.Direction.X < -0.001f || MathF.Abs(a.Direction.X) <= 0.001f && a.Direction.Y < 0 ? a.Direction * -1 : a.Direction;
                direction = axis.Perpendicular;
            }
            direction = direction.Normalized;
            float range = Math.Max(0, Math.Min(a.Part.Recipe.Range * a.Caster.Equipment.Range - V2.Distance(a.From, meeting.At),
                b.Part.Recipe.Range * b.Caster.Equipment.Range - V2.Distance(b.From, meeting.At)));
            BeamPart[] parts = [a.Part, b.Part];
            TraceBeam(meeting.At + direction * 0.02f, direction, range, parts, a.Caster.Id, -1, dt, "merged");
            if (Tick % 12 == 0) Emit("beam_merge", meeting.At, BeamElements(parts), "");
        }
        for (int i = 0; i < rays.Count; i++)
            if (!used.Contains(i))
            {
                var ray = rays[i];
                TraceBeam(ray.From, ray.Direction, ray.Part.Recipe.Range * ray.Caster.Equipment.Range, [ray.Part], ray.Caster.Id, ray.Caster.Id, dt, "direct");
            }
    }

    private V2 BeamEnd(V2 from, V2 direction, float range, int ignoredWizard, out Wall? wall, out Wizard? target,
        int owner, BeamPart[]? parts = null)
    {
        var end = RayEnd(from, direction, range, out wall, owner, parts);
        target = null; float nearest = 1;
        foreach (var player in _combatants.Values)
        {
            if (!player.Alive || player.Id == ignoredWizard) continue;
            float radius = IsDefense && _minions.TryGetValue(player.Id, out var minion) && minion.Kind == MinionKind.Boss ? 1.4f : player.Shield > 0 && player.ShieldTime > 0 ? 0.95f : 0.6f;
            if (Geometry.SegmentCircle(from, end, player.Position, radius) is not float t || t >= nearest) continue;
            nearest = t; target = player;
        }
        if (target != null) { end = V2.Lerp(from, end, nearest); wall = null; }
        return end;
    }

    private static Element[] BeamElements(BeamPart[] parts)
    {
        int count = 0;
        foreach (var part in parts) count += part.Recipe.Elements.Length;
        if (count == 0) return [];
        var elements = new Element[Math.Min(10, count)];
        int next = 0;
        foreach (var part in parts)
            foreach (var element in part.Recipe.Elements)
            {
                if (next == elements.Length) return elements;
                elements[next++] = element;
            }
        return elements;
    }
    private void AddBeam(int owner, V2 from, V2 to, BeamPart[] parts, string interaction)
    {
        if (_beams.Count >= 64 || !(V2.Distance(from, to) > 0.005f)) return;
        double power = 0;
        foreach (var part in parts) power += part.Scale * part.DamageMultiplier;
        _beams.Add(new(owner, from, to, BeamElements(parts), SpellShape.Beam, (float)power, interaction));
    }

    private float BeamPressure(Wizard target, BeamPart part)
    {
        var s = part.Recipe;
        double damage = 0;
        foreach (var element in SpellBalance.Offensive)
            damage += ElementDamage(target, part.Owner, element, SpellBalance.Component(s, element, target.Wet > 0));
        return part.Scale * part.DamageMultiplier * (float)damage;
    }

    private void TraceBeam(V2 from, V2 direction, float range, BeamPart[] parts, int owner, int ignoredWizard,
        float dt, string interaction)
    {
        for (int bounce = 0; bounce <= 2 && range > 0.08f; bounce++)
        {
            var end = BeamEnd(from, direction, range, ignoredWizard, out var wall, out var target, owner, parts);
            AddBeam(owner, from, end, parts, interaction);
            range -= V2.Distance(from, end);
            V2 normal;
            Element[] ward;
            int reflector;
            if (target != null)
            {
                bool alliedHealing = true;
                foreach (var part in parts)
                    if (!part.Recipe.Healing || !_combatants.TryGetValue(part.Owner, out var source) || !Allied(source, target))
                    { alliedHealing = false; break; }
                if (target.Shield <= 0 || target.ShieldTime <= 0 || alliedHealing)
                {
                    foreach (var part in parts) Hit(target, part.Recipe, dt * part.Scale, part.Owner, direction, part.DamageMultiplier);
                    return;
                }
                foreach (var part in parts)
                    if (!_combatants.TryGetValue(part.Owner, out var source) || source.Id == target.Id || !Allied(source, target) || Options.FriendlyFire && !IsDefense)
                    {
                        Damage(target, BeamPressure(target, part) * 0.2f * dt, part.Owner, part.Recipe.Elements);
                        if (target.Shield > 0) DamageProtection(target, UtilityPressure(target, part.Recipe, part.Owner) * part.Scale * 0.2f * dt,
                            part.Owner, part.Recipe.Elements);
                    }
                if (target.Shield <= 0)
                {
                    foreach (var part in parts) Hit(target, part.Recipe, dt * part.Scale * 0.8f, part.Owner, direction, part.DamageMultiplier);
                    return;
                }
                normal = (end - target.Position).Normalized;
                ward = target.Ward;
                reflector = target.Id;
            }
            else if (wall != null)
            {
                double pressure = 0;
                foreach (var part in parts) pressure += WallDamage(wall, part.Recipe, part.Owner,
                    110 * part.Recipe.Power * part.Scale * part.DamageMultiplier);
                wall.Health -= (float)pressure * dt * 0.2f;
                if (wall.Health <= 0) return;
                normal = (wall.B - wall.A).Perpendicular.Normalized;
                ward = wall.Recipe.Elements.Where(e => e != Element.Shield).ToArray();
                reflector = wall.Owner;
            }
            else return;
            if (bounce == 2) return;
            direction = (direction - normal * (2 * V2.Dot(direction, normal))).Normalized;
            parts = parts.Select(p => p with { Owner = reflector, Scale = p.Scale * 0.8f }).ToArray();
            interaction = ward.Length > 0 ? "refracted" : "reflected";
            if (ward.Length > 0)
            {
                direction = direction.Rotated(0.35f);
                parts = [.. parts, new(reflector, new SpellRecipe(ward), 0.35f)];
            }
            if (Tick % 12 == 0) Emit("beam_refract", end, BeamElements(parts), "");
            from = end + direction * 0.04f;
            range = Math.Max(0, range - 0.04f);
            ignoredWizard = reflector; owner = reflector;
        }
    }
}
