namespace Arcana.Core;

/// <summary>
/// Simulación autoritativa compartida por el servidor y la práctica sin conexión.
/// No depende de Godot, ni de sockets, ni del reloj del equipo del jugador.
/// Una sola hebra debe llamar a sus métodos; el servidor lo garantiza con un bloqueo.
/// </summary>
public sealed partial class GameWorld
{
    public const float StepSeconds = 1f / 60;
    private readonly Dictionary<int, Wizard> _players = [];
    private readonly List<Wizard> _orderedPlayers = [];
    private readonly List<Projectile> _projectiles = [];
    private readonly List<Wall> _walls = [];
    private readonly List<Field> _fields = [];
    private readonly List<BeamSnapshot> _beams = [];
    private readonly List<(long Tick, GameEvent Event)> _events = [];
    private readonly ControlPoint[] _points;
    private readonly Random _random;
    private int _nextPlayer = 1, _nextEntity = 1;
    private long _nextEvent = 1;
    private float _ticketClock;
    private int _holdTicks;
    private readonly List<(Wizard Target, float Distance, int Order)> _channelTargets = [];
    private static readonly Element[][] BotRecipes =
    [
        [Element.Arcane, Element.Arcane, Element.Fire], [Element.Fire, Element.Fire, Element.Fire],
        [Element.Water, Element.Cold, Element.Cold], [Element.Earth, Element.Earth, Element.Fire],
        [Element.Lightning, Element.Lightning, Element.Arcane], [Element.Water, Element.Fire, Element.Fire],
        [Element.Cold, Element.Cold, Element.Cold], [Element.Water, Element.Water, Element.Water]
    ];
    public WorldOptions Options { get; }
    public ArenaDefinition Map { get; }
    public bool HoldToWin => Options.Mode == GameMode.Warfare && Map.HoldSeconds > 0;
    public bool ConquestToWin => Options.Mode == GameMode.Warfare && Map.SequentialCapture;
    public int HoldingTeam { get; private set; } = -1;
    public float HoldSeconds => _holdTicks / 60f;
    public long Tick { get; private set; }
    public double Time => Tick / 60.0;
    public bool MatchEnded { get; private set; }
    public int Winner { get; private set; } = -1;
    public float RestartIn { get; private set; }
    public int[] Scores { get; } = new int[2];
    public IReadOnlyDictionary<int, Wizard> Players => _players;

    public GameWorld(WorldOptions? options = null)
    {
        Options = options ?? new();
        Map = ArenaMap.Get(Options.Map);
        if (!Map.SupportsMode(Options.Mode)) throw new ArgumentException("El modo no es compatible con el mapa.", nameof(options));
        if (!Enum.IsDefined(Options.DefenseFlow) || IsDefense && Options.MaxPlayers is < 1 or > DefenseRules.MaxDefenders)
            throw new ArgumentException("Defensa admite entre uno y cuatro defensores.", nameof(options));
        if (!Map.IsPractice) Map.PrepareNavigation();
        _random = new(Options.Seed);
        _points = Map.ControlPoints.Select((p, i) => new ControlPoint { Id = i, Position = p }).ToArray();
        _pickupRandom = new(Options.PickupSeed ?? Random.Shared.Next());
        _pickups = CreatePickups();
        ResetScores();
        InitializeDefense();
        for (int i = 0; i < Math.Min(Options.Bots, Options.MaxPlayers); i++)
            AddPlayer($"Bot {i + 1}", -1, true, (WizardType)(i % 5));
    }

    public Wizard? AddPlayer(string name, int preferredTeam = -1, bool bot = false, WizardType wizardType = WizardType.Balanced)
    {
        if (!Enum.IsDefined(wizardType) || _players.Count >= Options.MaxPlayers || Options.Mode == GameMode.Duel && _players.Count >= 2) return null;
        int blue = _players.Values.Count(p => p.Team == 0), red = _players.Count - blue;
        int team = Map.IsPractice ? preferredTeam == 1 ? 1 : 0
            : preferredTeam is 0 or 1 ? preferredTeam : blue <= red ? 0 : 1;
        // El segundo jugador humano de un duelo ocupa el equipo libre,
        // incluso si ambos tienen guardada la misma preferencia.
        if (Options.Mode == GameMode.Duel && !bot && _players.Values.FirstOrDefault(p => !p.Bot) is { } opponentHuman)
            team = 1 - opponentHuman.Team;
        if (Options.Mode == GameMode.Duel && _players.Values.FirstOrDefault(p => p.Team == team) is { } opponent)
        {
            if (!opponent.Bot) return null;
            // Al crear un duelo con un bot, el creador también puede elegir su lado.
            opponent.Team = 1 - team;
            opponent.Position = TeamSpawn(opponent.Team, 0); opponent.Facing = new(opponent.Team == 0 ? 1 : -1, 0);
        }
        string cleanName = new((name ?? "Mago").Where(c => !char.IsControl(c) && c != '[' && c != ']').Take(24).ToArray());
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Mago";
        var wizard = new Wizard { Id = IsDefense ? _nextMinion++ : _nextPlayer++, Name = cleanName.Trim(), Team = team, Bot = bot, WizardType = wizardType };
        wizard.Position = Map.IsPractice ? PracticeLayout.Respawn : Options.Mode == GameMode.Arena ? ArenaSpawnPosition(wizard, null)
            : TeamSpawn(team, _players.Values.Count(p => p.Team == team));
        if (ConquestToWin && !Walkable(wizard.Position)) wizard.Position = RespawnPosition(wizard);
        wizard.Facing = new(team == 0 ? 1 : -1, 0);
        wizard.LastInput = Time;
        _players.Add(wizard.Id, wizard);
        _combatants.Add(wizard.Id, wizard);
        AddDefender(wizard);
        _orderedPlayers.Add(wizard);
        Emit("join", wizard.Position, [], $"{wizard.Name} entra en la arena");
        return wizard;
    }

    /// <summary>Solo se permite cambiar de equipo antes de iniciar una sala competitiva.</summary>
    public bool SetPlayerTeam(int id, int team)
    {
        if (team is not (0 or 1) || !_players.TryGetValue(id, out var wizard) || wizard.Bot ||
            Map.IsPractice || IsDefense || MatchEnded) return false;
        if (Options.Mode == GameMode.Duel && _players.Values.Any(p => p.Id != id && p.Team == team)) return false;
        if (wizard.Team == team) return true;
        wizard.Team = team;
        wizard.Position = TeamSpawn(team, _players.Values.Count(p => p.Team == team) - 1);
        wizard.Facing = new(team == 0 ? 1 : -1, 0);
        CancelCast(wizard);
        return true;
    }

    public void RemovePlayer(int id)
    {
        if (!_players.Remove(id, out var wizard)) return;
        _orderedPlayers.Remove(wizard); _combatants.Remove(id); _defenders.Remove(id);
        _defenseDrops.RemoveAll(d => d.OwnerId == id);
        _defenseBotPlans.Remove(id); _defenseBotPlanClock = 0;
        _routes.Remove(id);
        _practiceSpawns.Remove(id); _practiceReadings.Remove(id);
        _projectiles.RemoveAll(p => p.Owner == id);
        _walls.RemoveAll(p => p.Owner == id);
        _fields.RemoveAll(p => p.Owner == id);
        Emit("leave", wizard.Position, [], $"{wizard.Name} sale de la arena");
        if (IsDefense) { _survivalFlows.Remove(id); CheckSurvivalEnd(); }
    }

    public void EquipPlayer(int id, EquipmentModifiers equipment)
    {
        if (!_players.TryGetValue(id, out var player)) throw new ArgumentException("Mago desconocido.");
        if (IsDefense) return;
        player.Equipment = equipment;
        player.PublicEquipment = PublicEquipment.Empty;
    }

    public void EquipPlayer(int id, PreparedEquipment equipment)
    {
        EquipPlayer(id, equipment.Modifiers);
        if (!IsDefense) _players[id].PublicEquipment = equipment.View;
    }

    public bool ApplyMessage(int id, ClientMessage message)
    {
        if (!_players.TryGetValue(id, out var p) || message.Sequence <= p.LastSequence) return false;
        if (message.Type?.StartsWith("defense_", StringComparison.Ordinal) == true)
        {
            p.LastSequence = message.Sequence;
            bool applied = DefenseCommand(p, message);
            if (applied && IsDefense && message.Type != "defense_performance") _defenders[p.Id].LastActivity = Time;
            return applied;
        }
        if (DefensePaused && message.Type is not ("input" or "release")) { p.LastSequence = message.Sequence; return false; }
        if (message.Type == "input")
        {
            if (!float.IsFinite(message.MoveX) || !float.IsFinite(message.MoveY) ||
                !float.IsFinite(message.AimX) || !float.IsFinite(message.AimY) ||
                !float.IsFinite(message.TargetX) || !float.IsFinite(message.TargetY)) return false;
            // No conserva una referencia mutable recibida del exterior.
            p.Input = new ClientMessage { Type = "input", Sequence = message.Sequence, MoveTo = message.MoveTo,
                MoveX = Math.Clamp(message.MoveX, -1, 1), MoveY = Math.Clamp(message.MoveY, -1, 1),
                AimX = Math.Clamp(message.AimX, -Math.Max(100, Map.HalfWidth), Math.Max(100, Map.HalfWidth)),
                AimY = Math.Clamp(message.AimY, -Math.Max(100, Map.HalfHeight), Math.Max(100, Map.HalfHeight)),
                TargetX = Math.Clamp(message.TargetX, -Map.HalfWidth, Map.HalfWidth),
                TargetY = Math.Clamp(message.TargetY, -Map.HalfHeight, Map.HalfHeight) };
            p.LastSequence = message.Sequence;
            p.LastInput = Time;
            if (IsDefense && (Math.Abs(message.MoveX) + Math.Abs(message.MoveY) > 0.1f || message.MoveTo &&
                V2.Distance(p.Position, new(message.TargetX, message.TargetY)) > 0.3f)) _defenders[p.Id].LastActivity = Time;
            return true;
        }
        if (message.Type is not ("element" or "clear" or "cast" or "release" or "self" or "nova" or "melee" or "magick") ||
            message.Type == "element" && !Enum.IsDefined(message.Element) || message.Type == "magick" && message.Slot is < 0 or > 3) return false;
        p.LastSequence = message.Sequence;
        if (!p.Alive || MatchEnded || p.Frozen > 0) return false;
        if (IsDefense && message.Type is "cast" or "melee" or "self" or "nova" or "magick") _defenders[p.Id].LastActivity = Time;
        UpdateFacing(p);
        switch (message.Type)
        {
            case "element": return p.Selection.Add(message.Element);
            case "clear": p.Selection.Clear(); return true;
            case "cast": BeginCast(p); return true;
            case "release": Release(p); return true;
            case "self": SelfCast(p); return true;
            case "nova": Nova(p); return true;
            case "melee": Melee(p); return true;
            case "magick": Magick(p, message.Slot); return true;
            default: return false;
        }
    }

    public void Step()
    {
        if (DefensePaused) return;
        Tick++;
        const float dt = StepSeconds;
        _beams.Clear();
        int expired = 0;
        while (expired < _events.Count && Tick - _events[expired].Tick > 90) expired++;
        if (expired > 0) _events.RemoveRange(0, expired);
        if (MatchEnded)
        {
            if (IsDefense) return;
            RestartIn -= dt;
            if (RestartIn <= 0) ResetMatch();
            return;
        }
        if (IsDefense) PlanDefenseBots(dt);
        foreach (var p in _players.Values)
        {
            p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
            p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
            p.Invulnerable = p.Invulnerable <= dt + 0.00001f ? 0 : p.Invulnerable - dt;
            for (int i = 0; i < 4; i++) p.MagickCooldowns[i] = Math.Max(0, p.MagickCooldowns[i] - dt);
            if (!p.Alive)
            {
                if (IsDefense && p.Deaths >= DefenseRules.PlayerLives) continue;
                p.Respawn -= dt;
                if (p.Respawn <= 0) Respawn(p);
                continue;
            }
            if (p.Bot) { if (IsDefense) ThinkDefenseBot(p, dt); else ThinkBot(p, dt); }
            if (Time - p.LastInput > 0.6)
            {
                CancelCast(p);
                p.Input.MoveTo = false; p.Input.MoveX = p.Input.MoveY = 0;
            }
            TickStatus(p, dt);
            if (!p.Alive) continue;
            UpdateFacing(p);
            Move(p, dt);
            if (p.Charging != null && p.Frozen <= 0)
            {
                p.ChargeTime = Math.Min(SpellBalance.ChargeSeconds, p.ChargeTime + dt * p.Equipment.RockChargeSpeed);
                if (p.ChargeTime >= SpellBalance.ChargeSeconds) Release(p);
            }
        }
        TickDefense(dt);
        TickPickups(dt);
        TickFrontline(dt);
        // Todos se mueven antes de resolver los canales: cruzar rayos no depende del orden de los jugadores.
        TickChannels(dt);
        TickProjectiles(dt);
        TickWallsAndFields(dt);
        if (Options.Mode == GameMode.Warfare && !Map.IsPractice) TickCapture(dt);
        CleanupDefense();
        CheckVictory();
    }

    private void TickStatus(Wizard p, float dt)
    {
        p.Wet = Math.Max(0, p.Wet - dt);
        p.Frozen = Math.Max(0, p.Frozen - dt);
        p.Chill = Math.Max(0, p.Chill - 10 * dt);
        p.Haste = Math.Max(0, p.Haste - dt);
        p.Energy = Math.Min(100, p.Energy + (1.8f + RelicBuffs.Energy(p)) * p.Equipment.EnergyRegeneration * dt);
        p.ShieldTime = Math.Max(0, p.ShieldTime - dt);
        p.WardTime -= dt;
        if (p.ShieldTime <= 0) p.Shield = p.ShieldMax = p.RegenerationRate = 0;
        if (p.Shield <= 0) p.RegenerationRate = 0;
        Heal(p, (p.RegenerationRate * RelicBuffs.Healing(p) * p.Equipment.Healing + RelicBuffs.Regeneration(p)) * dt);
        if (p.WardTime <= 0) p.Ward = [];
        if (p.Burning > 0)
        {
            p.Burning = Math.Max(0, p.Burning - dt);
            Damage(p, SpellBalance.BurnPerSecond * dt * p.BurnMultiplier * WizardTypes.Incoming(p.WizardType, Element.Fire) * Resistance(p, Element.Fire), p.BurnOwner, [Element.Fire]);
        }
    }

    private bool Allied(Wizard a, Wizard b) => a.Id == b.Id || Options.Mode != GameMode.Arena && a.Team == b.Team;
    private bool Hostile(Wizard a, Wizard b) => !Allied(a, b);
    private float Outgoing(int owner, Element element) => _combatants.TryGetValue(owner, out var source)
        ? WizardTypes.Outgoing(source.WizardType, element) * source.Equipment.ElementPower(element) : 1;
    private float ElementDamage(Wizard target, int owner, Element element, float amount) =>
        amount * Outgoing(owner, element) * WizardTypes.Incoming(target.WizardType, element) * Resistance(target, element) *
        (element == Element.Lightning && target.Wet > 0 && _combatants.TryGetValue(owner, out var source) ? source.Equipment.WetLightningPower : 1);

    private void Move(Wizard p, float dt)
    {
        var move = new V2(p.Input.MoveX, p.Input.MoveY);
        if (move.Length < 0.1f && p.Input.MoveTo)
        {
            var goal = new V2(p.Input.TargetX, p.Input.TargetY);
            move = goal - p.Position;
            if (move.Length < 0.18f) { move = V2.Zero; p.Input.MoveTo = false; }
            else if (Map.Id != ArenaMapId.Classic) move = NavigationDirection(p, goal, dt);
            move = move.Normalized;
        }
        else if (p.Bot || move.LengthSquared > 1) move = move.Normalized;
        float speed = 6.2f * RelicBuffs.Speed(p) * p.Equipment.Speed * DefenseSpeed(p) * (p.Haste > 0 ? 1.65f : 1) * (1 - p.Chill * 0.005f);
        if (p.Channel != null) speed *= 0.48f;
        if (p.Charging != null) speed *= 0.35f;
        if (p.Ward.Contains(Element.Earth)) speed *= 0.78f;
        if (p.Frozen > 0) { speed = 0; CancelCast(p); }
        // El joystick conserva su intensidad; flechas diagonales y rutas no superan la velocidad máxima.
        var walking = move * (speed * dt);
        var delta = walking + p.Knockback * dt;
        p.Knockback *= MathF.Max(0, 1 - 8 * dt);
        MoveWithCollisions(p, delta, walking);
    }

    private bool ClearMovement(V2 from, V2 to) => Map.ClearPath(from, to) && ClearWalls(from, to);
    private bool ClearWalls(V2 from, V2 to)
    {
        foreach (var wall in _walls)
            if (wall.Health > 0 && Geometry.SegmentCapsule(from, to, wall.A, wall.B, ArenaMap.WizardRadius + 0.18f) != null) return false;
        return true;
    }

    public bool Walkable(V2 position)
    {
        if (!Map.Walkable(position)) return false;
        foreach (var wall in _walls)
            if (wall.Health > 0 && Geometry.DistanceToSegment(position, wall.A, wall.B) < ArenaMap.WizardRadius + 0.18f) return false;
        return true;
    }

    private bool Ready(Wizard p) => p.Alive && p.Frozen <= 0 && p.ActionCooldown <= 0 && !MatchEnded;
    private static void UpdateFacing(Wizard p)
    {
        var aim = new V2(p.Input.AimX, p.Input.AimY) - p.Position;
        if (aim.Length > 0.05f) p.Facing = aim.Normalized;
    }
    private static void CancelCast(Wizard p)
    { p.Channel = p.Charging = null; p.ChargeTime = 0; p.BeamTicks = 0; p.BotWallTarget = 0; }
    private void BeginCast(Wizard p)
    {
        if (!Ready(p)) return;
        CancelCast(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        if (spell.Shape == SpellShape.Rock)
        {
            p.Charging = spell; p.ChargeTime = 0;
        }
        else if (spell.Shape == SpellShape.Shards)
        {
            int count = 3 + spell.Count(Element.Cold);
            bool launched = false;
            for (int i = 0; i < count; i++) launched |= SpawnProjectile(p, spell,
                p.Facing.Rotated((i - (count - 1) / 2f) * 0.11f), SpellBalance.ShardPower, 22, 0.16f);
            if (launched) Emit("cast", p.Position, spell.Elements, "");
            p.ActionCooldown = 0.5f;
        }
        else if (spell.Shape == SpellShape.Barrier)
        {
            CreateBarrier(p, spell); p.ActionCooldown = 0.7f;
        }
        else
        {
            p.Channel = spell;
            Emit("cast", p.Position, spell.Elements, "");
        }
    }

    private void Release(Wizard p)
    {
        if (p.Charging != null)
        {
            if (SpawnProjectile(p, p.Charging, p.Facing, SpellBalance.RockMinimum + p.ChargeTime, 13 + p.ChargeTime * 9,
                0.24f + p.Charging.Count(Element.Earth) * 0.13f)) Emit("cast", p.Position, p.Charging.Elements, "");
            p.ActionCooldown = 0.35f;
        }
        CancelCast(p);
    }

    private bool SpawnProjectile(Wizard p, SpellRecipe spell, V2 direction, float power, float speed, float radius)
    {
        if (_projectiles.Count >= 128) return false;
        _projectiles.Add(new Projectile
        {
            Id = _nextEntity++, Owner = p.Id, Position = SpellOrigin(p, direction, 0.85f, radius),
            Velocity = direction * speed, Recipe = spell, Power = power, Radius = radius, Lifetime = 2.5f * p.Equipment.Range
        });
        return true;
    }

    private V2 SpellOrigin(Wizard caster, V2 direction, float distance, float radius = 0)
    {
        var to = caster.Position + direction * distance;
        float? nearest = Map.BoundaryHit(caster.Position, to, radius);
        foreach (var obstacle in Map.Obstacles)
            if (Geometry.SegmentCircle(caster.Position, to, obstacle.Position, obstacle.Radius + radius) is float t && (nearest == null || t < nearest))
                nearest = t;
        foreach (var wall in _walls)
            if (wall.Health > 0 && !PassesOwnShield(wall, caster.Id) && Geometry.SegmentCapsule(caster.Position, to, wall.A, wall.B, radius + 0.2f) is float t && (nearest == null || t < nearest))
                nearest = t;
        return nearest is float hit ? V2.Lerp(caster.Position, to, Math.Max(0, hit - 0.01f)) : to;
    }

    private V2 RayEnd(V2 from, V2 direction, float range, out Wall? hitWall, int owner = 0, BeamPart[]? parts = null)
    {
        hitWall = null;
        if (!Map.Inside(from)) return from;
        var to = from + direction * range;
        float best = Map.BoundaryHit(from, to) ?? 1;
        foreach (var o in Map.Obstacles)
            if (Geometry.SegmentCircle(from, to, o.Position, o.Radius) is float t && t < best) best = t;
        // Segmento de pared contra rayo mediante productos cruzados.
        foreach (var w in _walls)
        {
            if (!(w.Health > 0) || PassesOwnShield(w, owner, parts)) continue;
            var r = to - from; var s = w.B - w.A;
            float cross = r.X * s.Y - r.Y * s.X;
            if (MathF.Abs(cross) < 0.0001f) continue;
            var a = w.A - from;
            float t = (a.X * s.Y - a.Y * s.X) / cross;
            float u = (a.X * r.Y - a.Y * r.X) / cross;
            if (t >= 0 && t < best && u >= 0 && u <= 1) { best = t; hitWall = w; }
        }
        return from + (to - from) * best;
    }

    private void Channel(Wizard p, float dt)
    {
        var spell = p.Channel!;
        var from = SpellOrigin(p, p.Facing, 0.7f);
        var end = RayEnd(from, p.Facing, spell.Range * p.Equipment.Range, out var wall, p.Id);
        if (wall != null) wall.Health -= WallDamage(wall, spell, p.Id, 110 * spell.Power * dt);
        // Los rayos rectos se resuelven juntos en TickChannels. Este método mantiene chorros y cadenas.
        if (spell.Shape != SpellShape.Beam)
        {
            var targets = _channelTargets;
            targets.Clear();
            float range = V2.Distance(end, from) + 0.1f;
            int order = 0;
            foreach (var q in _combatants.Values)
            {
                int index = order++;
                if (q.Id == p.Id || !q.Alive) continue;
                float distance = V2.Distance(q.Position, from);
                if (distance <= range && V2.Dot((q.Position - from).Normalized, p.Facing) >
                    (spell.Shape == SpellShape.Lightning ? 0.55f : 0.74f) && Visible(from, q.Position, p.Id))
                    targets.Add((q, distance, index));
            }
            targets.Sort(static (a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Order.CompareTo(b.Order));
            if (spell.Shape == SpellShape.Lightning)
            {
                // Mojado hace peligroso incluso preparar un rayo sobre uno mismo.
                if (p.Wet > 0) { Hit(p, spell, dt, p.Id, V2.Zero); p.Channel = null; }
                V2 last = from;
                for (int i = 0; i < Math.Min(3, targets.Count); i++)
                {
                    var target = targets[i].Target;
                    Hit(target, spell, dt, p.Id, p.Facing);
                    _beams.Add(new(p.Id, last, target.Position, spell.Elements, spell.Shape));
                    last = target.Position;
                }
                if (targets.Count == 0) _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
                return;
            }
            foreach (var target in targets) Hit(target.Target, spell, dt, p.Id, p.Facing);
        }
        _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
    }

    private bool Visible(V2 from, V2 to, int owner = 0)
    {
        float distance = V2.Distance(from, to);
        var end = RayEnd(from, (to - from).Normalized, distance, out _, owner);
        return V2.Distance(end, to) < 0.2f;
    }

    private void SelfCast(Wizard p)
    {
        if (!Ready(p)) return;
        Release(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        p.ActionCooldown = 0.45f;
        if (spell.Has(Element.Shield))
        {
            p.Ward = spell.Elements.Where(e => e != Element.Shield).Distinct().ToArray();
            p.WardTime = 12 * p.Equipment.ShieldDuration;
            p.Shield = p.ShieldMax = (spell.Has(Element.Earth) ? 650 : p.Ward.Length == 0 ? 420 : 180) * p.Equipment.ShieldCapacity;
            p.ShieldTime = p.WardTime;
            p.ShieldCharges = spell.Elements.Length;
            p.RegenerationRate = 38 * spell.Count(Element.Life);
        }
        else if (spell.Healing)
        {
            Heal(p, SpellBalance.SelfHeal * spell.Count(Element.Life) * HealingPower(p.Id));
        }
        else if (spell.Has(Element.Fire) && (p.Wet > 0 || p.Frozen > 0 || p.Chill > 0))
        {
            p.Wet = 0; p.Frozen = 0; p.Chill = 0;
        }
        else if (spell.Has(Element.Water))
        {
            p.Wet = 6 * p.Equipment.WetDuration; p.Burning = 0;
        }
        else if (spell.Elements.Length > 0) Hit(p, spell, 1, p.Id, V2.Zero);
        Emit("self", p.Position, spell.Elements, "");
    }

    private void Nova(Wizard p)
    {
        if (!Ready(p)) return;
        Release(p);
        var spell = new SpellRecipe(p.Selection.Consume());
        p.ActionCooldown = 0.8f;
        if (spell.Has(Element.Shield))
        {
            if (spell.Healing || spell.Has(Element.Arcane))
            {
                for (int i = 0; i < 6; i++) AddMine(p, spell, p.Position + new V2(1, 0).Rotated(i * MathF.Tau / 6) * 2.5f);
                Emit("mine_place", p.Position, spell.Elements, "");
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                var a = new V2(1, 0).Rotated(i * MathF.Tau / 6);
                var b = new V2(1, 0).Rotated((i + 1) * MathF.Tau / 6);
                AddWall(p, spell, p.Position + a * 2.5f, p.Position + b * 2.5f);
            }
        }
        else
        {
            foreach (var target in _combatants.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 4 * p.Equipment.Range))
                if (Visible(p.Position, target.Position, p.Id)) Hit(target, spell, 1.0f, p.Id, (target.Position - p.Position).Normalized);
        }
        Emit("nova", p.Position, spell.Elements, "", 4 * p.Equipment.Range);
    }

    private void CreateBarrier(Wizard p, SpellRecipe spell)
    {
        var center = p.Position + p.Facing * (3.2f * p.Equipment.Range);
        if (!Map.Inside(center)) return;
        if (spell.Has(Element.Life) || spell.Has(Element.Arcane))
        {
            AddMine(p, spell, center);
        }
        else AddWall(p, spell, center - p.Facing.Perpendicular * 2.2f, center + p.Facing.Perpendicular * 2.2f);
        Emit("barrier", center, spell.Elements, "");
    }

    private void AddWall(Wizard p, SpellRecipe recipe, V2 a, V2 b)
    {
        if (_walls.Count >= 40 || !Map.Inside(a) || !Map.Inside(b) || Map.BoundaryHit(a, b) != null) return;
        // Evita encerrar a alguien dentro de una pared recién creada.
        if (_combatants.Values.Any(q => q.Alive && Geometry.DistanceToSegment(q.Position, a, b) < 0.7f)) return;
        _walls.Add(new Wall { Id = _nextEntity++, Owner = p.Id, A = a, B = b, Recipe = recipe,
            Health = (recipe.Has(Element.Earth) ? 1300 : 650) * p.Equipment.ShieldCapacity,
            Lifetime = (recipe.Has(Element.Earth) ? 10 : 7) * p.Equipment.ShieldDuration });
    }

    private void Melee(Wizard p)
    {
        if (!Ready(p)) return;
        p.ActionCooldown = 0.5f;
        foreach (var q in _combatants.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 1.8f))
            if (V2.Dot((q.Position - p.Position).Normalized, p.Facing) > 0.2f &&
                Visible(p.Position, q.Position) &&
                (Options.FriendlyFire && !IsDefense || !Allied(p, q))) Damage(q, 90, p.Id);
        Emit("melee", p.Position + p.Facing, [], "");
    }

    private void Magick(Wizard p, int slot)
    {
        // Las magias tienen energía y recarga propias: un ataque recién soltado
        // no debe perder el lanzamiento especial al finalizar el gesto móvil.
        if (!p.Alive || p.Frozen > 0 || MatchEnded || slot is < 0 or > 3 || p.MagickCooldowns[slot] > 0) return;
        var magick = SpellBalance.Magicks[slot];
        float cost = magick.Cost * (slot == 3 ? p.Equipment.LifeCost : 1);
        if (p.Energy < cost) return;
        var target = new V2(p.Input.AimX, p.Input.AimY);
        if (slot == 0) p.Haste = 4;
        if (slot == 1)
        {
            var direction = (target - p.Position).Normalized;
            var last = MagickRules.BlinkDestination(p.Position, target, ClearMovement);
            if (V2.Distance(last, p.Position) < 0.4f) return;
            Emit("blink", p.Position, [], "");
            p.Position = last; p.Facing = direction; p.Input.MoveTo = false;
            p.Knockback = V2.Zero;
            ResetSlide(p);
            _routes.Remove(p.Id);
            Emit("blink_arrive", p.Position, [], "");
        }
        if (slot == 2)
        {
            target = MagickRules.MeteorTarget(p.Position, target);
            if (!Map.Walkable(target) || _fields.Count >= 40) return;
            _fields.Add(new Field { Id = _nextEntity++, Owner = p.Id, Position = target, Radius = SpellBalance.MeteorRadius,
                Lifetime = SpellBalance.MeteorLifetime, Delay = SpellBalance.MeteorDelay, Kind = "meteor",
                Recipe = new SpellRecipe([Element.Earth, Element.Fire, Element.Fire]) });
        }
        if (slot == 3)
        {
            var ally = _players.Values.Where(q => q.Id != p.Id && MagickRules.CanReanimate(Options.Mode, p.Team, q.Team, q.Alive, q.Deaths, V2.Distance(q.Position, p.Position)))
                .OrderBy(q => V2.Distance(q.Position, p.Position)).ThenBy(q => q.Id).FirstOrDefault();
            if (ally == null) return;
            var location = ally.Position;
            Respawn(ally); if (Walkable(location)) ally.Position = location; ally.Health = Math.Min(600, ally.MaxHealth);
        }
        if (slot != 0) CancelCast(p);
        p.Energy -= cost; p.MagickCooldowns[slot] = magick.Cooldown;
        p.ActionCooldown = 0.2f;
        Emit("magick", p.Position, [], magick.Name);
    }

    private void Hit(Wizard target, SpellRecipe spell, float scale, int owner, V2 force, float damageMultiplier = 1)
    {
        if (!target.Alive) return;
        var equipment = _combatants.TryGetValue(owner, out var attacker) ? attacker.Equipment : EquipmentModifiers.None;
        if (spell.Healing) Heal(target, SpellBalance.Heal * spell.Count(Element.Life) * scale * HealingPower(owner));
        if (target.Invulnerable > 0) return;
        if (_combatants.TryGetValue(owner, out var source) && source.Id != target.Id && Allied(source, target) && (!Options.FriendlyFire || IsDefense)) return;
        float damage = 0;
        foreach (var element in SpellBalance.Offensive)
            damage += ElementDamage(target, owner, element, SpellBalance.Component(spell, element, target.Wet > 0));
        if (spell.Has(Element.Water) && !spell.Steam && !spell.Ice && Resistance(target, Element.Water) > 0.5f)
        {
            target.Wet = 6 * equipment.WetDuration; target.Burning = 0;
            target.Knockback += force * (18 * scale * equipment.Water);
        }
        if (spell.Shape == SpellShape.Force) target.Knockback += force * (45 * scale);
        if (spell.Has(Element.Fire) && !spell.Steam && Resistance(target, Element.Fire) > 0.5f)
        {
            if (target.Wet > 0) target.Wet = Math.Max(0, target.Wet - 12 * scale);
            else { target.Burning = SpellBalance.BurnDuration * (_combatants.TryGetValue(owner, out var burner) ? burner.Equipment.BurnDuration : 1);
                target.BurnOwner = owner; target.BurnMultiplier = Outgoing(owner, Element.Fire); }
            target.Chill = Math.Max(0, target.Chill - 100 * scale);
            target.Frozen = Math.Max(0, target.Frozen - 5 * scale);
        }
        if (spell.Has(Element.Cold) && Resistance(target, Element.Cold) > 0.5f)
        {
            target.Burning = 0;
            target.Chill = Math.Min(100, target.Chill + 38 * spell.Count(Element.Cold) * (target.Wet > 0 ? 2 : 1) * scale * equipment.ChillPower);
            if (target.Chill >= 99) { target.Frozen = 1.4f * equipment.FreezeDuration; target.Wet = 0; }
        }
        if (spell.Has(Element.Earth)) target.Knockback += force * (10 * scale);
        damage *= damageMultiplier;
        Damage(target, damage * scale, owner, spell.Elements);
        if (target.Shield > 0) DamageProtection(target, UtilityPressure(target, spell, owner) * scale, owner, spell.Elements);
        if (_combatants.TryGetValue(owner, out var caster) && caster.Id != target.Id)
            caster.Energy = Math.Min(100, caster.Energy + damage * scale * 0.015f);
    }

    private void Damage(Wizard target, float amount, int owner, Element[]? elements = null)
        => ApplyDamage(target, amount, owner, elements, false);
    private void DamageProtection(Wizard target, float amount, int owner, Element[] elements)
        => ApplyDamage(target, amount, owner, elements, true);
    private void ApplyDamage(Wizard target, float amount, int owner, Element[]? elements, bool shieldOnly)
    {
        if (!target.Alive || target.Invulnerable > 0 || amount <= 0) return;
        amount *= DamagePower(owner) * RelicBuffs.Incoming(target) * target.Equipment.Incoming;
        float absorbed = Math.Min(target.Shield, amount);
        target.Shield -= absorbed; amount -= absorbed;
        if (shieldOnly) amount = 0;
        if (Map.IsPractice)
        {
            var reading = PracticeReading(target.Id);
            reading.Absorbed += absorbed; reading.Damage += Math.Min(target.Health, amount);
        }
        target.Health = Math.Max(0, target.Health - amount);
        // Un rayo golpea a 60 Hz; limita sus impactos, pero nunca omite la rotura del escudo.
        bool broken = absorbed > 0 && target.Shield <= 0;
        if (broken) { target.RegenerationRate = 0; Emit("shield_break", target.Position, [Element.Shield], ""); }
        if (target.HitEffectCooldown <= 0)
        {
            if (absorbed > 0 && !broken) Emit("shield_hit", target.Position, [Element.Shield], "");
            if (amount > 0) Emit("hurt", target.Position, elements ?? [], "");
            target.HitEffectCooldown = 0.14f;
        }
        if (target.Health > 0) return;
        if (DefenseDeath(target, owner)) return;
        target.Deaths++;
        target.Respawn = IsDefense ? 4 + target.Deaths * 0.5f : Map.RespawnSeconds(Options.Mode, target.Deaths);
        RelicBuffs.Clear(target);
        CancelCast(target); target.Selection.Clear(); target.Shield = target.ShieldMax = target.ShieldTime = target.RegenerationRate = 0;
        if (Options.Mode == GameMode.Warfare && !Map.IsPractice && !HoldToWin && !ConquestToWin) Scores[target.Team] = Math.Max(0, Scores[target.Team] - 1);
        string text = $"{target.Name} ha caído";
        if (_combatants.TryGetValue(owner, out var killer) && killer.Id != target.Id)
        {
            bool enemy = Hostile(killer, target);
            killer.Kills = Math.Max(0, killer.Kills + (enemy ? 1 : -1));
            killer.Energy = Math.Min(100, killer.Energy + 15);
            // Cada baja de otro mago cuenta para la ascensión, incluso con fuego amigo.
            // Una baja póstuma aumenta la capacidad, pero no reanima al autor.
            if (Map.KillHealthBonus > 0)
            {
                killer.MaxHealth += Map.KillHealthBonus;
                if (killer.Alive) killer.Health = Math.Min(killer.MaxHealth, killer.Health + Map.KillHealthBonus);
            }
            if (enemy && Options.Mode == GameMode.Duel) Scores[killer.Team]++;
            text = $"{killer.Name} → {target.Name}";
        }
        Emit("death", target.Position, [], text);
    }

    private void TickProjectiles(float dt)
    {
        foreach (var projectile in _projectiles)
        {
            projectile.Lifetime -= dt;
            var from = projectile.Position; var to = from + projectile.Velocity * dt;
            var boundary = Map.BoundaryHit(from, to, projectile.Radius);
            float nearest = boundary ?? 1; Wizard? target = null; Wall? wallHit = null; bool obstacleHit = boundary != null;
            foreach (var o in Map.Obstacles)
                if (Geometry.SegmentCircle(from, to, o.Position, o.Radius + projectile.Radius) is float t && t < nearest)
                { nearest = t; obstacleHit = true; target = null; wallHit = null; }
            foreach (var w in _walls)
                if (w.Health > 0 && !PassesOwnShield(w, projectile.Owner) && Geometry.SegmentCapsule(from, to, w.A, w.B, projectile.Radius + 0.2f) is float t && t < nearest)
                { nearest = t; wallHit = w; obstacleHit = false; target = null; }
            foreach (var q in _combatants.Values)
                if (q.Alive && q.Id != projectile.Owner && Geometry.SegmentCircle(from, to, q.Position, (IsDefense && _minions.TryGetValue(q.Id, out var minion) && minion.Kind == MinionKind.Boss ? 1.4f : ArenaMap.WizardRadius) + projectile.Radius) is float t && t < nearest)
                { nearest = t; target = q; obstacleHit = false; wallHit = null; }
            projectile.Position = V2.Lerp(from, to, nearest);
            if (target != null)
            {
                Hit(target, projectile.Recipe, projectile.Power, projectile.Owner, projectile.Velocity.Normalized);
                projectile.Lifetime = 0;
            }
            if (wallHit != null)
            {
                wallHit.Health -= WallDamage(wallHit, projectile.Recipe, projectile.Owner, 170 * projectile.Power * projectile.Recipe.Power);
                if (wallHit.Recipe.Elements.Length == 1)
                {
                    var normal = (wallHit.B - wallHit.A).Perpendicular.Normalized;
                    projectile.Velocity -= normal * (2 * V2.Dot(projectile.Velocity, normal));
                    projectile.Position += projectile.Velocity.Normalized * (projectile.Radius + 0.4f);
                    projectile.Owner = wallHit.Owner; projectile.Power *= 0.8f;
                }
                else projectile.Lifetime = 0;
            }
            if (obstacleHit || !Map.Inside(projectile.Position)) projectile.Lifetime = 0;
            if ((target != null || obstacleHit || wallHit != null && projectile.Lifetime <= 0) && Map.Inside(projectile.Position))
                ProjectileSplash(projectile, target?.Id ?? -1);
            if (projectile.Lifetime <= 0)
                Emit("impact", projectile.Position, projectile.Recipe.Elements, "");
        }
        _projectiles.RemoveAll(p => p.Lifetime <= 0);
    }

    private void TickWallsAndFields(float dt)
    {
        foreach (var w in _walls)
        {
            w.Lifetime -= dt;
            if (w.Health <= 0 || w.Lifetime <= 0) continue;
            if (w.Recipe.Has(Element.Fire) || w.Recipe.Has(Element.Cold) || w.Recipe.Has(Element.Lightning) || w.Recipe.Has(Element.Water))
                foreach (var p in _combatants.Values)
                    if (p.Alive && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 1)
                        Hit(p, w.Recipe, dt, w.Owner, (p.Position - (w.A + w.B) / 2).Normalized);
        }
        _walls.RemoveAll(w => w.Lifetime <= 0 || w.Health <= 0);
        foreach (var f in _fields)
        {
            f.Lifetime -= dt; f.Delay -= dt;
            if (f.Lifetime <= 0 || f.Delay > 0) continue;
            if (f.Kind is "burning_ground" or "frost_ground" or "water_pool")
            {
                foreach (var p in _combatants.Values)
                    if (p.Alive && V2.Distance(p.Position, f.Position) < f.Radius && Visible(f.Position, p.Position, f.Owner))
                        Hit(p, f.Recipe, dt * SpellBalance.GroundPower, f.Owner, (p.Position - f.Position).Normalized);
                continue;
            }
            if (f.Kind == "defense_meteor")
            {
                foreach (var minion in _minions.Values)
                    if (minion.Body.Alive && V2.Distance(minion.Body.Position, f.Position) < f.Radius && Visible(f.Position, minion.Body.Position, f.Owner))
                        Damage(minion.Body, f.Power, f.Owner, [Element.Earth, Element.Fire]);
                Emit("explosion", f.Position, [Element.Earth, Element.Fire], "Meteorito", f.Radius);
                f.Lifetime = 0; continue;
            }
            bool trigger = f.Kind == "meteor" || _combatants.Values.Any(p => EligibleForMine(p, f) && V2.Distance(p.Position, f.Position) < f.TriggerRadius);
            if (!trigger) continue;
            foreach (var p in _combatants.Values)
            {
                if (!p.Alive || !(V2.Distance(p.Position, f.Position) < f.Radius) || !Visible(f.Position, p.Position, f.Owner)) continue;
                if (IsDefense && f.Kind == "meteor" && _combatants.TryGetValue(f.Owner, out var caster) && Allied(caster, p)) continue;
                if (f.Kind == "healing_mine")
                {
                    if (_combatants.TryGetValue(f.Owner, out var owner) && Allied(owner, p))
                        Heal(p, SpellBalance.MineHeal * f.Recipe.Count(Element.Life) * HealingPower(owner.Id));
                }
                else Hit(p, f.Recipe, f.Kind == "meteor" ? SpellBalance.MeteorPower : SpellBalance.MinePower, f.Owner, (p.Position - f.Position).Normalized);
            }
            Emit(f.Kind == "healing_mine" ? "mine_heal" : "explosion", f.Position, f.Recipe.Elements, f.Kind == "meteor" ? "Meteoro" : "", f.Radius);
            f.Lifetime = 0;
        }
        _fields.RemoveAll(f => f.Lifetime <= 0);
    }

    private void TickCapture(float dt)
    {
        // Se fijan los dos objetivos antes de resolver el tick: nunca se captura una cadena de golpe.
        int blueTarget = ConquestToWin ? FrontierTarget(0) : -1;
        int redTarget = ConquestToWin ? FrontierTarget(1) : -1;
        foreach (var point in _points)
        {
            int blue = 0, red = 0;
            foreach (var player in _players.Values)
            {
                if (!player.Alive || !(V2.Distance(player.Position, point.Position) < 3)) continue;
                if (player.Team == 0) blue++; else if (player.Team == 1) red++;
            }
            point.Contested = blue > 0 && red > 0 || HoldToWin && (point.Owner == 0 && red > 0 || point.Owner == 1 && blue > 0);
            if (blue > 0 && red > 0 || blue + red == 0) continue;
            int team = red > 0 ? 1 : 0;
            if (ConquestToWin && point.Owner != team && point.Id != (team == 0 ? blueTarget : redTarget)) continue;
            point.Progress = Math.Clamp(point.Progress + (red > 0 ? 1 : -1) * dt * 0.24f, -1, 1);
            if (point.Owner == 0 && point.Progress > -0.6f || point.Owner == 1 && point.Progress < 0.6f) point.Owner = -1;
            if (point.Progress <= -0.99f) point.Owner = 0;
            if (point.Progress >= 0.99f) point.Owner = 1;
        }
        if (ConquestToWin) return;
        if (HoldToWin)
        {
            int team = _points[0].Owner;
            if (team >= 0 && _points.All(p => p.Owner == team && !p.Contested))
            {
                if (HoldingTeam != team) { HoldingTeam = team; _holdTicks = 0; }
                _holdTicks++;
            }
            else { HoldingTeam = -1; _holdTicks = 0; }
            return;
        }
        _ticketClock += dt;
        if (_ticketClock < 1) return;
        _ticketClock -= 1;
        int advantage = _points.Count(p => p.Owner == 0) - _points.Count(p => p.Owner == 1);
        if (advantage > 0) Scores[1] = Math.Max(0, Scores[1] - advantage);
        if (advantage < 0) Scores[0] = Math.Max(0, Scores[0] + advantage);
    }

    private void CheckVictory()
    {
        if (Map.IsPractice || IsDefense) return;
        int winner = -1;
        if (Options.Mode == GameMode.Warfare)
        {
            if (ConquestToWin)
            {
                int team = _points[0].Owner;
                if (team >= 0 && _points.All(p => p.Owner == team && !p.Contested)) winner = team;
            }
            else if (HoldToWin)
            {
                if (_holdTicks >= Map.HoldSeconds * 60) winner = HoldingTeam;
            }
            else if (Scores[0] <= 0) winner = 1;
            else if (Scores[1] <= 0) winner = 0;
        }
        else if (Options.Mode == GameMode.Duel)
        {
            if (Scores[0] >= Options.ScoreLimit) winner = 0;
            if (Scores[1] >= Options.ScoreLimit) winner = 1;
        }
        else
        {
            var champion = _players.Values.FirstOrDefault(p => p.Kills >= Options.ScoreLimit);
            if (champion != null) winner = champion.Id;
        }
        if (winner < 0) return;
        Winner = winner; MatchEnded = true; RestartIn = 9;
        foreach (var p in _players.Values) CancelCast(p);
        Emit("victory", V2.Zero, [], "Partida terminada");
    }

    private void ResetScores()
    {
        Scores[0] = Scores[1] = Options.Mode == GameMode.Warfare && !Map.IsPractice && !ConquestToWin ? Options.Tickets : 0;
        _ticketClock = 0;
        _holdTicks = 0; HoldingTeam = -1; _routes.Clear();
    }
    /// <summary>El servidor puede cancelar una serie incompleta al perder jugadores.</summary>
    public void ResetForWaitingRoom() => ResetMatch();

    private void ResetMatch()
    {
        MatchEnded = false; Winner = -1; RestartIn = 0;
        ResetScores(); _projectiles.Clear(); _walls.Clear(); _fields.Clear();
        ResetFrontline();
        ResetDefense();
        foreach (var point in _points) { point.Owner = -1; point.Progress = 0; point.Contested = false; }
        foreach (var item in _pickups) { item.Respawn = 0; if (Map.RandomPickups) RelocatePickup(item); }
        foreach (var p in _players.Values) { p.Kills = p.Deaths = 0; RelicBuffs.Clear(p); p.MaxHealth = Wizard.BaseMaxHealth; Respawn(p); p.Energy = 100; Array.Clear(p.MagickCooldowns); }
        Emit("restart", V2.Zero, [], "Nueva partida");
    }
    private void Respawn(Wizard p)
    {
        if (IsDefense && p.Deaths >= DefenseRules.PlayerLives) return;
        _routes.Remove(p.Id);
        ResetSlide(p);
        p.Position = RespawnPosition(p); p.Health = p.MaxHealth;
        if (Map.IsPractice)
            _walls.RemoveAll(w => Geometry.DistanceToSegment(p.Position, w.A, w.B) < ArenaMap.WizardRadius + 0.18f);
        p.Wet = p.Burning = p.Chill = p.Frozen = p.Haste = p.Shield = p.ShieldMax = p.ShieldTime = 0;
        p.RegenerationRate = 0;
        p.Ward = []; p.Knockback = V2.Zero; p.Selection.Clear(); CancelCast(p);
        p.BurnMultiplier = 1; p.HitEffectCooldown = 0;
        p.TeleportCooldown = 0;
        p.TeleportNeedsExit = false;
        p.Input.MoveTo = false; p.Input.MoveX = p.Input.MoveY = 0; p.Respawn = 0;
        p.ShieldCharges = 1;
        p.Invulnerable = _practiceSpawns.ContainsKey(p.Id) ? 0 : Options.Mode == GameMode.Arena ? 1 : 1.2f; p.ActionCooldown = 0;
    }

    private V2 RespawnPosition(Wizard p)
    {
        if (Map.IsPractice) return _practiceSpawns.GetValueOrDefault(p.Id, PracticeLayout.Respawn);
        if (IsDefense)
        {
            if (Walkable(p.Position)) return p.Position;
            for (float radius = 0.8f; radius <= 8; radius += 0.4f)
                for (int i = 0; i < 24; i++)
                {
                    float angle = i * MathF.Tau / 24;
                    var at = p.Position + new V2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    if (Walkable(at)) return at;
                }
        }
        if (Options.Mode == GameMode.Arena) return ArenaSpawnPosition(p, p.Deaths > 0 ? p.Position : null);
        var preferred = TeamSpawn(p.Team, p.Id % 4);
        for (int offset = 0; offset < 4; offset++)
        {
            var spawn = TeamSpawn(p.Team, (p.Id + offset) % 4);
            if (Walkable(spawn)) return spawn;
        }
        // Si los cuatro puestos están cubiertos, busca suelo libre cerca de la base.
        V2 best = preferred; float nearest = float.PositiveInfinity;
        for (float x = -Map.HalfWidth + 1; x < Map.HalfWidth; x++)
        for (float y = -Map.HalfHeight + 1; y < Map.HalfHeight; y++)
        {
            var candidate = new V2(x, y); float distance = (candidate - preferred).LengthSquared;
            if (distance >= nearest || !Walkable(candidate)) continue;
            best = candidate; nearest = distance;
        }
        return best;
    }

    private void ThinkBot(Wizard p, float dt)
    {
        Wizard? target = null; float closest = float.MaxValue;
        foreach (var candidate in _players.Values)
        {
            if (!candidate.Alive || !Hostile(p, candidate)) continue;
            float squared = (p.Position - candidate.Position).LengthSquared;
            if (squared < closest) { closest = squared; target = candidate; }
        }
        V2 goal = target?.Position ?? Map.ControlPoints[p.Id % Map.ControlPoints.Count];
        if (Options.Mode == GameMode.Warfare && (target == null || V2.Distance(p.Position, target.Position) > 12))
        {
            int frontier = ConquestToWin ? FrontierTarget(p.Team) : -1;
            ControlPoint? point = null; float nearest = float.PositiveInfinity;
            foreach (var candidate in _points)
            {
                if (ConquestToWin ? candidate.Id != frontier : candidate.Owner == p.Team && !candidate.Contested) continue;
                float range = V2.Distance(candidate.Position, p.Position);
                if (range < nearest) { nearest = range; point = candidate; }
            }
            goal = point?.Position ?? _points[p.Id % _points.Length].Position;
        }
        float distance = target == null ? 100 : V2.Distance(p.Position, target.Position);
        if (_pickups.Length > 0 && (target == null || distance > 9))
        {
            Pickup? item = null; float nearest = float.PositiveInfinity;
            foreach (var candidate in _pickups)
            {
                if (!(candidate.Respawn <= 0)) continue;
                float range = V2.Distance(p.Position, candidate.Position);
                if (range < nearest) { nearest = range; item = candidate; }
            }
            if (item != null && V2.Distance(p.Position, item.Position) < 12) goal = item.Position;
        }
        var direction = (goal - p.Position).Normalized;
        if (target != null && distance < 8) direction = direction.Perpendicular * MathF.Sin((float)Time * 0.65f + p.Id);
        if (target != null && distance < 3.5f) direction = (p.Position - target.Position).Normalized;
        if (Map.Id != ArenaMapId.Classic && (target == null || distance > 12 || !Visible(p.Position, target.Position, p.Id) || !Walkable(p.Position + direction * 1.4f)))
            direction = NavigationDirection(p, goal, dt);
        foreach (var o in Map.Obstacles)
            if (V2.Distance(p.Position + direction * 1.6f, o.Position) < o.Radius + 1)
                direction = (p.Position - o.Position).Normalized.Perpendicular * (p.Id % 2 == 0 ? 1 : -1);
        var aim = target?.Position ?? goal;
        if (p.BotWallTarget > 0 && p.Charging != null)
        {
            var wall = _walls.FirstOrDefault(w => w.Id == p.BotWallTarget && w.Health > 0);
            if (wall != null) aim = (wall.A + wall.B) / 2;
            else CancelCast(p);
        }
        p.Input = new() { Type = "input", MoveX = direction.X, MoveY = direction.Y, AimX = aim.X, AimY = aim.Y };
        p.LastInput = Time;
        UpdateFacing(p);
        p.BotDecision -= dt;
        if (p.BotDecision > 0 || p.Frozen > 0 || !Ready(p)) return;
        p.BotDecision = 0.6f + (float)_random.NextDouble() * 0.5f;
        if (p.Channel != null || p.Charging != null) Release(p);
        p.Selection.Clear();
        if (p.Health < p.MaxHealth * 0.6f || p.Burning > 0)
        {
            if (p.Burning > 0) p.Selection.Add(Element.Water);
            else for (int i = 0; i < 3; i++) p.Selection.Add(Element.Life);
            SelfCast(p); return;
        }
        if (ConquestToWin && (target == null || distance > 12 || !Visible(p.Position, target.Position, p.Id)) && TryAttackFrontlineWall(p, goal)) return;
        if (target == null || distance > 18 || !Visible(p.Position, target.Position, p.Id)) return;
        if (p.Shield <= 0 && _random.Next(8) == 0)
        {
            p.Selection.Add(Element.Shield); SelfCast(p); return;
        }
        if (p.Energy >= 75 && _random.Next(16) == 0) { Magick(p, 2); return; }
        int choice = distance > 9 ? _random.Next(2) == 0 ? 0 : 3 : _random.Next(BotRecipes.Length);
        foreach (var element in BotRecipes[choice]) p.Selection.Add(element);
        p.Facing = (aim - p.Position).Normalized;
        BeginCast(p);
    }

    private void Emit(string kind, V2 position, Element[] elements, string text, float radius = 0)
    {
        _events.Add((Tick, new(_nextEvent++, kind, position, elements, text, radius)));
        if (_events.Count > 160) _events.RemoveRange(0, _events.Count - 160);
    }

}
