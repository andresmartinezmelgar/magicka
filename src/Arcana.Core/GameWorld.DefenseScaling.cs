namespace Arcana.Core;

public sealed partial class GameWorld
{
    private readonly HashSet<int> _unavailableDefenders = [];
    private int ActiveDefensePartySize => _players.Values.Count(p => p.Alive &&
        p.Deaths < DefenseRules.PlayerLives && !_unavailableDefenders.Contains(p.Id));

    /// <summary>Solo el servidor decide si un humano terminó de cargar y conserva conexión.</summary>
    public void SetDefensePlayerActive(int playerId, bool active)
    {
        if (!IsDefense || !_players.TryGetValue(playerId, out var player) || player.Bot) return;
        if (active) _unavailableDefenders.Remove(playerId);
        else _unavailableDefenders.Add(playerId);
    }

    private void UpdateDefensePartyScaling()
    {
        _defensePartySize = Math.Clamp(ActiveDefensePartySize, 1, DefenseRules.MaxDefenders);
        foreach (var enemy in _minions.Values)
        {
            if (!enemy.Body.Alive || enemy.PartySize == _defensePartySize) continue;
            float healthRatio = DefenseRules.PartyHealthMultiplier(enemy.Kind, _defensePartySize) /
                DefenseRules.PartyHealthMultiplier(enemy.Kind, enemy.PartySize);
            float powerRatio = DefenseRules.PartyDamageMultiplier(_defensePartySize) /
                DefenseRules.PartyDamageMultiplier(enemy.PartySize);
            ScaleDefenseEnemy(enemy, healthRatio, powerRatio);
            enemy.PartySize = _defensePartySize;
            UpdateDefenseEnemyDetails(enemy, spawned: false);
        }
        TrimExcessDefensePrisms();
    }

    private static void ScaleDefenseEnemy(DefenseMinion enemy, float healthRatio, float powerRatio)
    {
        // Mantener el porcentaje de vida y de escudo, sin curar al entrar otro defensor.
        enemy.Body.Health *= healthRatio; enemy.Body.MaxHealth *= healthRatio;
        enemy.Strength *= powerRatio;
        enemy.Body.Equipment = enemy.Body.Equipment with { Power = enemy.Body.Equipment.Power * powerRatio };
        enemy.Body.Shield *= powerRatio; enemy.Body.ShieldMax *= powerRatio;
    }

    private void TrimExcessDefensePrisms()
    {
        int excess = _minions.Values.Count(m => m.Kind == MinionKind.Prism && m.Body.Alive) -
            DefenseRules.PrismLimit(ActiveDefensePartySize);
        if (excess <= 0) return;
        // Retirar primero los más alejados; no es una muerte ni genera oro o botín.
        var distant = _minions.Values.Where(m => m.Kind == MinionKind.Prism && m.Body.Alive)
            .OrderByDescending(m => _players.Values.Where(p => p.Alive && !_unavailableDefenders.Contains(p.Id))
                .Select(p => V2.Distance(p.Position, m.Body.Position)).DefaultIfEmpty(float.MaxValue).Min())
            .ThenByDescending(m => m.Body.Id).Take(excess).ToArray();
        foreach (var enemy in distant) RetireDefenseMinion(enemy);
        _prismSpawnCooldown = Math.Max(_prismSpawnCooldown, DefenseRules.PrismRespawnDelay);
    }

    private void RetireDefenseMinion(DefenseMinion enemy)
    {
        if (enemy.Kind == MinionKind.Prism)
            _prismSpawnCooldown = Math.Max(_prismSpawnCooldown, DefenseRules.PrismRespawnDelay);
        var body = enemy.Body;
        CancelCast(body);
        RememberEffectSource(body.Id, enemy.Kind, body.Equipment.Power);
        _statistics.Retire(body.Id);
        _minions.Remove(body.Id); _combatants.Remove(body.Id); _orderedPlayers.Remove(body); _routes.Remove(body.Id);
        _projectiles.RemoveAll(p => p.Owner == body.Id); _fields.RemoveAll(f => f.Owner == body.Id);
    }
}
