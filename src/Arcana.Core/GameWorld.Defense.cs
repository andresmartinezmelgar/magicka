namespace Arcana.Core;

public sealed partial class GameWorld
{
    public bool IsDefense => Options.Mode == GameMode.Defense;
    private readonly Dictionary<int, Wizard> _combatants = [];
    private readonly Dictionary<int, DefenseMinion> _minions = [];
    private readonly Dictionary<int, DefenseProgress> _defenders = [];
    private readonly Dictionary<string, DefenseProgress> _defenseAccounts = new(StringComparer.Ordinal);
    private readonly List<DefenseDrop> _defenseDrops = [];
    private Wizard? _castle;
    private int _nextMinion = 1000, _defenseWave, _toSpawn, _repairs, _defenseKills, _defenseDifficulty = 1, _spawnBurstRemaining;
    private double _defenseElapsed;
    private float _wavePause = DefenseRules.WavePause, _spawnClock, _auraClock;
    private int _defensePartySize = 1;

    public bool DefensePaused => IsDefense && !MatchEnded && _players.Values.Count(p => !p.Bot) == 1 &&
        _players.Values.Any(p => !p.Bot && _defenders[p.Id].ShopOpen);
    private float DefenseDamagePower(int id) => IsDefense && _defenders.TryGetValue(id, out var s) && s.BerserkerRemaining > 0
        ? DefenseRules.BerserkerPower(s.Passives[(int)DefensePassive.Berserker]) : 1;
    private float DefenseSpeed(Wizard player) => IsDefense && _defenders.TryGetValue(player.Id, out var s) && s.BerserkerRemaining > 0 ? 1.15f : 1;

    private void InitializeDefense()
    {
        if (!IsDefense) return;
        _castle = new Wizard { Id = int.MaxValue, Name = "Bastión", Team = 0, Role = CombatantRole.Castle,
            Position = DefenseRules.CastlePosition, MaxHealth = DefenseRules.CastleHealth,
            Health = DefenseRules.CastleHealth, Invulnerable = 0 };
        _combatants.Add(_castle.Id, _castle);
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
    public void AttachDefenseAccount(int playerId, string uid)
    {
        if (!IsDefense || string.IsNullOrWhiteSpace(uid) || !_players.TryGetValue(playerId, out var player)) return;
        if (_defenseAccounts.TryGetValue(uid, out var previous))
        {
            _defenders[playerId] = previous;
            player.Kills = previous.Kills; player.Deaths = previous.Deaths;
            Array.Copy(previous.Buffs, player.BuffStacks, RelicBuffs.Count);
            RecalculateDefender(player);
            player.Health = player.MaxHealth;
        }
        else _defenseAccounts[uid] = _defenders[playerId];
        _defenders[playerId].ShopOpen = false;
    }

    public double DefenseActiveSeconds(int playerId) => _defenders.TryGetValue(playerId, out var p) ? p.ActiveTicks / 60.0 : 0;

    private void TickDefense(float dt)
    {
        if (!IsDefense || _castle == null || !_players.Values.Any(p => !p.Bot)) return;
        _defenseElapsed += dt;
        _defensePartySize = Math.Max(_defensePartySize, _players.Count);
        foreach (var player in _players.Values)
        {
            var progress = _defenders[player.Id];
            if (player.Bot || Time - progress.LastActivity < 8 || player.Channel != null || player.Charging != null) progress.ActiveTicks++;
        }
        if (Options.DefenseFlow == DefenseFlow.Continuous)
        {
            _defenseDifficulty = 1 + (int)(_defenseElapsed / 60);
            _defenseWave = _defenseDifficulty;
            _wavePause = 0;
        }
        else if (_toSpawn == 0 && _minions.Count == 0)
        {
            _wavePause = Math.Max(0, _wavePause - dt);
            if (_wavePause <= 0)
            {
                _defenseWave++;
                _defenseDifficulty = Math.Max(_defenseWave, 1 + (int)(_defenseElapsed / 90));
                _spawnBurstRemaining = 0; _spawnClock = 0;
                _toSpawn = (int)Math.Ceiling(DefenseRules.WaveSize(_defenseWave) * (1 + 0.45 * (_defensePartySize - 1)));
                _wavePause = DefenseRules.WavePause;
                Emit("defense_wave", _castle.Position, [], $"Oleada {_defenseWave}: {_toSpawn} enemigos");
            }
        }
        _spawnClock -= dt;
        if (_spawnClock <= 0 && _minions.Count < DefenseRules.MaxMinions &&
            (Options.DefenseFlow == DefenseFlow.Continuous || _toSpawn > 0))
        {
            SpawnMinion();
            if (Options.DefenseFlow == DefenseFlow.Waves) _toSpawn--;
            // Alterna grupos rápidos y descansos irregulares. El ritmo se acelera con la dificultad.
            float pressure = 1 + (_defenseDifficulty - 1) * 0.09f + (_defensePartySize - 1) * 0.18f;
            if (_spawnBurstRemaining <= 0) _spawnBurstRemaining = _random.Next(2, Options.DefenseFlow == DefenseFlow.Waves ? 8 : 6);
            _spawnBurstRemaining--;
            _spawnClock = _spawnBurstRemaining > 0
                ? (0.16f + (float)_random.NextDouble() * 0.3f) / pressure
                : (1.8f + (float)_random.NextDouble() * 3.2f) / pressure;
        }
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
        if (_defenseDifficulty >= 10 && roll < 10) kind = MinionKind.Captain;
        else if (_defenseDifficulty >= 3 && roll < 24) kind = MinionKind.Tank;
        else if (_defenseDifficulty >= 7 && roll < 40) kind = MinionKind.Mage;
        else if (_defenseDifficulty >= 4 && roll < 62) kind = MinionKind.Archer;
        var entrance = DefenseRules.Entrances[_random.Next(DefenseRules.Entrances.Count)];
        V2 at = entrance;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var candidate = _random.Next(100) < 20
                ? new V2((float)_random.NextDouble() * 70 - 35, (float)_random.NextDouble() * 42 - 21)
                : entrance + new V2((float)_random.NextDouble() * 5 - 2.5f, (float)_random.NextDouble() * 5 - 2.5f);
            if (Map.Walkable(candidate) && V2.Distance(candidate, DefenseRules.CastlePosition) >= DefenseRules.SafeRadius &&
                _minions.Values.All(m => V2.Distance(m.Body.Position, candidate) > 0.8f)) { at = candidate; break; }
        }
        float strength = DefenseRules.EnemyStrength(_defenseDifficulty);
        float health = DefenseRules.MinionHealth(kind, _defenseDifficulty, _defensePartySize);
        var body = new Wizard { Id = _nextMinion++, Name = DefenseRules.MinionName(kind), Team = 1, Bot = true,
            Role = CombatantRole.Minion, WizardType = kind == MinionKind.Mage ? WizardType.Pyromancer : WizardType.Balanced,
            Position = at, Health = health, MaxHealth = health, Invulnerable = 0,
            Equipment = EquipmentModifiers.None with { Speed = (0.5f + Math.Min(0.35f, _defenseDifficulty * 0.012f)) * (kind == MinionKind.Tank ? 0.65f : 1),
                Power = strength * (kind == MinionKind.Mage ? 0.22f : 1) } };
        _minions.Add(body.Id, new(body, kind, strength));
        _combatants.Add(body.Id, body); _orderedPlayers.Add(body);
    }

    private bool Empowered(DefenseMinion minion) => _minions.Values.Any(m => m.Kind == MinionKind.Captain &&
        m.Body.Alive && m.Body.Id != minion.Body.Id && V2.Distance(m.Body.Position, minion.Body.Position) < 8);

    private void TickMinion(DefenseMinion minion, float dt)
    {
        var p = minion.Body;
        if (!p.Alive || _castle == null || !_castle.Alive) return;
        p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
        p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
        TickStatus(p, dt);
        if (!p.Alive) return;
        p.Equipment = p.Equipment with { Power = minion.Strength * (minion.Kind == MinionKind.Mage ? 0.22f : 1) * (Empowered(minion) ? 1.35f : 1) };
        Wizard target = _castle;
        float nearest = 5.5f;
        foreach (var defender in _players.Values)
        {
            float distance = V2.Distance(p.Position, defender.Position);
            if (defender.Alive && distance < nearest && V2.Distance(defender.Position, _castle.Position) < 28)
            { target = defender; nearest = distance; }
        }
        V2 goal = target.Position;
        if (target == _castle)
        {
            float angle = (p.Id % 20 - 9.5f) * 0.11f;
            goal += new V2(MathF.Cos(angle), MathF.Sin(angle)) * (DefenseRules.CastleRadius + 0.7f);
        }
        float range = minion.Kind is MinionKind.Archer or MinionKind.Mage ? 9 : target == _castle ? 3.5f : 1.4f;
        float distanceToTarget = V2.Distance(p.Position, target.Position);
        bool canAttack = distanceToTarget <= range && Visible(p.Position, target.Position, p.Id);
        var direction = canAttack ? V2.Zero : NavigationDirection(p, goal, dt);
        V2 separation = V2.Zero;
        foreach (var other in _minions.Values)
        {
            if (other.Body.Id == p.Id || !other.Body.Alive) continue;
            var away = p.Position - other.Body.Position;
            if (away.LengthSquared is > 0.0001f and < 1.2f) separation += away.Normalized * (1.1f - away.Length);
        }
        p.Input.MoveX = direction.X + separation.X * 1.5f;
        p.Input.MoveY = direction.Y + separation.Y * 1.5f;
        p.Input.AimX = target.Position.X; p.Input.AimY = target.Position.Y;
        p.Facing = (target.Position - p.Position).Normalized;
        Move(p, dt);
        minion.Attack -= dt;
        if (p.Channel != null)
        {
            minion.ChannelTime -= dt;
            if (minion.ChannelTime <= 0 || p.Frozen > 0) { CancelCast(p); minion.Attack = DefenseRules.AttackDelay(_defenseDifficulty); }
            return;
        }
        if (minion.Attack > 0 || p.Frozen > 0) return;
        // Los muros de los defensores se pueden romper: nunca bloquean una ruta indefinidamente.
        var blockingWall = _walls.FirstOrDefault(w => w.Health > 0 && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 2.2f);
        if (!canAttack && blockingWall != null)
        {
            blockingWall.Health -= 90 * minion.Strength;
            minion.Attack = DefenseRules.AttackDelay(_defenseDifficulty);
            return;
        }
        if (!canAttack) return;
        minion.Attack = DefenseRules.AttackDelay(_defenseDifficulty);
        if (minion.Kind == MinionKind.Mage)
        {
            p.Selection.Clear(); p.Selection.Add(Element.Arcane); p.Selection.Add(Element.Fire);
            BeginCast(p); minion.ChannelTime = 0.45f;
        }
        else if (minion.Kind == MinionKind.Archer)
        {
            if (_projectiles.Count >= 160) return;
            var recipe = new SpellRecipe([Element.Earth]);
            _projectiles.Add(new Projectile { Id = _nextEntity++, Owner = p.Id, Position = SpellOrigin(p, p.Facing, 0.75f),
                Velocity = p.Facing * 14, Recipe = recipe, Radius = 0.14f, Power = 0.18f, Lifetime = 1.3f });
            Emit("cast", p.Position, [Element.Earth], "");
        }
        else Damage(target, minion.Kind == MinionKind.Tank ? 75 : minion.Kind == MinionKind.Captain ? 45 : 30, p.Id);
    }

    private void ThinkDefenseBot(Wizard p, float dt)
    {
        if (_castle == null) return;
        var target = _minions.Values.Where(m => m.Body.Alive)
            .OrderBy(m => V2.Distance(m.Body.Position, _castle.Position)).FirstOrDefault()?.Body;
        var ally = _players.Values.Where(a => a.Alive && a.Health < a.MaxHealth * 0.55f && V2.Distance(a.Position, p.Position) < 9)
            .OrderBy(a => a.Health / a.MaxHealth).FirstOrDefault();
        var goal = target == null ? _castle.Position + new V2(7, (p.Id % 3 - 1) * 3) :
            V2.Lerp(_castle.Position, target.Position, 0.65f);
        if (V2.Distance(goal, _castle.Position) > 17) goal = _castle.Position + (goal - _castle.Position).Normalized * 17;
        var direction = NavigationDirection(p, goal, dt);
        if (V2.Distance(p.Position, goal) < 1) direction = V2.Zero;
        p.Input.MoveX = direction.X; p.Input.MoveY = direction.Y; p.Input.MoveTo = false;
        p.LastInput = Time; p.BotDecision -= dt;
        if (p.BotDecision > 0 || p.Frozen > 0 || !Ready(p)) return;
        p.BotDecision = 0.8f;
        if (p.Channel != null || p.Charging != null) Release(p);
        p.Selection.Clear();
        if (ally != null)
        {
            p.Selection.Add(Element.Life); p.Selection.Add(Element.Life); p.Selection.Add(Element.Life);
            if (ally == p) SelfCast(p);
            else { p.Input.AimX = ally.Position.X; p.Input.AimY = ally.Position.Y; UpdateFacing(p); BeginCast(p); }
        }
        else if (target != null && V2.Distance(p.Position, target.Position) < 15)
        {
            p.Input.AimX = target.Position.X; p.Input.AimY = target.Position.Y; UpdateFacing(p);
            p.Selection.Add(Element.Fire); p.Selection.Add(Element.Arcane); p.Selection.Add(Element.Arcane); BeginCast(p);
        }
        AutoShopDefenseBot(p);
    }

    private bool DefenseDeath(Wizard target, int owner)
    {
        if (!IsDefense) return false;
        if (target.Role == CombatantRole.Castle)
        {
            MatchEnded = true; Winner = 1; RestartIn = 0;
            foreach (var body in _combatants.Values) CancelCast(body);
            Emit("defense_end", target.Position, [], "El bastión ha caído");
            return true;
        }
        if (target.Role != CombatantRole.Minion) return false;
        CancelCast(target); _defenseKills++;
        if (_players.TryGetValue(owner, out var killer)) { killer.Kills++; _defenders[killer.Id].Kills = killer.Kills; }
        foreach (var defender in _players.Values)
        {
            var progress = _defenders[defender.Id];
            progress.Experience += 10;
            bool levelled = false;
            while (progress.Level < 100 && progress.Experience >= DefenseRules.ExperienceForLevel(progress.Level))
            {
                progress.Experience -= DefenseRules.ExperienceForLevel(progress.Level); progress.Level++; levelled = true;
            }
            if (progress.Level == 100) progress.Experience = Math.Min(progress.Experience, DefenseRules.ExperienceForLevel(100) - 1);
            if (levelled) RecalculateDefender(defender);
            if (_random.Next(100) < 6) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Equipment);
            if (_random.Next(100) < 2) AddDefenseDrop(target.Position, defender.Id, DefenseDropKind.Relic);
        }
        if (_random.Next(100) < 35) AddDefenseDrop(target.Position, 0, DefenseDropKind.Gold);
        return true;
    }

    private void CleanupDefense()
    {
        if (!IsDefense) return;
        foreach (int id in _minions.Where(m => !m.Value.Body.Alive).Select(m => m.Key).ToArray())
        {
            var body = _minions[id].Body;
            _minions.Remove(id); _combatants.Remove(id); _orderedPlayers.Remove(body); _routes.Remove(id);
        }
    }

    public bool RestartDefense()
    {
        if (!IsDefense || !MatchEnded) return false;
        ResetMatch(); return true;
    }

    private void ResetDefense()
    {
        if (!IsDefense || _castle == null) return;
        foreach (var minion in _minions.Values) { _combatants.Remove(minion.Body.Id); _orderedPlayers.Remove(minion.Body); }
        _minions.Clear(); _defenseDrops.Clear(); _defenseAccounts.Clear();
        _defenseWave = _toSpawn = _repairs = _defenseKills = _spawnBurstRemaining = 0; _defenseDifficulty = _defensePartySize = 1;
        _defenseElapsed = _spawnClock = _auraClock = 0; _wavePause = DefenseRules.WavePause;
        _castle.Health = _castle.MaxHealth; _castle.Burning = _castle.Wet = _castle.Chill = _castle.Frozen = 0;
        foreach (var player in _players.Values) { _defenders[player.Id] = new(); RecalculateDefender(player); }
    }
}
