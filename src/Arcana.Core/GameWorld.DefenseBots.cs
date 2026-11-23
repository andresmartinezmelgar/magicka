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
    private readonly List<Wizard> _planningBots = [], _planningHumans = [];
    private readonly List<DefenseMinion> _planningThreats = [];
    private readonly HashSet<int> _planningAttacks = [], _planningHealers = [];
    private float _defenseBotPlanClock;

    private const int DefenseBotPriorityCycle = 3;

    private void ThinkDefenseBot(Wizard bot, float dt)
    {
        if (!_defenseBotPlans.TryGetValue(bot.Id, out var plan)) return;
        var direction = V2.Distance(bot.Position, plan.Goal) > 0.9f
            ? NavigationDirection(bot, plan.Goal, dt, useTeleports: true) : V2.Zero;
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
        int turn = plan.SpellTurn + bot.Id;
        bool wet = bot.Wet > 0;
        Element? weakness = target.VulnerableElement;
        Element? attack = healing ? null : weakness ?? RotateDefenseBotElement(PreferredDefenseBotElement(bot), turn, wet);
        if (attack == Element.Lightning && wet)
        {
            CancelCast(bot); bot.Selection.Clear(); bot.Selection.Add(Element.Fire);
            SelfCast(bot); bot.BotDecision = 0.4f; return;
        }
        // Conserva las tres variantes del hechizo preferido al intercalar otros elementos.
        int recipeTurn = weakness.HasValue ? turn : turn - turn / DefenseBotPriorityCycle;
        Element[] recipe = healing ? [Element.Life, Element.Life, Element.Life]
            : attack == null ? SafeDefenseBotAttack(distance, turn, wet)
            : AttunedDefenseBotAttack(attack.Value, distance / bot.Equipment.Range, recipeTurn, weakness.HasValue);
        var spell = new SpellRecipe(recipe);
        bot.BotDecision = (spell.Shape is SpellShape.Rock or SpellShape.Shards ? 0.8f : 0.45f)
            + bot.Id % 3 * 0.07f;
        if (bot.Channel is { } channel && channel.Elements.SequenceEqual(recipe)) return;
        CancelCast(bot); bot.Selection.Clear();
        foreach (var element in recipe) bot.Selection.Add(element);
        UpdateFacing(bot); BeginCast(bot);
    }
}
