namespace Arcana.Core;

/// <summary>Alcances y objetivos compartidos por la simulación y la previsualización del cliente.</summary>
public static class MagickRules
{
    public const float BlinkRange = 7, MeteorRange = 14, ReanimationRange = 8;

    public static V2 BlinkDestination(V2 origin, V2 target, Func<V2, V2, bool> clearPath)
    {
        var direction = (target - origin).Normalized;
        float distance = Math.Min(BlinkRange, V2.Distance(origin, target));
        var last = origin;
        for (float d = 0.15f; d < distance + 0.15f; d += 0.15f)
        {
            var candidate = origin + direction * Math.Min(d, distance);
            if (!clearPath(last, candidate)) break;
            last = candidate;
            if (d >= distance) break;
        }
        return last;
    }

    public static V2 MeteorTarget(V2 origin, V2 target) =>
        V2.Distance(origin, target) > MeteorRange ? origin + (target - origin).Normalized * MeteorRange : target;

    public static bool CanReanimate(GameMode mode, int sourceTeam, int targetTeam, bool alive, int deaths, float distance) =>
        mode != GameMode.Arena && sourceTeam == targetTeam && !alive && distance < ReanimationRange &&
        (mode != GameMode.Defense || deaths < DefenseRules.PlayerLives);

    public static WizardSnapshot? ReanimationTarget(WorldSnapshot world, WizardSnapshot source) => world.Players
        .Where(p => p.Id != source.Id && CanReanimate(world.Mode, source.Team, p.Team, p.Alive, p.Deaths, V2.Distance(source.Position, p.Position)))
        .OrderBy(p => V2.Distance(source.Position, p.Position)).ThenBy(p => p.Id).FirstOrDefault();
}
