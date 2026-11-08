namespace Arcana.Core;

/// <summary>Busca concentraciones dentro del área real de impacto sin añadir mensajes de red.</summary>
public static class MeteorTargeting
{
    public static V2? Find(WorldSnapshot world, WizardSnapshot caster)
    {
        var enemies = world.Players.Where(p => p.Id != caster.Id && p.Alive &&
                (world.Mode == GameMode.Arena || p.Team != caster.Team)).Select(p => p.Position)
            .Concat(world.Defense?.Minions.Where(m => m.Health > 0).Select(m => m.Position) ?? []);
        var map = ArenaMap.Get(world);
        return Find(caster.Position, enemies, point => map.Walkable(point));
    }

    public static V2? Find(V2 origin, IEnumerable<V2> enemies, Func<V2, bool> canTarget)
    {
        const float radius = SpellBalance.MeteorRadius, range = MagickRules.MeteorRange, epsilon = .0001f;
        var points = enemies.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y) &&
            Squared(p, origin) <= (range + radius) * (range + radius)).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (points.Length == 0) return null;
        V2? best = null; int bestCount = 0;
        float bestSpread = float.PositiveInfinity, bestDistance = float.PositiveInfinity;
        void Consider(V2 target)
        {
            float distance = Squared(target, origin);
            if (distance > range * range + epsilon || !canTarget(target)) return;
            int count = 0; float spread = 0;
            foreach (var point in points)
            {
                float squared = Squared(point, target);
                if (squared <= radius * radius + epsilon) { count++; spread += squared; }
            }
            if (count == 0) return;
            spread /= count;
            if (count > bestCount || count == bestCount && (spread < bestSpread - epsilon ||
                Math.Abs(spread - bestSpread) <= epsilon && distance < bestDistance - epsilon))
            { best = target; bestCount = count; bestSpread = spread; bestDistance = distance; }
        }
        void Intersections(V2 a, float ar, V2 b, float br)
        {
            var offset = b - a; float distance = offset.Length;
            if (distance < .0001f || distance > ar + br + epsilon || distance < Math.Abs(ar - br) - epsilon) return;
            var direction = offset / distance;
            float along = (ar * ar - br * br + distance * distance) / (2 * distance);
            float height = MathF.Sqrt(Math.Max(0, ar * ar - along * along));
            var center = a + direction * along;
            var normal = new V2(-direction.Y, direction.X) * height;
            Consider(center + normal); Consider(center - normal);
        }
        Consider(origin);
        foreach (var point in points) Consider(MagickRules.MeteorTarget(origin, point));
        // Los centros entre enemigos cubren grupos que no caben apuntando a uno de ellos.
        // La intersección con el alcance permite aprovechar también el borde de la explosión.
        if (bestCount < points.Length)
            for (int i = 0; i < points.Length; i++)
            {
                Intersections(origin, range, points[i], radius);
                for (int j = i + 1; j < points.Length && bestCount < points.Length; j++)
                    Intersections(points[i], radius, points[j], radius);
            }
        if (best is { } chosen)
        {
            var covered = points.Where(p => Squared(p, chosen) <= radius * radius + epsilon).ToArray();
            var center = new V2(covered.Average(p => p.X), covered.Average(p => p.Y));
            Consider(MagickRules.MeteorTarget(origin, center));
        }
        return best;
    }

    private static float Squared(V2 a, V2 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
