namespace Arcana.Core;

public sealed partial class GameWorld
{
    public bool IsDefense => Options.Mode == GameMode.Defense;
    private readonly Dictionary<int, Wizard> _combatants = [];
    private readonly Dictionary<int, DefenseMinion> _minions = [];
    private readonly Dictionary<int, DefenseProgress> _defenders = [];
    private readonly Dictionary<string, DefenseProgress> _defenseAccounts = new(StringComparer.Ordinal);
    private readonly List<DefenseDrop> _defenseDrops = [];
    private readonly List<int> _deadMinionIds = [];
    private int _nextMinion = 1000, _defenseKills, _bossesKilled, _defenseDifficulty = 1;
    private double _defenseElapsed;
    private float _spawnClock, _auraClock, _bossClock, _oracleSpawnCooldown;
    private float _prismSpawnCooldown = DefenseRules.PrismMinimumSpawnGap;
    private int _defensePartySize = 1;

    public bool DefensePaused
    {
        get
        {
            if (!IsDefense || MatchEnded) return false;
            Wizard? human = null;
            foreach (var player in _players.Values)
            {
                if (player.Bot) continue;
                if (human != null) return false;
                human = player;
            }
            return human != null && _defenders[human.Id].ShopOpen;
        }
    }
    private float DefenseDamagePower(int id)
    {
        if (!IsDefense || !_defenders.TryGetValue(id, out var progress)) return 1;
        float berserker = progress.BerserkerRemaining > 0 ? DefenseRules.BerserkerPower(progress.Passives[(int)DefensePassive.Berserker]) : 1;
        return berserker * (_colossusMomentumRemaining > 0 ? DefenseRules.ColossusMomentumPower : 1);
    }
    private float DefenseSpeed(Wizard player) => IsDefense && _defenders.TryGetValue(player.Id, out var s) && s.BerserkerRemaining > 0 ? 1.15f : 1;

    private void InitializeDefense()
    {
        if (IsDefense) _bossClock = DefenseRules.BossInterval(_random, true);
    }

    private void AddDefender(Wizard player)
    {
        if (!IsDefense) return;
        player.Team = 0;
        player.Position = Map.Spawn(0, player.Id % 4);
        _defenders[player.Id] = new();
        RecalculateDefender(player);
    }

    /// <summary>La identidad solo la proporciona el servidor después de verificar Google. Nunca viaja en un comando de combate.</summary>
    public bool AttachDefenseAccount(int playerId, string uid)
    {
        if (!IsDefense || string.IsNullOrWhiteSpace(uid) || !_players.TryGetValue(playerId, out var player)) return false;
        bool restored = false;
        if (_defenseAccounts.TryGetValue(uid, out var previous))
        {
            _defenders[playerId] = previous;
            player.Kills = previous.Kills; player.Deaths = previous.Deaths;
            Array.Copy(previous.Buffs, player.BuffStacks, RelicBuffs.Count);
            RecalculateDefender(player);
            if (previous.CombatState is { } combat && combat.Id != player.Id)
            {
                RestoreDefenseCombat(player, combat); restored = true;
            }
        }
        else _defenseAccounts[uid] = _defenders[playerId];
        _defenders[playerId].CombatState = player;
        _defenders[playerId].ShopOpen = false;
        _defenders[playerId].Performance = new();
        _defenders[playerId].LastPerformanceReport = -100;
        CheckSurvivalEnd();
        return restored;
    }

    private void RestoreDefenseCombat(Wizard player, Wizard previous)
    {
        // Una salida voluntaria conserva los recursos igual que una reconexión de transporte.
        player.Position = Walkable(previous.Position) ? previous.Position : Map.Spawn(0, player.Id % 4);
        player.Facing = previous.Facing;
        player.Health = player.Deaths >= DefenseRules.PlayerLives ? 0 : Math.Min(previous.Health, player.MaxHealth);
        player.Energy = previous.Energy; player.Respawn = previous.Respawn;
        player.Shield = previous.Shield; player.ShieldMax = previous.ShieldMax;
        player.ShieldTime = previous.ShieldTime; player.ShieldCharges = previous.ShieldCharges;
        player.RegenerationRate = previous.RegenerationRate;
        player.Ward = previous.Ward.ToArray(); player.WardTime = previous.WardTime;
        player.Wet = previous.Wet; player.Burning = previous.Burning;
        player.BurnOwner = previous.BurnOwner == previous.Id ? player.Id : previous.BurnOwner;
        player.BurnMultiplier = previous.BurnMultiplier; player.HitEffectCooldown = previous.HitEffectCooldown;
        player.Chill = previous.Chill; player.Frozen = previous.Frozen; player.Haste = previous.Haste;
        player.StunTime = previous.StunTime; player.WaterSlowTime = previous.WaterSlowTime; player.WaterSlowRank = previous.WaterSlowRank;
        player.FreezeRecovery = previous.FreezeRecovery;
        player.Invulnerable = previous.Invulnerable; player.ActionCooldown = previous.ActionCooldown;
        player.RingCooldown = previous.RingCooldown;
        player.ReprisalCooldown = previous.ReprisalCooldown;
        player.AutomaticRockKillCredits = previous.AutomaticRockKillCredits;
        player.AutomaticRockElementIndex = previous.AutomaticRockElementIndex;
        CheckAutomaticRockEquipment(player);
        player.TeleportCooldown = previous.TeleportCooldown; player.TeleportNeedsExit = previous.TeleportNeedsExit;
        player.Knockback = previous.Knockback;
        // Los efectos del identificador anterior (p. ej. una quemadura) siguen
        // sumando al mismo contador personal tras una reconexión.
        player.MatchDamage = previous.MatchDamage;
        player.ManualSelectionSeconds = previous.ManualSelectionSeconds;
        player.AutomaticSelectionSeconds = previous.AutomaticSelectionSeconds;
        player.UnreportedSelectionSeconds = previous.UnreportedSelectionSeconds;
        _statistics.ResumeParticipant(previous.Id, player);
        Array.Copy(previous.MagickCooldowns, player.MagickCooldowns, player.MagickCooldowns.Length);
    }

    public double DefenseActiveSeconds(int playerId) => _defenders.TryGetValue(playerId, out var p) ? p.ActiveTicks / 60.0 : 0;
    private int DefensePerformanceLimit
    {
        get
        {
            int limit = _simulationBudget.Limit; bool found = false;
            foreach (var p in _players.Values)
                if (!p.Bot) { found = true; if (!_statistics.Network) limit = Math.Min(limit, _defenders[p.Id].Performance.Limit); }
            return found ? limit : Math.Min(limit, DefensePerformanceBudget.DefaultLimit);
        }
    }
    private int DefenseMinionLimit => Math.Min(DefensePerformanceLimit,
        _commanderSurgeTime > 0 ? DefenseRules.SurgeLimit(_defenseDifficulty, _defensePartySize)
                                : DefenseRules.SpawnLimit(_defenseDifficulty, _defensePartySize));

    private void TickDefense(float dt)
    {
        if (!IsDefense || !_players.Values.Any(p => !p.Bot)) return;
        _defenseElapsed += dt;
        _specialSpawnCooldown = Math.Max(0, _specialSpawnCooldown - dt);
        _colossusMomentumRemaining = Math.Max(0, _colossusMomentumRemaining - dt);
        _oracleSpawnCooldown = Math.Max(0, _oracleSpawnCooldown - dt);
        _prismSpawnCooldown = Math.Max(0, _prismSpawnCooldown - dt);
        UpdateDefensePartyScaling();
        foreach (var player in _players.Values)
        {
            var progress = _defenders[player.Id];
            if (player.Deaths < DefenseRules.PlayerLives && !progress.ShopOpen && (player.Bot || Time - progress.LastActivity < 8 || player.Channel != null || player.Charging != null)) progress.ActiveTicks++;
        }
        if (!_tutorialFirstWave) TickDefenseSpawns(dt);
        BuildDefenseCells();
        foreach (var minion in _minions.Values) TickMinion(minion, dt);
        TickDefensePassives(dt);
        TickDefenseDrops(dt);
        _auraClock += dt;
        if (_auraClock >= 0.25f) { TickDefenseAuras(_auraClock); _auraClock = 0; }
    }

    private bool DefenseDeath(Wizard target, int owner)
    {
        if (!IsDefense) return false;
        _statistics.Death(target, owner);
        if (target.Role == CombatantRole.Player)
        {
            target.Deaths = Math.Min(DefenseRules.PlayerLives, target.Deaths + 1);
            _defenders[target.Id].Deaths = target.Deaths;
            target.Respawn = target.Deaths < DefenseRules.PlayerLives ? 4 + target.Deaths * 0.5f : 0;
            CancelCast(target); target.Selection.Clear();
            target.Shield = target.ShieldMax = target.ShieldTime = target.RegenerationRate = 0;
            Emit("death", target.Position, [], target.Deaths < DefenseRules.PlayerLives ? "Te queda una vida" : target.Name + " ha agotado sus vidas");
            CheckSurvivalEnd();
            return true;
        }
        if (target.Role != CombatantRole.Minion) return false;
        if (_minions[target.Id].Kind == MinionKind.Prism)
            _prismSpawnCooldown = Math.Max(_prismSpawnCooldown, DefenseRules.PrismRespawnDelay);
        var special = _minions[target.Id].SpecialAbility != DefenseSpecialAbility.None;
        if (special) _specialSpawnCooldown = Math.Max(_specialSpawnCooldown, DefenseRules.SpecialDeathGap);
        CancelCast(target); _defenseKills++;
        if (_minions[target.Id].Kind == MinionKind.Oracle)
            foreach (var field in _fields)
                if (field.Owner == target.Id && field.Kind == "oracle_bombardment") field.Lifetime = 0;
        bool boss = _minions[target.Id].Kind == MinionKind.Boss;
        if (boss)
        {
            _bossesKilled++;
            CancelColossusFissures(target.Id);
            _colossusMomentumRemaining = DefenseRules.ColossusMomentumDuration;
        }
        _defenseBotPlanClock = 0;
        var discovery = _defenseBestiary[_minions[target.Id].Kind];
        _defenseBestiary[discovery.Kind] = discovery with { Killed = discovery.Killed + 1 };
        if (_players.TryGetValue(owner, out var killer))
        {
            killer.Kills++; var progress = _defenders[killer.Id]; progress.Kills = killer.Kills;
            string kind = BestiaryCatalogue.Key(_minions[target.Id].Kind);
            progress.BestiaryKills[kind] = progress.BestiaryKills.GetValueOrDefault(kind) + 1;
        }
        foreach (var defender in _players.Values)
        {
            var progress = _defenders[defender.Id];
            if (defender.Deaths >= DefenseRules.PlayerLives) continue;
            progress.Experience += (boss ? 60 : 10) * (special ? DefenseRules.SpecialExperienceMultiplier : 1);
            bool levelled = false;
            while (progress.Level < 100 && progress.Experience >= DefenseRules.ExperienceForLevel(progress.Level))
            {
                progress.Experience -= DefenseRules.ExperienceForLevel(progress.Level); progress.Level++; levelled = true;
                StatisticsEconomy(defender.Id, StatisticMeasure.Levels, 1);
            }
            if (progress.Level == 100) progress.Experience = Math.Min(progress.Experience, DefenseRules.ExperienceForLevel(100) - 1);
            if (levelled) RecalculateDefender(defender);
            int lootMultiplier = special ? DefenseRules.SpecialLootMultiplier : 1;
            if (_random.Next(100) < DefenseRules.EquipmentDropChance * lootMultiplier) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Equipment);
            if (_random.Next(10000) < DefenseRules.RelicDropChance * 100 * lootMultiplier) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Relic);
        }
        if (special)
        {
            AddDefenseDrop(target.Position, 0, DefenseDropKind.Gold);
            _defenseDrops[^1].Gold *= DefenseRules.SpecialGoldMultiplier;
            _defenseDrops[^1].SpecialReward = true;
        }
        else if (_random.Next(100) < DefenseRules.GoldDropChance) AddDefenseDrop(target.Position, 0, DefenseDropKind.Gold);
        return true;
    }

}
