namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void ClearDefensePlanningScratch()
    {
        _planningBots.Clear(); _planningHumans.Clear(); _planningThreats.Clear();
        _planningAttacks.Clear(); _planningHealers.Clear();
    }

    private void PlanDefenseBots(float dt)
    {
        _defenseBotPlanClock -= dt;
        if (_defenseBotPlanClock > 0) return;
        _defenseBotPlanClock = 0.45f;
        ClearDefensePlanningScratch();
        var bots = _planningBots; var humans = _planningHumans; var threats = _planningThreats;
        var attacks = _planningAttacks; var healers = _planningHealers;
        foreach (var player in _players.Values)
        {
            if (player.Bot && player.Alive) bots.Add(player);
            else if (!player.Bot && player.Deaths < DefenseRules.PlayerLives) humans.Add(player);
        }
        if (humans.Count == 0 || bots.Count == 0) { ClearDefensePlanningScratch(); return; }
        bots.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        foreach (var minion in _minions.Values)
        {
            if (!minion.Body.Alive) continue;
            foreach (var human in humans)
                if (V2.Distance(minion.Body.Position, human.Position) < 28)
                { threats.Add(minion); break; }
        }
        foreach (var bot in bots) if (bot.Health < bot.MaxHealth * 0.4f) healers.Add(bot.Id);
        int medics = 0;
        for (int index = 0; index < bots.Count; index++)
        {
            var bot = bots[index];
            if (!_defenseBotPlans.TryGetValue(bot.Id, out var plan)) _defenseBotPlans[bot.Id] = plan = new();
            float lane = bots.Count == 1 ? 0 : (index / (float)(bots.Count - 1) * 2 - 1) * 5;
            var anchor = ClosestDefenseHuman(humans, bot)!.Position;
            var home = SpacedDefenseBotGoal(anchor + new V2(5 + index % 2 * 2, lane), anchor, bot.Id);
            Wizard? healing = bot.Health < bot.MaxHealth * 0.4f ? bot : null;
            if (healing == null && (threats.Count == 0 || medics < Math.Max(1, bots.Count - 1)))
            {
                // Un único sanador por aliado. Mantiene la ayuda hasta estabilizarlo, sin cambiar a cada paso.
                healing = SelectDefenseHealingTarget(bot, plan.HealTargetId, healers);
            }
            if (healing != null)
            {
                plan.TargetId = 0; plan.HealTargetId = healing.Id; healers.Add(healing.Id); medics++;
                plan.Goal = healing == bot ? SpacedDefenseBotGoal(bot.Position, home, bot.Id)
                    : SpacedDefenseBotGoal(healing.Position + new V2(4.5f, lane * 0.5f), home, bot.Id);
                continue;
            }
            plan.HealTargetId = 0;
            var target = SelectDefenseThreat(threats, attacks, bot, anchor, plan.TargetId);
            plan.TargetId = target?.Body.Id ?? 0;
            if (target == null) { plan.Goal = home; continue; }
            attacks.Add(target.Body.Id);
            var forward = (target.Body.Position - anchor).Normalized;
            var lateral = new V2(-forward.Y, forward.X);
            float flank = bots.Count == 1 ? 0 : (index - (bots.Count - 1) * 0.5f) * 3;
            var goal = target.Body.Position - forward * (target.Body.VulnerableElement == Element.Water ? 4.5f : 6) + lateral * flank;
            if (V2.Distance(goal, anchor) > 18) goal = anchor + (goal - anchor).Normalized * 18;
            plan.Goal = SpacedDefenseBotGoal(goal, home, bot.Id);
        }
        ClearDefensePlanningScratch();
    }

    private float DefenseThreatScore(DefenseMinion minion, Wizard bot, V2 anchor, int previous)
    {
        float distance = V2.Distance(minion.Body.Position, anchor);
        return distance * 2 + V2.Distance(minion.Body.Position, bot.Position) * 0.35f
            - (distance < 7 ? 16 : 0) - (minion.Kind is MinionKind.Captain or MinionKind.Oracle ? 5 : 0) - (minion.Body.Id == previous ? 3 : 0);
    }

    private V2 SpacedDefenseBotGoal(V2 goal, V2 home, int botId)
    {
        bool Free(V2 at) => Walkable(at) && _players.Values.All(p => p.Bot || !p.Alive || V2.Distance(at, p.Position) >= 3.5f);
        if (Free(goal)) return goal;
        if (Free(home)) return home;
        var anchor = _players.Values.Where(p => !p.Bot && p.Alive).MinBy(p => V2.Distance(goal, p.Position));
        if (anchor == null) return SafeDefenseBotGoal(goal, home);
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < 8; i++)
            {
                float angle = (i + botId % 8) * MathF.Tau / 8;
                var candidate = anchor.Position + new V2(MathF.Cos(angle), MathF.Sin(angle)) * (4.5f + ring * 2);
                if (Free(candidate)) return candidate;
            }
        return SafeDefenseBotGoal(goal, home);
    }

    private V2 SafeDefenseBotGoal(V2 goal, V2 home)
    {
        for (int i = 0; i <= 10; i++)
        {
            var candidate = V2.Lerp(goal, home, i / 10f);
            if (Walkable(candidate)) return candidate;
        }
        return home;
    }
}
