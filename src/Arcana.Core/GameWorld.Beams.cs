namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed record BeamPart(int Owner, SpellRecipe Recipe, float Scale = 1);
    private sealed record CastRay(Wizard Caster, V2 From, V2 Direction, V2 End, BeamPart Part);

    private void TickChannels(float dt)
    {
        var rays = new List<CastRay>();
        foreach (var player in _players.Values.OrderBy(p => p.Id))
        {
            if (!player.Alive || player.Frozen > 0 || player.Channel == null) continue;
            if (player.Channel.Shape != SpellShape.Beam) { Channel(player, dt); continue; }
            var from = SpellOrigin(player, player.Facing, 0.7f);
            var end = BeamEnd(from, player.Facing, player.Channel.Range, player.Id, out _, out _);
            rays.Add(new(player, from, player.Facing, end, new(player.Id, player.Channel)));
        }
        rays.RemoveAll(ray => !ray.Caster.Alive || ray.Caster.Frozen > 0 || ray.Caster.Channel == null);

        var meetings = new List<(int A, int B, V2 At, float Distance)>();
        for (int a = 0; a < rays.Count; a++)
        for (int b = a + 1; b < rays.Count; b++)
            if (Geometry.SegmentMeeting(rays[a].From, rays[a].End, rays[b].From, rays[b].End, out var at) &&
                V2.Distance(at, rays[a].From) > 0.08f && V2.Distance(at, rays[b].From) > 0.08f)
                meetings.Add((a, b, at, V2.Distance(at, rays[a].From) + V2.Distance(at, rays[b].From)));

        // Un canal participa en una unión por paso. Las refracciones tienen como máximo dos rebotes.
        var used = new HashSet<int>();
        foreach (var meeting in meetings.OrderBy(m => m.Distance).ThenBy(m => m.A).ThenBy(m => m.B))
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
            float range = Math.Max(0, Math.Min(a.Part.Recipe.Range - V2.Distance(a.From, meeting.At),
                b.Part.Recipe.Range - V2.Distance(b.From, meeting.At)));
            BeamPart[] parts = [a.Part, b.Part];
            TraceBeam(meeting.At + direction * 0.02f, direction, range, parts, a.Caster.Id, -1, dt, "merged");
            if (Tick % 12 == 0) Emit("beam_merge", meeting.At, BeamElements(parts), "");
        }
        for (int i = 0; i < rays.Count; i++)
            if (!used.Contains(i))
            {
                var ray = rays[i];
                TraceBeam(ray.From, ray.Direction, ray.Part.Recipe.Range, [ray.Part], ray.Caster.Id, ray.Caster.Id, dt, "direct");
            }
    }

    private V2 BeamEnd(V2 from, V2 direction, float range, int ignoredWizard, out Wall? wall, out Wizard? target)
    {
        var end = RayEnd(from, direction, range, out wall);
        target = null; float nearest = 1;
        foreach (var player in _players.Values)
        {
            if (!player.Alive || player.Id == ignoredWizard) continue;
            float radius = player.Shield > 0 && player.ShieldTime > 0 ? 0.95f : 0.6f;
            if (Geometry.SegmentCircle(from, end, player.Position, radius) is not float t || t >= nearest) continue;
            nearest = t; target = player;
        }
        if (target != null) { end = V2.Lerp(from, end, nearest); wall = null; }
        return end;
    }

    private static Element[] BeamElements(IEnumerable<BeamPart> parts) => parts.SelectMany(p => p.Recipe.Elements).Take(10).ToArray();
    private void AddBeam(int owner, V2 from, V2 to, BeamPart[] parts, string interaction)
    {
        if (_beams.Count < 64 && V2.Distance(from, to) > 0.005f)
            _beams.Add(new(owner, from, to, BeamElements(parts), SpellShape.Beam, parts.Sum(p => p.Scale), interaction));
    }

    private float BeamPressure(Wizard target, BeamPart part)
    {
        var s = part.Recipe;
        return part.Scale * (ElementDamage(target, part.Owner, Element.Arcane, 115 * s.Count(Element.Arcane)) +
            ElementDamage(target, part.Owner, Element.Fire, s.Steam ? 160 : 58 * s.Count(Element.Fire)) +
            ElementDamage(target, part.Owner, Element.Cold, (s.Ice ? 68 : 24) * s.Count(Element.Cold)) +
            ElementDamage(target, part.Owner, Element.Lightning, 100 * s.Count(Element.Lightning) * (target.Wet > 0 ? 1.9f : 1)) +
            ElementDamage(target, part.Owner, Element.Earth, 100 * s.Count(Element.Earth)) + 35 * s.Count(Element.Life));
    }

    private void TraceBeam(V2 from, V2 direction, float range, BeamPart[] parts, int owner, int ignoredWizard,
        float dt, string interaction)
    {
        for (int bounce = 0; bounce <= 2 && range > 0.08f; bounce++)
        {
            var end = BeamEnd(from, direction, range, ignoredWizard, out var wall, out var target);
            AddBeam(owner, from, end, parts, interaction);
            range -= V2.Distance(from, end);
            V2 normal;
            Element[] ward;
            int reflector;
            if (target != null)
            {
                bool alliedHealing = parts.All(part => part.Recipe.Healing && _players.TryGetValue(part.Owner, out var source) && Allied(source, target));
                if (target.Shield <= 0 || target.ShieldTime <= 0 || alliedHealing)
                {
                    foreach (var part in parts) Hit(target, part.Recipe, dt * part.Scale, part.Owner, direction);
                    return;
                }
                float pressure = parts.Where(part => !_players.TryGetValue(part.Owner, out var source) ||
                    source.Id == target.Id || !Allied(source, target) || Options.FriendlyFire).Sum(part => BeamPressure(target, part));
                Damage(target, pressure * 0.2f * dt, owner, BeamElements(parts));
                if (target.Shield <= 0)
                {
                    foreach (var part in parts) Hit(target, part.Recipe, dt * part.Scale * 0.8f, part.Owner, direction);
                    return;
                }
                normal = (end - target.Position).Normalized;
                ward = target.Ward;
                reflector = target.Id;
            }
            else if (wall != null)
            {
                wall.Health -= parts.Sum(p => 110 * p.Recipe.Power * p.Scale) * dt * 0.2f;
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
