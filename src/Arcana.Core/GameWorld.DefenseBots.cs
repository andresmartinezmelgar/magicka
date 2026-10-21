namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class DefenseBotPlan
    {
        public int TargetId, HealTargetId;
        public V2 Goal;
        public float SpellTime;
        public int SpellTurn;
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
            var home = SpacedDefenseBotGoal(anchor + new V2(5 + index % 2 * 2, lane), anchor, bot.Id);
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
                plan.Goal = healing == bot ? SpacedDefenseBotGoal(bot.Position, home, bot.Id)
                    : SpacedDefenseBotGoal(healing.Position + new V2(4.5f, lane * 0.5f), home, bot.Id);
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
            var goal = target.Body.Position - forward * (target.Body.VulnerableElement == Element.Water ? 4.5f : 6) + lateral * flank;
            if (V2.Distance(goal, anchor) > 18) goal = anchor + (goal - anchor).Normalized * 18;
            plan.Goal = SpacedDefenseBotGoal(goal, home, bot.Id);
        }
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
    // All arrays resolve to legal spells through SpellRecipe. Range bands prevent
    // short cones from replacing beams against targets they could never hit.
    // The sequence is deterministic, reproducible in tests and desynchronised per bot.
    private static Element[] DefenseBotAttack(float distance, int turn)
    {
        int phase = (turn % 7 + 7) % 7;
        if (distance > 9f) return phase switch
        {
            0 or 4 => [Element.Fire, Element.Arcane, Element.Arcane], // distant fire beam
            1 or 5 => [Element.Earth, Element.Earth, Element.Fire], // charged fiery rock
            2 => [Element.Water, Element.Cold, Element.Cold], // frost shards
            _ => [Element.Arcane, Element.Arcane, Element.Arcane] // pure arcane beam
        };
        if (distance > 6f) return phase switch
        {
            0 => [Element.Lightning, Element.Fire, Element.Fire],
            1 or 5 => [Element.Water, Element.Cold, Element.Cold],
            2 => [Element.Earth, Element.Earth, Element.Fire],
            3 or 6 => [Element.Fire, Element.Arcane, Element.Arcane],
            _ => [Element.Arcane, Element.Arcane, Element.Arcane]
        };
        return phase switch
        {
            0 => [Element.Fire, Element.Fire, Element.Fire], // flame cone
            1 => [Element.Water, Element.Water, Element.Water], // water cone
            2 => [Element.Cold, Element.Cold, Element.Cold], // frost cone
            3 => [Element.Lightning, Element.Fire, Element.Fire],
            4 => [Element.Water, Element.Cold, Element.Cold],
            5 => [Element.Earth, Element.Earth, Element.Fire],
            _ => [Element.Fire, Element.Arcane, Element.Arcane]
        };
    }
    private static Element[] SafeDefenseBotAttack(float distance, int turn, bool wet)
    {
        var selection = DefenseBotAttack(distance, turn);
        // An electrical discharge hurts its caster while wet. AI must use a
        // valid alternative instead of repeatedly self-damaging.
        if (!wet || !selection.Contains(Element.Lightning)) return selection;
        return distance <= 6f
            ? [Element.Fire, Element.Fire, Element.Fire]
            : [Element.Water, Element.Cold, Element.Cold];
    }
    private static Element[] AttunedDefenseBotAttack(WizardType type, float distance, int turn, Element? weakness)
    {
        Element? element = weakness ?? WizardTypes.Affinity(type);
        int phase = Math.Abs(turn % 3);
        return element switch
        {
            Element.Fire => distance <= 6 && phase == 0 ? [Element.Fire, Element.Fire, Element.Fire]
                : phase == 1 ? [Element.Earth, Element.Fire, Element.Fire] : [Element.Arcane, Element.Fire, Element.Fire],
            Element.Cold => distance <= 6 && phase == 0 ? [Element.Cold, Element.Cold, Element.Cold]
                : phase == 1 ? [Element.Water, Element.Cold, Element.Cold] : [Element.Earth, Element.Cold, Element.Cold],
            Element.Lightning => distance <= 9 && phase == 0 ? [Element.Lightning, Element.Lightning, Element.Lightning]
                : [Element.Arcane, Element.Lightning, Element.Lightning],
            Element.Earth => phase == 1 && weakness == null ? [Element.Earth, Element.Earth, Element.Fire] : [Element.Earth, Element.Earth, Element.Earth],
            Element.Water => distance <= 6 ? [Element.Water, Element.Water, Element.Water] : [Element.Earth, Element.Water, Element.Water],
            Element.Arcane => [Element.Arcane, Element.Arcane, Element.Arcane],
            _ => DefenseBotAttack(distance, turn)
        };
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
            if (away.LengthSquared < 0.0001f)
            { float angle = bot.Id % 8 * MathF.Tau / 8; away = new(MathF.Cos(angle), MathF.Sin(angle)); }
            separation += away.Normalized * (1 - Math.Min(1, away.Length / 2.6f));
        }
        var movement = direction + separation * 1.8f;
        Wizard? closeHuman = null; float humanDistance = 3.5f;
        foreach (var player in _players.Values)
        {
            if (player.Bot || !player.Alive) continue;
            float distanceToHuman = V2.Distance(player.Position, bot.Position);
            if (distanceToHuman < humanDistance) { closeHuman = player; humanDistance = distanceToHuman; }
        }
        if (closeHuman != null)
        {
            var away = bot.Position - closeHuman.Position;
            if (away.LengthSquared < 0.001f)
            { float angle = bot.Id % 8 * MathF.Tau / 8; away = new(MathF.Cos(angle), MathF.Sin(angle)); }
            movement = away.Normalized * 1.6f + separation * 0.8f;
        }
        bot.Input.MoveX = movement.X; bot.Input.MoveY = movement.Y; bot.Input.MoveTo = false; bot.LastInput = Time;
        Wizard? target = plan.HealTargetId != 0 ? _players.GetValueOrDefault(plan.HealTargetId)
            : _minions.GetValueOrDefault(plan.TargetId)?.Body;
        bool healing = plan.HealTargetId != 0;
        if (target is not { Alive: true } || V2.Distance(bot.Position, target.Position) > 16 ||
            !Visible(bot.Position, target.Position, bot.Id))
        {
            if (bot.Channel != null || bot.Charging != null) CancelCast(bot);
            return;
        }
        bot.Input.AimX = target.Position.X; bot.Input.AimY = target.Position.Y;
        float distance = V2.Distance(bot.Position, target.Position);
        // Cycle the offensive repertoire every few seconds; healing takes precedence
        // and is never interrupted merely to vary the attacking spell.
        if (healing) plan.SpellTime = 0;
        else
        {
            plan.SpellTime -= dt;
            if (plan.SpellTime <= 0)
            {
                plan.SpellTurn++;
                plan.SpellTime = 2.15f + bot.Id % 3 * 0.23f;
            }
        }
        bot.BotDecision -= dt;
        if (bot.BotDecision > 0 || !Ready(bot)) return;
        AutoShopDefenseBot(bot);
        if (healing && target == bot)
        {
            bot.BotDecision = 0.65f;
            CancelCast(bot); bot.Selection.Clear();
            for (int i = 0; i < 3; i++) bot.Selection.Add(Element.Life);
            SelfCast(bot); return;
        }
        // Do not abort rocks mid-charge: the normal simulation releases them when
        // charged, and targeting remains updated while aiming at moving enemies.
        if (!healing && bot.Charging != null) return;
        Element? preferred = target.VulnerableElement ?? WizardTypes.Affinity(bot.WizardType);
        if (!healing && preferred == Element.Lightning && bot.Wet > 0)
        {
            CancelCast(bot); bot.Selection.Clear(); bot.Selection.Add(Element.Fire);
            SelfCast(bot); bot.BotDecision = 0.4f; return;
        }
        Element[] recipe = healing ? [Element.Life, Element.Life, Element.Life]
            : preferred == null ? SafeDefenseBotAttack(distance, plan.SpellTurn + bot.Id, bot.Wet > 0)
            : AttunedDefenseBotAttack(bot.WizardType, distance, plan.SpellTurn + bot.Id, target.VulnerableElement);
        var spell = new SpellRecipe(recipe);
        bot.BotDecision = (spell.Shape is SpellShape.Rock or SpellShape.Shards ? 0.8f : 0.45f)
            + bot.Id % 3 * 0.07f;
        if (bot.Channel is { } channel && channel.Elements.SequenceEqual(recipe)) return;
        CancelCast(bot); bot.Selection.Clear();
        foreach (var element in recipe) bot.Selection.Add(element);
        UpdateFacing(bot); BeginCast(bot);
    }
}
