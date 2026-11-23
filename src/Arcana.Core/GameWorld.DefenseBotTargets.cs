namespace Arcana.Core;

public sealed partial class GameWorld
{
    private static Wizard? ClosestDefenseHuman(List<Wizard> humans, Wizard bot)
    {
        Wizard? closest = null;
        float bestDistance = 0;
        foreach (var human in humans)
        {
            float distance = (human.Position - bot.Position).LengthSquared;
            if (closest == null || distance.CompareTo(bestDistance) < 0)
            { closest = human; bestDistance = distance; }
        }
        return closest;
    }

    private Wizard? SelectDefenseHealingTarget(Wizard bot, int previous, HashSet<int> healers)
    {
        Wizard? target = null;
        float bestHealth = 0;
        foreach (var candidate in _players.Values)
        {
            if (candidate.Alive && candidate.Id != bot.Id && !healers.Contains(candidate.Id) &&
                candidate.Health < candidate.MaxHealth * (previous == candidate.Id ? 0.82f : 0.55f) &&
                V2.Distance(candidate.Position, bot.Position) < 12)
            {
                float health = candidate.Health / candidate.MaxHealth;
                int comparison = health.CompareTo(bestHealth);
                if (target == null || comparison < 0 || comparison == 0 && candidate.Id < target.Id)
                { target = candidate; bestHealth = health; }
            }
        }
        return target;
    }

    private DefenseMinion? SelectDefenseThreat(List<DefenseMinion> threats, HashSet<int> attacks,
        Wizard bot, V2 anchor, int previous)
    {
        bool uncovered = false;
        foreach (var minion in threats)
            if (!attacks.Contains(minion.Body.Id)) { uncovered = true; break; }
        DefenseMinion? target = null;
        float bestScore = 0;
        foreach (var minion in threats)
        {
            if (uncovered && attacks.Contains(minion.Body.Id)) continue;
            float score = DefenseThreatScore(minion, bot, anchor, previous);
            int comparison = score.CompareTo(bestScore);
            // Match OrderBy(score).ThenBy(id), including equal keys and floating-point edge cases.
            if (target == null || comparison < 0 || comparison == 0 && minion.Body.Id < target.Body.Id)
            { target = minion; bestScore = score; }
        }
        return target;
    }
}
