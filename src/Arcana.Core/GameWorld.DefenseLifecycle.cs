namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void CleanupDefense()
    {
        if (!IsDefense) return;
        _deadMinionIds.Clear();
        foreach (var entry in _minions)
            if (!entry.Value.Body.Alive) _deadMinionIds.Add(entry.Key);
        foreach (int id in _deadMinionIds)
        {
            var minion = _minions[id];
            if (minion.Kind == MinionKind.Prism)
                _prismSpawnCooldown = Math.Max(_prismSpawnCooldown, DefenseRules.PrismRespawnDelay);
            var body = minion.Body;
            RememberEffectSource(id, minion.Kind, body.Equipment.Power);
            _statistics.Retire(id);
            _minions.Remove(id); _combatants.Remove(id); _orderedPlayers.Remove(body); _routes.Remove(id);
        }
        _deadMinionIds.Clear();
        CleanupEffectSources();
    }

    private void CheckSurvivalEnd()
    {
        if (MatchEnded) return;
        Wizard? firstHuman = null;
        foreach (var player in _players.Values)
        {
            if (player.Bot) continue;
            firstHuman ??= player;
            if (player.Deaths < DefenseRules.PlayerLives) return;
        }
        if (firstHuman == null) return;
        MatchEnded = true; Winner = 1; RestartIn = 0;
        foreach (var body in _combatants.Values) CancelCast(body);
        Emit("defense_end", firstHuman.Position, [], "No quedan vidas");
    }

    public bool RestartDefense()
    {
        if (!IsDefense || !MatchEnded) return false;
        ResetMatch(); return true;
    }

    private void ResetDefense()
    {
        if (!IsDefense) return;
        // Los efectos transitorios del terreno anterior no pertenecen al mapa recién generado.
        _beams.Clear(); _events.Clear();
        if (Map.SurvivalMap is { } previous)
        {
            Map = ArenaMap.Get(ArenaMapId.Bastion, SurvivalMapSpec.New(previous));
            Map.PrepareNavigation(); _routes.Clear(); _survivalCells = null;
        }
        foreach (var minion in _minions.Values) { _combatants.Remove(minion.Body.Id); _orderedPlayers.Remove(minion.Body); }
        _minions.Clear(); _lingeringSources.Clear(); _effectOwners.Clear();
        _defenseDrops.Clear(); _defenseAccounts.Clear(); _defenseBestiary.Clear();
        _defenseBotPlans.Clear(); _defenseBotPlanClock = 0;
        ClearDefensePlanningScratch(); _deadMinionIds.Clear();
        _defenseKills = _bossesKilled = 0; _defenseDifficulty = _defensePartySize = 1;
        _defenseElapsed = _spawnClock = _auraClock = _oracleSpawnCooldown = 0;
        _colossusMomentumRemaining = 0;
        _prismSpawnCooldown = DefenseRules.PrismMinimumSpawnGap;
        _commanderSurgeTime = _commanderSurgeCooldown = _commanderSpawnClock = 0;
        _commanderReinforcements = 0;
        _simulationBudget = new(DefenseRules.MaxMinions); _simulationCost = 0; _simulationSamples = 0;
        _bossClock = DefenseRules.BossInterval(_random, true); _survivalFlows.Clear();
        foreach (var player in _players.Values)
        {
            var previousProgress = _defenders[player.Id];
            _defenders[player.Id] = new() { EquipmentSetup = previousProgress.EquipmentSetup.Copy(), EquipmentSetupInitialized = previousProgress.EquipmentSetupInitialized };
            RecalculateDefender(player);
        }
    }
}
