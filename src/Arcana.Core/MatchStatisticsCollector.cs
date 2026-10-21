namespace Arcana.Core;

internal sealed class MatchStatisticsCollector
{
    private readonly Dictionary<(StatisticGroup Group, int Key, StatisticMeasure Measure), double> _values = [];
    private readonly Dictionary<int, (int Code, MinionKind? Kind)> _actors = [];
    private readonly HashSet<int> _dead = [];
    private readonly Queue<int> _retired = [];
    private long _ticks;
    private int _humans, _bots, _dropped;
    public string RunId { get; private set; } = Guid.NewGuid().ToString("N");
    private string _day = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    public bool Network, Continuation;
    public void Add(StatisticGroup group, int key, StatisticMeasure measure, double value)
    {
        if (!(value > 0) || !double.IsFinite(value)) return;
        var bucket = (group, key, measure);
        if (!_values.ContainsKey(bucket) && _values.Count >= MatchStatisticsReport.MaxMetrics - (group == StatisticGroup.Total ? 0 : 64))
        { if (_dropped < int.MaxValue) _dropped++; return; }
        _values[bucket] = Math.Min(1e18, _values.GetValueOrDefault(bucket) + value);
    }
    public void Total(StatisticMeasure measure, double value = 1) => Add(StatisticGroup.Total, 0, measure, value);
    public void Actor(int id, StatisticMeasure measure, double value = 1)
    {
        if (!_actors.TryGetValue(id, out var actor)) return;
        Add(StatisticGroup.Actor, actor.Code, measure, value);
        if (actor.Kind is { } kind) Add(StatisticGroup.Enemy, (int)kind, measure, value);
    }
    public void Bind(Wizard body, MinionKind? kind = null, bool spawn = true)
    {
        _actors[body.Id] = (kind == null ? (int)body.WizardType + (body.Bot ? 16 : 0) + body.Team * 32 : 64 + (int)kind, kind);
        _dead.Remove(body.Id);
        if (spawn) { Total(StatisticMeasure.Spawns); Actor(body.Id, StatisticMeasure.Spawns); Actor(body.Id, StatisticMeasure.SpawnHealth, body.MaxHealth); }
    }
    public void Retire(int id)
    {
        _retired.Enqueue(id);
        if (_retired.Count > 2048) { int old = _retired.Dequeue(); _actors.Remove(old); _dead.Remove(old); }
    }
    public void Step(int humans, int bots, int enemies)
    {
        _ticks++; _humans = Math.Max(_humans, humans); _bots = Math.Max(_bots, bots);
        Add(StatisticGroup.Performance, 0, StatisticMeasure.Samples, 1);
        Add(StatisticGroup.Performance, 0, StatisticMeasure.ValueSum, enemies);
    }
    public void Status(Wizard p, double dt)
    {
        Actor(p.Id, StatisticMeasure.AliveSeconds, dt);
        if (p.Wet > 0) Actor(p.Id, StatisticMeasure.WetSeconds, dt);
        if (p.Burning > 0) Actor(p.Id, StatisticMeasure.BurningSeconds, dt);
        if (p.Frozen > 0) Actor(p.Id, StatisticMeasure.FrozenSeconds, dt);
    }
    public static int Recipe(Element[]? elements)
    {
        int key = 0, count = Math.Min(3, elements?.Length ?? 0);
        Span<int> sorted = stackalloc int[3];
        for (int i = 0; i < count; i++) { int j = i, value = (int)elements![i] + 1; while (j > 0 && sorted[j - 1] > value) { sorted[j] = sorted[j - 1]; j--; } sorted[j] = value; }
        for (int i = 0; i < count; i++) key = key * 9 + sorted[i];
        return key;
    }
    public static StatisticAttack Shape(SpellShape shape) => shape switch
    {
        SpellShape.Beam => StatisticAttack.Beam, SpellShape.Lightning => StatisticAttack.Lightning,
        SpellShape.Spray => StatisticAttack.Spray, SpellShape.Rock => StatisticAttack.Rock, SpellShape.Shards => StatisticAttack.Shards,
        SpellShape.Barrier => StatisticAttack.Barrier, _ => StatisticAttack.Force
    };
    private StatisticGroup Role(int owner, bool element)
    {
        if (!_actors.TryGetValue(owner, out var a)) return element ? StatisticGroup.EnvironmentElement : StatisticGroup.EnvironmentAttack;
        return a.Kind != null ? element ? StatisticGroup.EnemyElementDealt : StatisticGroup.EnemyAttack
            : a.Code % 32 >= 16 ? element ? StatisticGroup.BotElement : StatisticGroup.BotAttack
            : element ? StatisticGroup.HumanElement : StatisticGroup.HumanAttack;
    }
    public void Attack(int owner, StatisticAttack attack, StatisticMeasure measure, double value = 1)
    { Add(StatisticGroup.Attack, (int)attack, measure, value); Add(Role(owner, false), (int)attack, measure, value); }
    public void Cast(int owner, StatisticAttack attack, Element[]? elements = null)
    {
        Total(StatisticMeasure.Casts); Actor(owner, StatisticMeasure.Casts); Attack(owner, attack, StatisticMeasure.Casts);
        Add(StatisticGroup.Recipe, Recipe(elements), StatisticMeasure.Casts, 1);
        if (elements != null) foreach (var e in elements) { Add(StatisticGroup.Element, (int)e, StatisticMeasure.Casts, 1); Add(Role(owner, true), (int)e, StatisticMeasure.Casts, 1); }
    }
    public void Immune(int owner, Wizard target, Element[] elements)
    {
        if (target.VulnerableElement == null) return;
        foreach (var e in elements.Distinct()) if (e != target.VulnerableElement && e is not (Element.Life or Element.Shield))
        {
            Total(StatisticMeasure.ImmuneHits); Add(StatisticGroup.Element, (int)e, StatisticMeasure.ImmuneHits, 1);
            Add(Role(owner, true), (int)e, StatisticMeasure.ImmuneHits, 1);
            if (_actors.TryGetValue(target.Id, out var a) && a.Kind is {} kind)
                Add(StatisticGroup.EnemyElement, (int)kind * 9 + (int)e, StatisticMeasure.ImmuneHits, 1);
        }
    }
    public void Damage(int owner, Wizard target, double health, double shield, double overkill, Element[]? elements,
        StatisticAttack attack, bool friendly, ReadOnlySpan<float> weights = default)
    {
        if (!(health > 0 || shield > 0)) return;
        Total(StatisticMeasure.Hits); Actor(owner, StatisticMeasure.Hits); Attack(owner, attack, StatisticMeasure.Hits);
        DamageValue(owner, attack, elements, StatisticMeasure.Damage, health);
        DamageValue(owner, attack, elements, StatisticMeasure.ShieldDamage, shield);
        DamageValue(owner, attack, elements, StatisticMeasure.Overkill, overkill);
        Actor(target.Id, StatisticMeasure.DamageReceived, health); Actor(target.Id, StatisticMeasure.ShieldAbsorbed, shield);
        if (friendly) Total(owner == target.Id ? StatisticMeasure.SelfDamage : StatisticMeasure.FriendlyDamage, health + shield);
        Span<float> contributions = stackalloc float[9];
        if (weights.Length == 9) weights.CopyTo(contributions);
        else if (elements is { Length: > 0 }) foreach (var e in elements) { if (target.VulnerableElement == null || e == target.VulnerableElement) contributions[(int)e]++; }
        else contributions[8] = 1;
        float total = 0; foreach (float c in contributions) total += c;
        if (!(total > 0)) return;
        for (int e = 0; e < 9; e++) if (contributions[e] > 0)
        {
            double fraction = contributions[e] / total;
            Add(StatisticGroup.Element, e, StatisticMeasure.Damage, health * fraction);
            Add(StatisticGroup.Element, e, StatisticMeasure.ShieldDamage, shield * fraction);
            Add(Role(owner, true), e, StatisticMeasure.Damage, health * fraction);
            Add(Role(owner, true), e, StatisticMeasure.ShieldDamage, shield * fraction);
            if (_actors.TryGetValue(target.Id, out var a) && a.Kind is {} kind)
            { Add(StatisticGroup.EnemyElement, (int)kind * 9 + e, StatisticMeasure.DamageReceived, health * fraction); Add(StatisticGroup.EnemyElement, (int)kind * 9 + e, StatisticMeasure.ShieldAbsorbed, shield * fraction); }
        }
    }
    private void DamageValue(int owner, StatisticAttack attack, Element[]? elements, StatisticMeasure measure, double value)
    { Total(measure, value); Actor(owner, measure, value); Attack(owner, attack, measure, value); Add(StatisticGroup.Recipe, Recipe(elements), measure, value); }
    public void BarrierDamage(int owner, int defender, double value, Element[]? elements, StatisticAttack attack)
    {
        if (!(value > 0)) return;
        DamageValue(owner, attack, elements, StatisticMeasure.ShieldDamage, value);
        Actor(defender, StatisticMeasure.ShieldAbsorbed, value);
        int count = elements?.Length ?? 0;
        if (count == 0) { Add(StatisticGroup.Element, 8, StatisticMeasure.ShieldDamage, value); Add(Role(owner, true), 8, StatisticMeasure.ShieldDamage, value); }
        else foreach (var e in elements!) { Add(StatisticGroup.Element, (int)e, StatisticMeasure.ShieldDamage, value / count); Add(Role(owner, true), (int)e, StatisticMeasure.ShieldDamage, value / count); }
    }
    public void Death(Wizard target, int owner, StatisticAttack attack = StatisticAttack.EnemyArea)
    {
        if (!_dead.Add(target.Id)) return;
        Total(StatisticMeasure.Deaths); Actor(target.Id, StatisticMeasure.Deaths);
        if (owner != target.Id) { Total(StatisticMeasure.Kills); Actor(owner, StatisticMeasure.Kills); Attack(owner, attack, StatisticMeasure.Kills); }
    }
    public void Heal(int owner, Wizard target, double actual, StatisticAttack attack)
    { Total(StatisticMeasure.Healing, actual); Actor(owner, StatisticMeasure.Healing, actual); Actor(target.Id, StatisticMeasure.HealingReceived, actual); Attack(owner, attack, StatisticMeasure.Healing, actual); }
    public void Shield(int owner, double actual, bool repair = false)
    { var m = repair ? StatisticMeasure.ShieldRepaired : StatisticMeasure.ShieldGranted; Total(m, actual); Actor(owner, m, actual); }
    public MatchStatisticsReport Capture(GameWorld world, MatchRecordStatus status) => new(MatchStatisticsReport.CurrentSchema, RunId, _ticks,
        BuildInfo.Version, _day, world.Options.Mode, world.Map.Id, Network, world.MatchEnded ? MatchRecordStatus.Completed : status,
        _ticks / 60.0, _humans, _bots, world.StatisticsDifficulty,
        world.MatchEnded && world.Winner >= 0 ? world.Options.Mode == GameMode.Arena ? world.Players.GetValueOrDefault(world.Winner)?.Team ?? -1 : world.Winner : -1,
        [.. world.Scores], _values.OrderBy(p => p.Key.Group).ThenBy(p => p.Key.Key).ThenBy(p => p.Key.Measure).Select(p => new MatchStatistic(p.Key.Group, p.Key.Key, p.Key.Measure, p.Value)).ToArray(), _dropped, Continuation);
    public static MatchStatisticsCollector Restore(MatchStatisticsReport report)
    {
        report.Validate();
        var c = new MatchStatisticsCollector { RunId = report.RunId, _day = report.Day, _ticks = report.Revision,
            Network = report.Network, Continuation = report.Continuation, _humans = report.Humans, _bots = report.Bots, _dropped = report.DroppedBuckets };
        foreach (var m in report.Metrics) c._values[(m.Group, m.Key, m.Measure)] = m.Value;
        return c;
    }
}
