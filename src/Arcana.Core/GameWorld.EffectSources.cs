namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed record LingeringSource(Wizard Body, MinionKind Kind);
    private readonly Dictionary<int, LingeringSource> _lingeringSources = [];
    private readonly HashSet<int> _effectOwners = [];

    // Los cadáveres salen del combate; sus efectos conservan bando y potencia.
    private bool TryCombatSource(int id, out Wizard source)
    {
        if (_combatants.TryGetValue(id, out source!)) return true;
        if (_lingeringSources.TryGetValue(id, out var lingering)) { source = lingering.Body; return true; }
        source = null!; return false;
    }

    private void RememberEffectSource(int id, MinionKind kind, float power)
    {
        // Los minions solo modifican potencia y velocidad. Una fuente retirada no
        // se mueve, recibe daño, revive ni participa en búsquedas de objetivos.
        var body = new Wizard { Id = id, Name = DefenseRules.MinionName(kind), Team = 1, Bot = true,
            Role = CombatantRole.Minion, WizardType = DefenseRules.EnemyWizardType(kind), Health = 0, Invulnerable = 0,
            Equipment = EquipmentModifiers.None with { Power = power } };
        _lingeringSources[id] = new(body, kind);
    }

    private void CleanupEffectSources()
    {
        if (_lingeringSources.Count == 0) return;
        _effectOwners.Clear();
        foreach (var p in _projectiles) if (p.Lifetime > 0) _effectOwners.Add(p.Owner);
        foreach (var w in _walls) if (w.Lifetime > 0 && w.Health > 0) _effectOwners.Add(w.Owner);
        foreach (var f in _fields) if (f.Lifetime > 0) _effectOwners.Add(f.Owner);
        foreach (var p in _combatants.Values) if (p.Alive && p.Burning > 0) _effectOwners.Add(p.BurnOwner);
        foreach (int id in _lingeringSources.Keys.Where(id => !_effectOwners.Contains(id)).ToArray())
            _lingeringSources.Remove(id);
    }
}
