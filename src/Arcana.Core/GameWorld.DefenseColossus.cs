namespace Arcana.Core;

public sealed partial class GameWorld
{
    private const float ColossusFissureRange = 13, ColossusFissureDamage = 210;
    private static readonly SpellRecipe ColossusFissure = new([Element.Earth, Element.Fire]);
    private float _colossusMomentumRemaining;

    private static bool IsColossusFissure(Field field, int owner) =>
        field.Owner == owner && field.Kind == DefenseRules.ColossusFissureKind && field.Lifetime > 0;

    private bool TryBeginColossusFissures(DefenseMinion colossus, Wizard primary)
    {
        var body = colossus.Body;
        if (body.Health > body.MaxHealth * DefenseRules.ColossusFissureHealth || colossus.SpellTurn++ % 2 != 0) return false;

        int before = _fields.Count;
        foreach (var defender in _players.Values.OrderByDescending(p => p.Id == primary.Id))
        {
            if (_fields.Count >= 40) break;
            if (!defender.Alive || _unavailableDefenders.Contains(defender.Id) ||
                V2.Distance(body.Position, defender.Position) > ColossusFissureRange || !Visible(body.Position, defender.Position, body.Id) ||
                _fields.Any(f => IsColossusFissure(f, body.Id) && V2.Distance(f.Position, defender.Position) < DefenseRules.ColossusFissureRadius)) continue;
            _fields.Add(new Field
            {
                Id = _nextEntity++, Owner = body.Id, Position = defender.Position, Recipe = ColossusFissure,
                Kind = DefenseRules.ColossusFissureKind, Radius = DefenseRules.ColossusFissureRadius,
                Delay = DefenseRules.ColossusFissureWindup, Lifetime = DefenseRules.ColossusFissureWindup + 0.2f,
                TriggerRadius = 0, Power = ColossusFissureDamage
            });
        }
        return _fields.Count > before;
    }

    private bool HoldColossusFissures(DefenseMinion colossus)
    {
        var body = colossus.Body;
        if (!_fields.Any(f => IsColossusFissure(f, body.Id))) return false;
        body.Input.MoveX = body.Input.MoveY = 0;
        if (body.Frozen > 0 || body.StunTime > 0) CancelColossusFissures(body.Id);
        return true;
    }

    private void CancelColossusFissures(int owner)
    {
        foreach (var field in _fields)
            if (IsColossusFissure(field, owner)) field.Lifetime = 0;
    }

    private void ResolveColossusFissures(Field first)
    {
        if (!_minions.TryGetValue(first.Owner, out var colossus) || colossus.Kind != MinionKind.Boss ||
            !colossus.Body.Alive || colossus.Body.Frozen > 0 || colossus.Body.StunTime > 0)
        { CancelColossusFissures(first.Owner); return; }

        var fissures = _fields.Where(f => IsColossusFissure(f, first.Owner)).ToArray();
        _statistics.Cast(first.Owner, StatisticAttack.EnemyArea, ColossusFissure.Elements);
        // Los círculos de una misma tanda pueden solaparse. Cada defensor recibe un solo golpe.
        foreach (var defender in _players.Values)
            if (defender.Alive && Visible(colossus.Body.Position, defender.Position, first.Owner) &&
                fissures.Any(f => V2.Distance(f.Position, defender.Position) <= f.Radius))
                DamageAs(StatisticAttack.EnemyArea, defender, first.Power, first.Owner, ColossusFissure.Elements);

        foreach (var field in fissures)
        {
            field.Lifetime = 0;
            Emit("nova", field.Position, ColossusFissure.Elements, "Fisura del coloso", field.Radius, first.Owner);
        }
    }
}
