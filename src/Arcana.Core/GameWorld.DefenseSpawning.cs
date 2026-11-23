namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void TrimDistantDefenseMinions()
    {
        int excess = _minions.Count - DefenseMinionLimit;
        if (excess <= 0) return;
        // Nunca desaparecen enemigos próximos a un aliado ni se conceden premios por retirarlos.
        var distant = _minions.Values.Where(m => m.Kind != MinionKind.Boss && m.Body.Alive &&
            _players.Values.All(p => p.Deaths >= DefenseRules.PlayerLives || V2.Distance(p.Position, m.Body.Position) > 60))
            .OrderByDescending(m => _players.Values.Min(p => V2.Distance(p.Position, m.Body.Position))).Take(excess).ToArray();
        foreach (var m in distant) RetireDefenseMinion(m);
    }

    private void TickDefenseSpawns(float dt)
    {
        _defenseDifficulty = DefenseRules.TimedDifficulty(_defenseElapsed);
        bool bossAlive = _minions.Values.Any(m => m.Kind == MinionKind.Boss && m.Body.Alive);
        if (!bossAlive)
        {
            _bossClock = Math.Max(0, _bossClock - dt);
            if (_bossClock <= 0 && _minions.Count < DefenseMinionLimit && TrySpawnMinion(MinionKind.Boss))
                _bossClock = DefenseRules.BossInterval(_random, false);
        }
        AdvanceCommanderSurge(dt);
        _spawnClock -= dt;
        if (_spawnClock <= 0)
        {
            if (_minions.Count < DefenseMinionLimit - (bossAlive ? 0 : 1)) SpawnMinion();
            else ReturnDistantMinion();
            float interval = DefenseRules.SpawnInterval(_defenseDifficulty, _defensePartySize, _random.NextDouble());
            _spawnClock = Math.Max(DefensePerformanceLimit <= 48 ? 0.4f : DefensePerformanceLimit <= 96 ? 0.25f : 0.18f, interval);
        }
    }

    private void SpawnMinion()
    {
        MinionKind kind = MinionKind.Melee;
        int roll = _random.Next(100);
        int cursor = 0;
        bool Pick(int firstStage, int chance) { if (_defenseDifficulty < firstStage) return false; cursor += chance; return roll < cursor; }
        if (Pick(5, 4)) kind = MinionKind.Captain;
        else if (Pick(6, 5)) kind = MinionKind.Sentinel;
        else if (Pick(6, DefenseRules.OracleSpawnChance)) kind = MinionKind.Oracle;
        else if (Pick(3, DefenseRules.PrismSpawnChance)) kind = MinionKind.Prism;
        else if (Pick(5, 6)) kind = MinionKind.Warlock;
        else if (Pick(4, 6)) kind = MinionKind.Cryomancer;
        else if (Pick(3, 5)) kind = MinionKind.Shaman;
        else if (Pick(4, 6)) kind = MinionKind.Brute;
        else if (Pick(5, 8)) kind = MinionKind.Tank;
        else if (Pick(4, 8)) kind = MinionKind.Mage;
        else if (Pick(4, 8)) kind = MinionKind.Bomber;
        else if (Pick(4, 14)) kind = MinionKind.Archer;
        else if (Pick(2, 12)) kind = MinionKind.Runner;
        if (kind == MinionKind.Oracle && (_oracleSpawnCooldown > 0 ||
            _minions.Values.Count(m => m.Kind == kind && m.Body.Alive) >= DefenseRules.OracleNaturalLimit)) kind = MinionKind.Archer;
        if (kind == MinionKind.Prism && (ActiveDefensePartySize == 0 || _prismSpawnCooldown > 0 ||
            _minions.Values.Count(m => m.Kind == kind && m.Body.Alive) >= DefenseRules.PrismLimit(ActiveDefensePartySize)))
            kind = MinionKind.Runner;
        TrySpawnMinion(kind);
    }

    private bool TrySpawnMinion(MinionKind kind)
    {
        if (_minions.Count >= DefenseRules.MaxMinions || !TryDefenseSpawn(out var at)) return false;
        SpawnDefenseMinion(kind, at);
        return true;
    }

    private DefenseMinion SpawnDefenseMinion(MinionKind kind, V2 at)
    {
        int party = Math.Clamp(ActiveDefensePartySize, 1, DefenseRules.MaxDefenders);
        float strength = DefenseRules.EnemyStrength(_defenseDifficulty) * DefenseRules.PartyDamageMultiplier(party);
        float health = DefenseRules.MinionHealth(kind, _defenseDifficulty, party);
        var body = new Wizard { Id = _nextMinion++, Name = DefenseRules.MinionName(kind), Team = 1, Bot = true,
            Role = CombatantRole.Minion, WizardType = DefenseRules.EnemyWizardType(kind),
            Position = at, Health = health, MaxHealth = health, Invulnerable = 0,
            VulnerableElement = kind == MinionKind.Prism ? DefenseRules.PrismElements[_random.Next(DefenseRules.PrismElements.Length)] : null,
            Equipment = EquipmentModifiers.None with { Speed = DefenseRules.MinionSpeed(kind, _defenseDifficulty),
                Power = strength * DefenseRules.EnemySpellPower(kind) } };
        var minion = new DefenseMinion(body, kind, strength) { PartySize = party,
            SpeedClock = 7 + (float)_random.NextDouble() * 12, SupportCooldown = kind == MinionKind.Oracle ? 0.1f : 3 };
        _statistics.Bind(body, kind);
        _statistics.Add(StatisticGroup.Difficulty, _defenseDifficulty, StatisticMeasure.Spawns, 1);
        _statistics.Add(StatisticGroup.Difficulty, _defenseDifficulty, StatisticMeasure.SpawnHealth, health);
        _minions.Add(body.Id, minion);
        if (kind == MinionKind.Oracle)
            _oracleSpawnCooldown = DefenseRules.OracleMinimumSpawnGap + (float)_random.NextDouble() * DefenseRules.OracleSpawnGapVariation;
        if (kind == MinionKind.Prism)
            _prismSpawnCooldown = DefenseRules.PrismMinimumSpawnGap + (float)_random.NextDouble() * DefenseRules.PrismSpawnGapVariation;
        _combatants.Add(body.Id, body); _orderedPlayers.Add(body);
        DiscoverDefenseEnemy(minion);
        if (kind is MinionKind.Boss or MinionKind.Captain) StartCommanderSurge(kind == MinionKind.Boss);
        if (kind == MinionKind.Boss) Emit("defense_boss", at, [], "Un coloso te está buscando");
        if (kind == MinionKind.Oracle) Emit("defense_oracle", at, [Element.Arcane, Element.Shield], "¡Ha aparecido un oráculo de asedio!");
        return minion;
    }

    private bool TryDefenseSpawn(out V2 position, bool nearbyOnly = false)
    {
        var defenders = _players.Values.Where(p => p.Deaths < DefenseRules.PlayerLives).ToArray();
        var nodes = Map.Navigation.Positions;
        for (int attempt = 0; attempt < 160 && defenders.Length > 0; attempt++)
        {
            V2 candidate;
            if (nearbyOnly || attempt < 120 && _random.NextDouble() < 0.9)
            {
                var anchor = defenders[_random.Next(defenders.Length)].Position;
                float angle = (float)_random.NextDouble() * MathF.Tau;
                float radius = DefenseRules.SafeRadius + 2 + (float)_random.NextDouble() * 10;
                candidate = anchor + new V2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }
            else candidate = nodes[_random.Next(nodes.Length)];
            if (!Map.Walkable(candidate) || defenders.Any(p => V2.Distance(p.Position, candidate) < DefenseRules.SafeRadius) ||
                _minions.Values.Any(m => m.Body.Alive && V2.Distance(m.Body.Position, candidate) < 1.5f)) continue;
            position = candidate; return true;
        }
        position = default; return false;
    }

    private void ReturnDistantMinion()
    {
        // Un cambio de zona no deja toda la población ocupada lejos del combate.
        // Solo regresa un enemigo fuera de vista, conservando salud y estados.
        var defenders = _players.Values.Where(p => p.Deaths < DefenseRules.PlayerLives).ToArray();
        if (defenders.Length == 0) return;
        var distant = _minions.Values.Where(m => m.Kind is not (MinionKind.Boss or MinionKind.Prism) && m.Body.Alive &&
            defenders.All(p => V2.Distance(p.Position, m.Body.Position) > 60))
            .OrderByDescending(m => defenders.Min(p => V2.Distance(p.Position, m.Body.Position))).FirstOrDefault();
        if (distant == null || !TryDefenseSpawn(out var at, nearbyOnly: true)) return;
        CancelCast(distant.Body); distant.Body.Position = at; distant.Body.Knockback = V2.Zero;
        distant.Navigation = at; distant.NavigationClock = 0;
        _routes.Remove(distant.Body.Id);
    }
}
