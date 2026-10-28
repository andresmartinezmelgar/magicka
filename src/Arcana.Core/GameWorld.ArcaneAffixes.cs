namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed record ArcaneBurst(int Owner, V2 Position, int Rank, double Damage);
    private readonly Queue<ArcaneBurst> _arcaneBursts = [];
    private void RecordArcaneDamage(Wizard target, int owner, double actual, Element[]? elements, ReadOnlySpan<float> weights, StatisticAttack attack)
    {
        if (!(actual > 0)) return;
        target.RecentDamage.Record(Tick, actual);
        if (attack == StatisticAttack.ArcaneMark || !AcceptsDefenseElement(target, Element.Arcane) ||
            !(weights.Length == 9 ? weights[(int)Element.Arcane] > 0 : elements?.Contains(Element.Arcane) == true) ||
            !_players.TryGetValue(owner, out var source) || !Hostile(source, target) || source.Equipment.ArcaneMark <= 0) return;
        int rank = source.Equipment.ArcaneMark;
        if (rank < target.ArcaneMarkRank) return;
        target.ArcaneMarkOwner = owner; target.ArcaneMarkRank = rank;
    }
    private void QueueArcaneMarkDeath(Wizard target)
    {
        if (target.ArcaneMarkRank > 0 && _arcaneBursts.Count < CombatAffixes.ArcaneBurstLimit)
        {
            double damage = target.RecentDamage.Sum(Tick, CombatAffixes.ArcaneMarkSeconds(target.ArcaneMarkRank));
            if (damage > 0) _arcaneBursts.Enqueue(new(target.ArcaneMarkOwner, target.Position, target.ArcaneMarkRank, damage));
        }
        target.ArcaneMarkOwner = target.ArcaneMarkRank = 0; target.RecentDamage.Clear();
    }
    private void ResolveArcaneBursts()
    {
        // Consumir la marca antes de encolar evita repetir una misma muerte.
        // Las muertes de otros enemigos ya marcados se resuelven en cola, sin recursión.
        Span<float> weights = stackalloc float[9]; weights.Clear(); weights[(int)Element.Arcane] = 1;
        int resolved = 0;
        while (_arcaneBursts.TryDequeue(out var burst) && resolved++ < CombatAffixes.ArcaneBurstLimit)
        {
            if (!TryCombatSource(burst.Owner, out var owner)) continue;
            float radius = CombatAffixes.ArcaneMarkRadius(burst.Rank);
            _statistics.Cast(owner.Id, StatisticAttack.ArcaneMark, [Element.Arcane]);
            Emit("arcane_burst", burst.Position, [Element.Arcane], "", radius);
            foreach (var enemy in _orderedPlayers)
            {
                if (!enemy.Alive || !Hostile(owner, enemy) || V2.Distance(enemy.Position, burst.Position) > radius ||
                    !Visible(burst.Position, enemy.Position, owner.Id)) continue;
                float resistance = WizardTypes.Incoming(enemy.WizardType, Element.Arcane) * Resistance(enemy, Element.Arcane);
                if (resistance <= 0) { _statistics.Immune(owner.Id, enemy, [Element.Arcane]); continue; }
                ApplyDamage(enemy, (float)(burst.Damage * resistance), owner.Id, [Element.Arcane], shieldOnly: false,
                    resolvedElements: true, weights: weights, cause: StatisticAttack.ArcaneMark, replayed: true);
            }
        }
        _arcaneBursts.Clear();
    }
    private static void ClearArcaneTarget(Wizard target)
    { target.ArcaneMarkOwner = target.ArcaneMarkRank = 0; target.RecentDamage.Clear(); }
    private void RemoveArcaneOwner(int id)
    {
        foreach (var target in _combatants.Values)
            if (target.ArcaneMarkOwner == id) target.ArcaneMarkOwner = target.ArcaneMarkRank = 0;
        int count = _arcaneBursts.Count;
        while (count-- > 0) { var burst = _arcaneBursts.Dequeue(); if (burst.Owner != id) _arcaneBursts.Enqueue(burst); }
    }

    private void TickArcaneEcho(CastRay ray, float dt)
    {
        var caster = ray.Caster;
        if (_beams.Count >= 64 || !caster.Alive || caster.Frozen > 0 || caster.Channel == null || caster.Equipment.ArcaneEcho <= 0 ||
            !ray.Part.Recipe.Has(Element.Arcane) || !_combatants.TryGetValue(ray.TargetId, out var aimed) || !Hostile(caster, aimed))
        { caster.ArcaneEchoActive = false; return; }
        Wizard? nearest = null; V2 origin = default, direction = default;
        float distance = float.PositiveInfinity, range = ray.Part.Recipe.Range * caster.Equipment.Range;
        foreach (var enemy in _orderedPlayers)
        {
            if (!enemy.Alive || enemy.Id == aimed.Id || !Hostile(caster, enemy)) continue;
            float separation = V2.Distance(caster.Position, enemy.Position);
            if (separation > range || separation > distance || separation == distance && nearest != null && enemy.Id > nearest.Id) continue;
            var aim = (enemy.Position - caster.Position).Normalized;
            if (aim.LengthSquared < .01f) continue;
            var from = SpellOrigin(caster, aim, .7f);
            BeamEnd(from, aim, range, caster.Id, out _, out var first, caster.Id);
            if (first?.Id != enemy.Id) continue; // Ni aliados, ni el objetivo principal, ni muros de por medio.
            nearest = enemy; distance = separation; origin = from; direction = aim;
        }
        if (nearest == null) { caster.ArcaneEchoActive = false; return; }
        if (!caster.ArcaneEchoActive) _statistics.Cast(caster.Id, StatisticAttack.ArcaneEcho, ray.Part.Recipe.Elements);
        caster.ArcaneEchoActive = true;
        var echo = ray.Part with { Scale = ray.Part.Scale * CombatAffixes.ArcaneEchoPercent(caster.Equipment.ArcaneEcho) / 100f };
        var previous = _statisticsAttack; _statisticsAttack = StatisticAttack.ArcaneEcho;
        try { TraceBeam(origin, direction, range, [echo], caster.Id, caster.Id, dt, "direct", allowPenetration: false); }
        finally { _statisticsAttack = previous; }
    }
}
