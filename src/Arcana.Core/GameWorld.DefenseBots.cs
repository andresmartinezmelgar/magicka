namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class DefenseBotPlan
    {
        public int TargetId, HealTargetId;
        public V2 Goal;
    }
    private readonly Dictionary<int, DefenseBotPlan> _defenseBotPlans = [];
    private float _defenseBotPlanClock;

    private void PlanDefenseBots(float dt)
    {
        _defenseBotPlanClock -= dt;
        if (_defenseBotPlanClock > 0) return;
        _defenseBotPlanClock = 0.45f;
        var bots = _players.Values.Where(p => p.Bot && p.Alive).OrderBy(p => p.Id).ToArray();
        var humans = _players.Values.Where(p => !p.Bot && p.Deaths < DefenseRules.PlayerLives).ToArray();
        if (humans.Length == 0) return;
        var threats = _minions.Values.Where(m => m.Body.Alive && humans.Any(p => V2.Distance(m.Body.Position, p.Position) < 28)).ToArray();
        var attacks = new Dictionary<int, int>();
        var healers = new HashSet<int>(bots.Where(p => p.Health < p.MaxHealth * 0.4f).Select(p => p.Id));
        int medics = 0;
        for (int index = 0; index < bots.Length; index++)
        {
            var bot = bots[index];
            if (!_defenseBotPlans.TryGetValue(bot.Id, out var plan)) _defenseBotPlans[bot.Id] = plan = new();
            float lane = bots.Length == 1 ? 0 : (index / (float)(bots.Length - 1) * 2 - 1) * 5;
            var anchor = humans.MinBy(p => (p.Position - bot.Position).LengthSquared)!.Position;
            var home = SafeDefenseBotGoal(anchor + new V2(3 + index % 2 * 3, lane * 0.7f), anchor);
            Wizard? healing = bot.Health < bot.MaxHealth * 0.4f ? bot : null;
            if (healing == null && (threats.Length == 0 || medics < Math.Max(1, bots.Length - 1)))
            {
                // Un único sanador por aliado. Mantiene la ayuda hasta estabilizarlo, sin cambiar a cada paso.
                healing = _players.Values.Where(a => a.Alive && a.Id != bot.Id && !healers.Contains(a.Id) &&
                        a.Health < a.MaxHealth * (plan.HealTargetId == a.Id ? 0.82f : 0.55f) &&
                        V2.Distance(a.Position, bot.Position) < 12)
                    .OrderBy(a => a.Health / a.MaxHealth).ThenBy(a => a.Id).FirstOrDefault();
            }
            if (healing != null)
            {
                plan.TargetId = 0; plan.HealTargetId = healing.Id; healers.Add(healing.Id); medics++;
                plan.Goal = healing == bot ? SafeDefenseBotGoal(bot.Position, home)
                    : SafeDefenseBotGoal(healing.Position + new V2(2, lane * 0.3f), home);
                continue;
            }
            plan.HealTargetId = 0;
            bool uncovered = threats.Any(m => !attacks.ContainsKey(m.Body.Id));
            var target = threats.Where(m => !uncovered || !attacks.ContainsKey(m.Body.Id))
                .OrderBy(m => DefenseThreatScore(m, bot, anchor, plan.TargetId)).ThenBy(m => m.Body.Id).FirstOrDefault();
            plan.TargetId = target?.Body.Id ?? 0;
            if (target == null) { plan.Goal = home; continue; }
            attacks[target.Body.Id] = attacks.GetValueOrDefault(target.Body.Id) + 1;
            var forward = (target.Body.Position - anchor).Normalized;
            var lateral = new V2(-forward.Y, forward.X);
            float flank = bots.Length == 1 ? 0 : (index - (bots.Length - 1) * 0.5f) * 3;
            var goal = target.Body.Position - forward * 6 + lateral * flank;
            if (V2.Distance(goal, anchor) > 18) goal = anchor + (goal - anchor).Normalized * 18;
            plan.Goal = SafeDefenseBotGoal(goal, home);
        }
    }
    private float DefenseThreatScore(DefenseMinion minion, Wizard bot, V2 anchor, int previous)
    {
        float distance = V2.Distance(minion.Body.Position, anchor);
        return distance * 2 + V2.Distance(minion.Body.Position, bot.Position) * 0.35f
            - (distance < 7 ? 16 : 0) - (minion.Kind == MinionKind.Captain ? 5 : 0) - (minion.Body.Id == previous ? 3 : 0);
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
    private void ThinkDefenseBot(Wizard bot, float dt)
    {
        if (!_defenseBotPlans.TryGetValue(bot.Id, out var plan)) return;
        var direction = V2.Distance(bot.Position, plan.Goal) > 0.9f ? NavigationDirection(bot, plan.Goal, dt) : V2.Zero;
        var separation = V2.Zero;
        foreach (var ally in _players.Values)
        {
            if (ally.Id == bot.Id || !ally.Alive) continue;
            var away = bot.Position - ally.Position;
            if (away.LengthSquared >= 2.6f * 2.6f) continue;
            if (away.LengthSquared < 0.0001f) away = new V2(0, bot.Id < ally.Id ? -1 : 1);
            separation += away.Normalized * (1 - Math.Min(1, away.Length / 2.6f));
        }
        var movement = direction + separation * 1.8f;
        bot.Input.MoveX = movement.X; bot.Input.MoveY = movement.Y; bot.Input.MoveTo = false; bot.LastInput = Time;
        Wizard? target = plan.HealTargetId != 0 ? _players.GetValueOrDefault(plan.HealTargetId)
            : _minions.GetValueOrDefault(plan.TargetId)?.Body;
        bool healing = plan.HealTargetId != 0;
        if (target is not { Alive: true } || V2.Distance(bot.Position, target.Position) > 16 || !Visible(bot.Position, target.Position, bot.Id))
        { if (bot.Channel != null) CancelCast(bot); return; }
        bot.Input.AimX = target.Position.X; bot.Input.AimY = target.Position.Y;
        bot.BotDecision -= dt;
        if (bot.BotDecision > 0 || !Ready(bot)) return;
        bot.BotDecision = 0.45f + bot.Id % 3 * 0.07f;
        AutoShopDefenseBot(bot);
        if (healing && target == bot)
        {
            CancelCast(bot); bot.Selection.Clear();
            for (int i = 0; i < 3; i++) bot.Selection.Add(Element.Life);
            SelfCast(bot); return;
        }
        // Mantiene el rayo y su carga mientras la receta siga siendo válida, incluso al apuntar a otra amenaza.
        bool same = bot.Channel is { } channel && (healing ? channel.Healing && channel.Count(Element.Life) == 3
            : channel.Count(Element.Arcane) == 2 && channel.Count(Element.Fire) == 1);
        if (same) return;
        CancelCast(bot); bot.Selection.Clear();
        if (healing) for (int i = 0; i < 3; i++) bot.Selection.Add(Element.Life);
        else { bot.Selection.Add(Element.Fire); bot.Selection.Add(Element.Arcane); bot.Selection.Add(Element.Arcane); }
        UpdateFacing(bot); BeginCast(bot);
    }
}
