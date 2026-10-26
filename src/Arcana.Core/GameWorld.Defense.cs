namespace Arcana.Core;

public sealed partial class GameWorld
{
    public bool IsDefense => Options.Mode == GameMode.Defense;
    private readonly Dictionary<int, Wizard> _combatants = [];
    private readonly Dictionary<int, DefenseMinion> _minions = [];
    private readonly Dictionary<int, DefenseProgress> _defenders = [];
    private readonly Dictionary<string, DefenseProgress> _defenseAccounts = new(StringComparer.Ordinal);
    private readonly List<DefenseDrop> _defenseDrops = [];
    private int _nextMinion = 1000, _defenseKills, _bossesKilled, _defenseDifficulty = 1;
    private double _defenseElapsed;
    private float _spawnClock, _auraClock, _bossClock;
    private int _defensePartySize = 1;

    public bool DefensePaused => IsDefense && !MatchEnded && _players.Values.Count(p => !p.Bot) == 1 &&
        _players.Values.Any(p => !p.Bot && _defenders[p.Id].ShopOpen);
    private float DefenseDamagePower(int id) => IsDefense && _defenders.TryGetValue(id, out var s) && s.BerserkerRemaining > 0
        ? DefenseRules.BerserkerPower(s.Passives[(int)DefensePassive.Berserker]) : 1;
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
        player.FreezeRecovery = previous.FreezeRecovery;
        player.Invulnerable = previous.Invulnerable; player.ActionCooldown = previous.ActionCooldown;
        player.RingCooldown = previous.RingCooldown;
        player.ReprisalCooldown = previous.ReprisalCooldown;
        player.TeleportCooldown = previous.TeleportCooldown; player.TeleportNeedsExit = previous.TeleportNeedsExit;
        player.Knockback = previous.Knockback;
        Array.Copy(previous.MagickCooldowns, player.MagickCooldowns, player.MagickCooldowns.Length);
    }

    public double DefenseActiveSeconds(int playerId) => _defenders.TryGetValue(playerId, out var p) ? p.ActiveTicks / 60.0 : 0;
    private int DefensePerformanceLimit
    {
        get
        {
            int limit = _simulationBudget.Limit; bool found = false;
            foreach (var p in _players.Values)
                if (!p.Bot) { found = true; limit = Math.Min(limit, _defenders[p.Id].Performance.Limit); }
            return found ? limit : Math.Min(limit, DefensePerformanceBudget.DefaultLimit);
        }
    }
    private int DefenseMinionLimit => Math.Min(DefensePerformanceLimit,
        _commanderSurgeTime > 0 ? DefenseRules.SurgeLimit(_defenseDifficulty, _defensePartySize)
                                : DefenseRules.SpawnLimit(_defenseDifficulty, _defensePartySize));

    private void TrimDistantDefenseMinions()
    {
        int excess = _minions.Count - DefenseMinionLimit;
        if (excess <= 0) return;
        // Nunca desaparecen enemigos próximos a un aliado ni se conceden premios por retirarlos.
        var distant = _minions.Values.Where(m => m.Kind != MinionKind.Boss && m.Body.Alive &&
            _players.Values.All(p => p.Deaths >= DefenseRules.PlayerLives || V2.Distance(p.Position, m.Body.Position) > 60))
            .OrderByDescending(m => _players.Values.Min(p => V2.Distance(p.Position, m.Body.Position))).Take(excess).ToArray();
        foreach (var m in distant)
        {
            CancelCast(m.Body);
            _statistics.Retire(m.Body.Id);
            _minions.Remove(m.Body.Id); _combatants.Remove(m.Body.Id); _orderedPlayers.Remove(m.Body); _routes.Remove(m.Body.Id);
            _projectiles.RemoveAll(p => p.Owner == m.Body.Id); _fields.RemoveAll(f => f.Owner == m.Body.Id);
        }
    }

    private void TickDefense(float dt)
    {
        if (!IsDefense || !_players.Values.Any(p => !p.Bot)) return;
        _defenseElapsed += dt;
        _defensePartySize = Math.Clamp(_players.Count, 1, DefenseRules.MaxDefenders);
        foreach (var player in _players.Values)
        {
            var progress = _defenders[player.Id];
            if (player.Deaths < DefenseRules.PlayerLives && !progress.ShopOpen && (player.Bot || Time - progress.LastActivity < 8 || player.Channel != null || player.Charging != null)) progress.ActiveTicks++;
        }
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
        BuildDefenseCells();
        foreach (var minion in _minions.Values) TickMinion(minion, dt);
        TickDefensePassives(dt);
        TickDefenseDrops(dt);
        _auraClock += dt;
        if (_auraClock >= 0.25f) { TickDefenseAuras(_auraClock); _auraClock = 0; }
    }

    private void SpawnMinion()
    {
        MinionKind kind = MinionKind.Melee;
        int roll = _random.Next(100);
        int cursor = 0;
        bool Pick(int firstStage, int chance) { if (_defenseDifficulty < firstStage) return false; cursor += chance; return roll < cursor; }
        if (Pick(5, 4)) kind = MinionKind.Captain;
        else if (Pick(6, 5)) kind = MinionKind.Sentinel;
        else if (Pick(6, 3)) kind = MinionKind.Oracle;
        else if (Pick(3, 8)) kind = MinionKind.Prism;
        else if (Pick(5, 6)) kind = MinionKind.Warlock;
        else if (Pick(4, 6)) kind = MinionKind.Cryomancer;
        else if (Pick(3, 5)) kind = MinionKind.Shaman;
        else if (Pick(4, 6)) kind = MinionKind.Brute;
        else if (Pick(5, 8)) kind = MinionKind.Tank;
        else if (Pick(4, 8)) kind = MinionKind.Mage;
        else if (Pick(4, 8)) kind = MinionKind.Bomber;
        else if (Pick(4, 14)) kind = MinionKind.Archer;
        else if (Pick(2, 12)) kind = MinionKind.Runner;
        if (kind == MinionKind.Oracle && _minions.Values.Count(m => m.Kind == kind && m.Body.Alive) >= 2) kind = MinionKind.Archer;
        if (kind == MinionKind.Prism && _minions.Values.Count(m => m.Kind == kind && m.Body.Alive) >= 8) kind = MinionKind.Runner;
        TrySpawnMinion(kind);
    }

    private bool TrySpawnMinion(MinionKind kind)
    {
        if (_minions.Count >= DefenseRules.MaxMinions || !TryDefenseSpawn(out var at)) return false;
        float strength = DefenseRules.EnemyStrength(_defenseDifficulty);
        float health = DefenseRules.MinionHealth(kind, _defenseDifficulty, _defensePartySize);
        var body = new Wizard { Id = _nextMinion++, Name = DefenseRules.MinionName(kind), Team = 1, Bot = true,
            Role = CombatantRole.Minion, WizardType = DefenseRules.EnemyWizardType(kind),
            Position = at, Health = health, MaxHealth = health, Invulnerable = 0,
            VulnerableElement = kind == MinionKind.Prism ? DefenseRules.PrismElements[_random.Next(DefenseRules.PrismElements.Length)] : null,
            Equipment = EquipmentModifiers.None with { Speed = DefenseRules.MinionSpeed(kind, _defenseDifficulty),
                Power = strength * DefenseRules.EnemySpellPower(kind) } };
        var minion = new DefenseMinion(body, kind, strength) { SpeedClock = 7 + (float)_random.NextDouble() * 12, SupportCooldown = kind == MinionKind.Oracle ? 0.1f : 3 };
        _statistics.Bind(body, kind);
        _statistics.Add(StatisticGroup.Difficulty, _defenseDifficulty, StatisticMeasure.Spawns, 1);
        _statistics.Add(StatisticGroup.Difficulty, _defenseDifficulty, StatisticMeasure.SpawnHealth, health);
        _minions.Add(body.Id, minion);
        _combatants.Add(body.Id, body); _orderedPlayers.Add(body);
        DiscoverDefenseEnemy(minion);
        if (kind is MinionKind.Boss or MinionKind.Captain) StartCommanderSurge(kind == MinionKind.Boss);
        if (kind == MinionKind.Boss) Emit("defense_boss", at, [], "Un coloso te está buscando");
        return true;
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
        var distant = _minions.Values.Where(m => m.Kind != MinionKind.Boss && m.Body.Alive &&
            defenders.All(p => V2.Distance(p.Position, m.Body.Position) > 60))
            .OrderByDescending(m => defenders.Min(p => V2.Distance(p.Position, m.Body.Position))).FirstOrDefault();
        if (distant == null || !TryDefenseSpawn(out var at, nearbyOnly: true)) return;
        CancelCast(distant.Body); distant.Body.Position = at; distant.Body.Knockback = V2.Zero;
        distant.Navigation = at; distant.NavigationClock = 0;
        _routes.Remove(distant.Body.Id);
    }

    private bool Empowered(DefenseMinion minion)
    {
        foreach (var other in _minions.Values)
            if (other.Kind is MinionKind.Captain or MinionKind.Boss && other.Body.Alive &&
                other.Body.Id != minion.Body.Id && V2.Distance(other.Body.Position, minion.Body.Position) <
                (other.Kind == MinionKind.Boss ? 12 : 8)) return true;
        return false;
    }

    private void TickMinion(DefenseMinion minion, float dt)
    {
        var p = minion.Body;
        if (!p.Alive || MatchEnded) return;
        p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
        p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
        TickStatus(p, dt);
        minion.AuraRepairCooldown = Math.Max(0, minion.AuraRepairCooldown - dt);
        if (!p.Alive) return;
        minion.SpeedBoost = Math.Max(0, minion.SpeedBoost - dt);
        minion.SpeedClock -= dt;
        if (minion.SpeedClock <= 0)
        {
            minion.SpeedClock = 8 + (float)_random.NextDouble() * 14;
            if (minion.Kind != MinionKind.Boss && _random.Next(100) < 35) minion.SpeedBoost = 2 + (float)_random.NextDouble() * 2;
        }
        float power = minion.Strength * DefenseRules.EnemySpellPower(minion.Kind)
            * (Empowered(minion) ? 1.35f : 1);
        float speed = DefenseRules.MinionSpeed(minion.Kind, _defenseDifficulty) * (minion.SpeedBoost > 0 ? DefenseRules.SpeedBoostMultiplier : 1);
        if (p.Equipment.Power != power || p.Equipment.Speed != speed)
            p.Equipment = p.Equipment with { Power = power, Speed = speed };
        if (minion.Kind == MinionKind.Shaman) TickDefenseShaman(minion, dt);
        if (minion.Kind == MinionKind.Oracle) TickOracleShield(minion, dt);
        UpdateDefenseEnemyDefense(minion, dt);
        if (minion.Windup > 0)
        {
            p.Input.MoveX = p.Input.MoveY = 0;
            if (p.Frozen > 0) { minion.Windup = 0; minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty); return; }
            minion.Windup = Math.Max(0, minion.Windup - dt);
            if (minion.Windup <= 0) ResolveDefenseAreaAttack(minion);
            return;
        }
        Wizard? target = DefenseTarget(minion, dt);
        if (target == null) { p.Input.MoveX = p.Input.MoveY = 0; return; }
        V2 goal = target.Position;
        float range = DefenseRules.MinionRange(minion.Kind);
        float distanceToTarget = V2.Distance(p.Position, target.Position);
        bool canAttack = distanceToTarget <= range && Visible(p.Position, target.Position, p.Id);
        minion.NavigationClock -= dt;
        if (!canAttack && minion.NavigationClock <= 0)
        {
            minion.Navigation = SurvivalDirection(p, target);
            minion.NavigationClock = 0.16f + p.Id % 4 * 0.015f;
        }
        var direction = canAttack ? V2.Zero : minion.Navigation;
        // Casters keep distance and strafe instead of running straight at the mage.
        if (minion.Kind == MinionKind.Oracle && distanceToTarget < DefenseRules.OracleRetreatRange)
            direction = OracleRetreat(p, target);
        else if (RangedDefenseEnemy(minion.Kind) && canAttack)
        {
            var away = (p.Position - target.Position).Normalized;
            var flank = away.Perpendicular * (p.Id % 2 == 0 ? 0.52f : -0.52f);
            var tactical = distanceToTarget < 4.5f ? away + flank
                : distanceToTarget < (minion.Kind == MinionKind.Oracle ? 21 : 8.5f) ? flank : V2.Zero;
            if (tactical.LengthSquared > 0.01f && Walkable(p.Position + tactical.Normalized * 1.2f))
                direction = tactical.Normalized;
        }
        V2 separation = DefenseSeparation(minion);
        p.Input.MoveX = direction.X + separation.X * 1.5f;
        p.Input.MoveY = direction.Y + separation.Y * 1.5f;
        p.Input.AimX = target.Position.X; p.Input.AimY = target.Position.Y;
        p.Facing = (target.Position - p.Position).Normalized;
        var oldCell = DefenseCell(p.Position);
        Move(p, dt);
        var newCell = DefenseCell(p.Position);
        if (oldCell != newCell) { DefenseCellList(oldCell).Remove(minion); DefenseCellList(newCell).Add(minion); }
        minion.Attack -= dt;
        if (p.Channel != null)
        {
            minion.ChannelTime -= dt;
            if (minion.ChannelTime <= 0 || p.Frozen > 0) { CancelCast(p); minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty); }
            return;
        }
        if (minion.Attack > 0 || p.Frozen > 0) return;
        // Los muros de los defensores se pueden romper: nunca bloquean una ruta indefinidamente.
        var blockingWall = _walls.FirstOrDefault(w => w.Health > 0 && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 2.2f);
        if (!canAttack && blockingWall != null)
        {
            float before = blockingWall.Health;
            blockingWall.Health -= 90 * minion.Strength;
            _statistics.BarrierDamage(p.Id, blockingWall.Owner, Math.Min(Math.Max(0, before), before - blockingWall.Health), null, StatisticAttack.EnemyMelee);
            minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
            return;
        }
        if (!canAttack) return;
        minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
        if (DefenseRules.AreaAttackRadius(minion.Kind) > 0)
        {
            minion.Windup = DefenseRules.AttackWindup(minion.Kind);
            if (minion.Kind is MinionKind.Captain or MinionKind.Sentinel)
                minion.Attack = Math.Max(2.8f, DefenseRules.AttackPause(minion.Kind, _defenseDifficulty) * 2.7f);
        }
        else if (minion.Kind == MinionKind.Oracle) CastOracleBombardment(minion, target);
        else if (RangedDefenseEnemy(minion.Kind) && minion.Kind != MinionKind.Archer)
            CastDefenseEnemySpell(minion, distanceToTarget);
        else if (minion.Kind == MinionKind.Archer)
        {
            if (_projectiles.Count >= 160) return;
            var recipe = new SpellRecipe([Element.Earth]);
            _statistics.Cast(p.Id, StatisticAttack.Rock, recipe.Elements);
            _projectiles.Add(new Projectile { Id = _nextEntity++, Owner = p.Id, Position = SpellOrigin(p, p.Facing, 0.75f),
                Velocity = p.Facing * 14, Recipe = recipe, Radius = 0.14f, Power = DefenseRules.ArrowPower, Lifetime = 1.3f });
            Emit("cast", p.Position, [Element.Earth], "");
        }
        else { _statistics.Cast(p.Id, StatisticAttack.EnemyMelee); DamageAs(StatisticAttack.EnemyMelee, target, DefenseRules.MinionMeleeAttack(minion.Kind), p.Id); }
    }

    private void TickDefenseShaman(DefenseMinion minion, float dt)
    {
        minion.SupportCooldown -= dt;
        if (minion.SupportCooldown > 0 || minion.Body.Frozen > 0) return;
        minion.SupportCooldown = DefenseRules.ShamanHealInterval;
        Wizard? wounded = null;
        float mostMissing = 0;
        foreach (var ally in _minions.Values)
        {
            var body = ally.Body;
            if (!body.Alive || body.Id == minion.Body.Id || V2.Distance(body.Position, minion.Body.Position) > DefenseRules.ShamanHealRadius ||
                !Visible(body.Position, minion.Body.Position, minion.Body.Id)) continue;
            float missing = 1 - body.Health / body.MaxHealth;
            if (missing > mostMissing) { mostMissing = missing; wounded = body; }
        }
        if (wounded == null) return;
        float before = wounded.Health;
        _statistics.Cast(minion.Body.Id, StatisticAttack.Regeneration);
        wounded.Health = Math.Min(wounded.MaxHealth, wounded.Health + DefenseRules.ShamanHealAmount * minion.Strength);
        _statistics.Heal(minion.Body.Id, wounded, wounded.Health - before, StatisticAttack.Regeneration);
        Emit("mine_heal", wounded.Position, [Element.Life], "");
    }

    private void ResolveDefenseAreaAttack(DefenseMinion minion)
    {
        _statistics.Cast(minion.Body.Id, StatisticAttack.EnemyArea);
        float radius = DefenseRules.AreaAttackRadius(minion.Kind);
        foreach (var defender in _players.Values)
            if (defender.Alive && V2.Distance(defender.Position, minion.Body.Position) <= radius &&
                Visible(minion.Body.Position, defender.Position, minion.Body.Id))
                DamageAs(StatisticAttack.EnemyArea, defender, DefenseRules.MinionMeleeAttack(minion.Kind), minion.Body.Id);
        Emit("nova", minion.Body.Position,
            [minion.Kind is MinionKind.Bomber or MinionKind.Boss ? Element.Fire :
             minion.Kind == MinionKind.Sentinel ? Element.Shield : Element.Earth], "", radius);
        if (minion.Kind == MinionKind.Bomber)
        {
            minion.Body.Health = 0;
            DefenseDeath(minion.Body, minion.Body.Id);
        }
        minion.Attack = DefenseRules.AttackPause(minion.Kind, _defenseDifficulty);
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
        CancelCast(target); _defenseKills++;
        if (_minions[target.Id].Kind == MinionKind.Oracle)
            foreach (var field in _fields)
                if (field.Owner == target.Id && field.Kind == "oracle_bombardment") field.Lifetime = 0;
        bool boss = _minions[target.Id].Kind == MinionKind.Boss;
        if (boss) _bossesKilled++; _defenseBotPlanClock = 0;
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
            progress.Experience += boss ? 60 : 10;
            bool levelled = false;
            while (progress.Level < 100 && progress.Experience >= DefenseRules.ExperienceForLevel(progress.Level))
            {
                progress.Experience -= DefenseRules.ExperienceForLevel(progress.Level); progress.Level++; levelled = true;
                StatisticsEconomy(defender.Id, StatisticMeasure.Levels, 1);
            }
            if (progress.Level == 100) progress.Experience = Math.Min(progress.Experience, DefenseRules.ExperienceForLevel(100) - 1);
            if (levelled) RecalculateDefender(defender);
            if (_random.Next(100) < DefenseRules.EquipmentDropChance) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Equipment);
            if (_random.Next(10000) < DefenseRules.RelicDropChance * 100) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Relic);
        }
        if (_random.Next(100) < DefenseRules.GoldDropChance) AddDefenseDrop(target.Position, 0, DefenseDropKind.Gold);
        return true;
    }

    private void CleanupDefense()
    {
        if (!IsDefense) return;
        foreach (int id in _minions.Where(m => !m.Value.Body.Alive).Select(m => m.Key).ToArray())
        {
            var body = _minions[id].Body;
            _statistics.Retire(id);
            _minions.Remove(id); _combatants.Remove(id); _orderedPlayers.Remove(body); _routes.Remove(id);
        }
    }

    private void CheckSurvivalEnd()
    {
        var humans = _players.Values.Where(p => !p.Bot).ToArray();
        if (MatchEnded || humans.Length == 0 || humans.Any(p => p.Deaths < DefenseRules.PlayerLives)) return;
        MatchEnded = true; Winner = 1; RestartIn = 0;
        foreach (var body in _combatants.Values) CancelCast(body);
        Emit("defense_end", humans[0].Position, [], "No quedan vidas");
    }

    public bool RestartDefense()
    {
        if (!IsDefense || !MatchEnded) return false;
        ResetMatch(); return true;
    }

    private void ResetDefense()
    {
        if (!IsDefense) return;
        foreach (var minion in _minions.Values) { _combatants.Remove(minion.Body.Id); _orderedPlayers.Remove(minion.Body); }
        _minions.Clear(); _defenseDrops.Clear(); _defenseAccounts.Clear(); _defenseBestiary.Clear();
        _defenseBotPlans.Clear(); _defenseBotPlanClock = 0;
        _defenseKills = _bossesKilled = 0; _defenseDifficulty = _defensePartySize = 1;
        _defenseElapsed = _spawnClock = _auraClock = 0;
        _commanderSurgeTime = _commanderSurgeCooldown = _commanderSpawnClock = 0;
        _commanderReinforcements = 0;
        _simulationBudget = new(DefenseRules.MaxMinions); _simulationCost = 0; _simulationSamples = 0;
        _bossClock = DefenseRules.BossInterval(_random, true); _survivalFlows.Clear();
        foreach (var player in _players.Values) { _defenders[player.Id] = new(); RecalculateDefender(player); }
    }
}
