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
        if (!Map.SupportsMode(Options.Mode)) throw new ArgumentException("Frente de conquista requiere el modo Guerra.", nameof(options));
        _random = new(Options.Seed);
        _points = Map.ControlPoints.Select((p, i) => new ControlPoint { Id = i, Position = p }).ToArray();
        _pickupRandom = new(Options.PickupSeed ?? Random.Shared.Next());
        _pickups = CreatePickups();
        ResetScores();
        for (int i = 0; i < Math.Min(Options.Bots, Options.MaxPlayers); i++)
            AddPlayer($"Bot {i + 1}", -1, true, (WizardType)(i % 5));
    }

    public Wizard? AddPlayer(string name, int preferredTeam = -1, bool bot = false, WizardType wizardType = WizardType.Balanced)
    {
        if (!Enum.IsDefined(wizardType) || _players.Count >= Options.MaxPlayers || Options.Mode == GameMode.Duel && _players.Count >= 2) return null;
        int blue = _players.Values.Count(p => p.Team == 0), red = _players.Count - blue;
        // No se puede llenar un solo equipo mediante el campo del saludo.
        int team = blue == red && preferredTeam is 0 or 1 ? preferredTeam : blue <= red ? 0 : 1;
        string cleanName = new((name ?? "Mago").Where(c => !char.IsControl(c) && c != '[' && c != ']').Take(24).ToArray());
        if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "Mago";
        var wizard = new Wizard { Id = _nextPlayer++, Name = cleanName.Trim(), Team = team, Bot = bot, WizardType = wizardType };
        wizard.Position = Options.Mode == GameMode.Arena ? ArenaSpawnPosition(wizard, null)
            : TeamSpawn(team, _players.Values.Count(p => p.Team == team));
        if (ConquestToWin && !Walkable(wizard.Position)) wizard.Position = RespawnPosition(wizard);
        wizard.Facing = new(team == 0 ? 1 : -1, 0);
        wizard.LastInput = Time;
        _players.Add(wizard.Id, wizard);
        Emit("join", wizard.Position, [], $"{wizard.Name} entra en la arena");
        return wizard;
    }

    public void RemovePlayer(int id)
    {
        if (!_players.Remove(id, out var wizard)) return;
        _routes.Remove(id);
        _projectiles.RemoveAll(p => p.Owner == id);
        _walls.RemoveAll(p => p.Owner == id);
        _fields.RemoveAll(p => p.Owner == id);
        Emit("leave", wizard.Position, [], $"{wizard.Name} sale de la arena");
    }

    public bool ApplyMessage(int id, ClientMessage message)
    {
        if (!_players.TryGetValue(id, out var p) || message.Sequence <= p.LastSequence) return false;
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
            return true;
        }
        if (message.Type is not ("element" or "clear" or "cast" or "release" or "self" or "nova" or "melee" or "magick") ||
            message.Type == "element" && !Enum.IsDefined(message.Element) || message.Type == "magick" && message.Slot is < 0 or > 3) return false;
        p.LastSequence = message.Sequence;
        if (!p.Alive || MatchEnded || p.Frozen > 0) return false;
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
        Tick++;
        const float dt = StepSeconds;
        _beams.Clear();
        _events.RemoveAll(e => Tick - e.Tick > 90);
        if (MatchEnded)
        {
            RestartIn -= dt;
            if (RestartIn <= 0) ResetMatch();
            return;
        }
        foreach (var p in _players.Values)
        {
            p.ActionCooldown = Math.Max(0, p.ActionCooldown - dt);
            p.HitEffectCooldown = Math.Max(0, p.HitEffectCooldown - dt);
            p.Invulnerable = p.Invulnerable <= dt + 0.00001f ? 0 : p.Invulnerable - dt;
            for (int i = 0; i < 4; i++) p.MagickCooldowns[i] = Math.Max(0, p.MagickCooldowns[i] - dt);
            if (!p.Alive)
            {
                p.Respawn -= dt;
                if (p.Respawn <= 0) Respawn(p);
                continue;
            }
            if (p.Bot) ThinkBot(p, dt);
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
                p.ChargeTime = Math.Min(SpellBalance.ChargeSeconds, p.ChargeTime + dt);
                if (p.ChargeTime >= SpellBalance.ChargeSeconds) Release(p);
            }
        }
        TickPickups(dt);
        TickFrontline(dt);
        // Todos se mueven antes de resolver los canales: cruzar rayos no depende del orden de los jugadores.
        TickChannels(dt);
        TickProjectiles(dt);
        TickWallsAndFields(dt);
        if (Options.Mode == GameMode.Warfare) TickCapture(dt);
        CheckVictory();
    }

    private void TickStatus(Wizard p, float dt)
    {
        p.Wet = Math.Max(0, p.Wet - dt);
        p.Frozen = Math.Max(0, p.Frozen - dt);
        p.Chill = Math.Max(0, p.Chill - 10 * dt);
        p.Haste = Math.Max(0, p.Haste - dt);
        p.Energy = Math.Min(100, p.Energy + (1.8f + RelicBuffs.Energy(p)) * dt);
        p.ShieldTime = Math.Max(0, p.ShieldTime - dt);
        p.WardTime -= dt;
        if (p.ShieldTime <= 0) p.Shield = p.ShieldMax = p.RegenerationRate = 0;
        if (p.Shield <= 0) p.RegenerationRate = 0;
        p.Health = Math.Min(p.MaxHealth, p.Health + (p.RegenerationRate * RelicBuffs.Healing(p) + RelicBuffs.Regeneration(p)) * dt);
        if (p.WardTime <= 0) p.Ward = [];
        if (p.Burning > 0)
        {
            p.Burning = Math.Max(0, p.Burning - dt);
            Damage(p, SpellBalance.BurnPerSecond * dt * p.BurnMultiplier * WizardTypes.Incoming(p.WizardType, Element.Fire) * Resistance(p, Element.Fire), p.BurnOwner, [Element.Fire]);
        }
    }

    private bool Allied(Wizard a, Wizard b) => a.Id == b.Id || Options.Mode != GameMode.Arena && a.Team == b.Team;
    private bool Hostile(Wizard a, Wizard b) => !Allied(a, b);
    private float Resistance(Wizard p, Element e) => p.Ward.Contains(e) ? 0.18f : 1;
    private float Outgoing(int owner, Element element) => _players.TryGetValue(owner, out var source)
        ? WizardTypes.Outgoing(source.WizardType, element) : 1;
    private float ElementDamage(Wizard target, int owner, Element element, float amount) =>
        amount * Outgoing(owner, element) * WizardTypes.Incoming(target.WizardType, element) * Resistance(target, element);

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
        float speed = 6.2f * RelicBuffs.Speed(p) * (p.Haste > 0 ? 1.65f : 1) * (1 - p.Chill * 0.005f);
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
    private bool ClearWalls(V2 from, V2 to) =>
        !_walls.Any(w => w.Health > 0 && Geometry.SegmentCapsule(from, to, w.A, w.B, ArenaMap.WizardRadius + 0.18f) != null);

    public bool Walkable(V2 position)
    {
        float r = ArenaMap.WizardRadius;
        if (!Map.Walkable(position)) return false;
        return !_walls.Any(w => w.Health > 0 && Geometry.DistanceToSegment(position, w.A, w.B) < r + 0.18f);
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
            Velocity = direction * speed, Recipe = spell, Power = power, Radius = radius
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
        foreach (var wall in _walls.Where(w => w.Health > 0))
            if (Geometry.SegmentCapsule(caster.Position, to, wall.A, wall.B, radius + 0.2f) is float t && (nearest == null || t < nearest))
                nearest = t;
        return nearest is float hit ? V2.Lerp(caster.Position, to, Math.Max(0, hit - 0.01f)) : to;
    }

    private V2 RayEnd(V2 from, V2 direction, float range, out Wall? hitWall)
    {
        hitWall = null;
        if (!Map.Inside(from)) return from;
        var to = from + direction * range;
        float best = Map.BoundaryHit(from, to) ?? 1;
        foreach (var o in Map.Obstacles)
            if (Geometry.SegmentCircle(from, to, o.Position, o.Radius) is float t && t < best) best = t;
        // Segmento de pared contra rayo mediante productos cruzados.
        foreach (var w in _walls.Where(w => w.Health > 0))
        {
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
        var end = RayEnd(from, p.Facing, spell.Range, out var wall);
        if (wall != null && !spell.Healing) wall.Health -= 110 * spell.Power * DamagePower(p.Id) * dt;
        // Los rayos rectos se resuelven juntos en TickChannels. Este método mantiene chorros y cadenas.
        if (spell.Shape != SpellShape.Beam)
        {
            var targets = _players.Values.Where(q => q.Id != p.Id && q.Alive)
                .Where(q => V2.Distance(q.Position, from) <= V2.Distance(end, from) + 0.1f &&
                            V2.Dot((q.Position - from).Normalized, p.Facing) >
                            (spell.Shape == SpellShape.Lightning ? 0.55f : 0.74f))
                .Where(q => Visible(from, q.Position)).OrderBy(q => V2.Distance(q.Position, from)).ToArray();
            if (spell.Shape == SpellShape.Lightning)
            {
                // Mojado hace peligroso incluso preparar un rayo sobre uno mismo.
                if (p.Wet > 0) { Hit(p, spell, dt, p.Id, V2.Zero); p.Channel = null; }
                V2 last = from;
                foreach (var target in targets.Take(3))
                {
                    Hit(target, spell, dt, p.Id, p.Facing);
                    _beams.Add(new(p.Id, last, target.Position, spell.Elements, spell.Shape));
                    last = target.Position;
                }
                if (targets.Length == 0) _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
                return;
            }
            foreach (var target in targets) Hit(target, spell, dt, p.Id, p.Facing);
        }
        _beams.Add(new(p.Id, from, end, spell.Elements, spell.Shape));
    }

    private bool Visible(V2 from, V2 to)
    {
        float distance = V2.Distance(from, to);
        var end = RayEnd(from, (to - from).Normalized, distance, out _);
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
            p.WardTime = 12;
            p.Shield = p.ShieldMax = spell.Has(Element.Earth) ? 650 : p.Ward.Length == 0 ? 420 : 180;
            p.ShieldTime = 12;
            p.ShieldCharges = spell.Elements.Length;
            p.RegenerationRate = 38 * spell.Count(Element.Life);
        }
        else if (spell.Healing)
        {
            p.Health = Math.Min(p.MaxHealth, p.Health + SpellBalance.SelfHeal * spell.Count(Element.Life) * RelicBuffs.Healing(p));
        }
        else if (spell.Has(Element.Fire) && (p.Wet > 0 || p.Frozen > 0 || p.Chill > 0))
        {
            p.Wet = 0; p.Frozen = 0; p.Chill = 0;
        }
        else if (spell.Has(Element.Water))
        {
            p.Wet = 6; p.Burning = 0;
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
            foreach (var target in _players.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 4))
                if (Visible(p.Position, target.Position)) Hit(target, spell, 1.0f, p.Id, (target.Position - p.Position).Normalized);
        }
        Emit("nova", p.Position, spell.Elements, "", 4);
    }

    private void CreateBarrier(Wizard p, SpellRecipe spell)
    {
        var center = p.Position + p.Facing * 3.2f;
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
        if (_players.Values.Any(q => q.Alive && Geometry.DistanceToSegment(q.Position, a, b) < 0.7f)) return;
        _walls.Add(new Wall { Id = _nextEntity++, Owner = p.Id, A = a, B = b, Recipe = recipe,
            Health = recipe.Has(Element.Earth) ? 1300 : 650, Lifetime = recipe.Has(Element.Earth) ? 10 : 7 });
    }

    private void Melee(Wizard p)
    {
        if (!Ready(p)) return;
        p.ActionCooldown = 0.5f;
        foreach (var q in _players.Values.Where(q => q.Id != p.Id && q.Alive && V2.Distance(q.Position, p.Position) < 1.8f))
            if (V2.Dot((q.Position - p.Position).Normalized, p.Facing) > 0.2f &&
                Visible(p.Position, q.Position) &&
                (Options.FriendlyFire || !Allied(p, q))) Damage(q, 90, p.Id);
        Emit("melee", p.Position + p.Facing, [], "");
    }

    private void Magick(Wizard p, int slot)
    {
        if (!Ready(p) || slot is < 0 or > 3 || p.MagickCooldowns[slot] > 0) return;
        var magick = SpellBalance.Magicks[slot];
        if (p.Energy < magick.Cost) return;
        var target = new V2(p.Input.AimX, p.Input.AimY);
        if (slot == 0) p.Haste = 4;
        if (slot == 1)
        {
            // El recorrido se comprueba: no se puede saltar fuera del mapa ni cruzar muros.
            var direction = (target - p.Position).Normalized;
            float distance = Math.Min(7, V2.Distance(p.Position, target));
            V2 last = p.Position;
            for (float d = 0.3f; d <= distance; d += 0.3f)
            {
                var candidate = p.Position + direction * d;
                if (!Walkable(candidate)) break;
                last = candidate;
            }
            if (V2.Distance(last, p.Position) < 0.4f) return;
            Emit("blink", p.Position, [], "");
            p.Position = last; p.Facing = direction; p.Input.MoveTo = false;
            ResetSlide(p);
            _routes.Remove(p.Id);
            Emit("blink_arrive", p.Position, [], "");
        }
        if (slot == 2)
        {
            var direction = target - p.Position;
            if (direction.Length > 14) target = p.Position + direction.Normalized * 14;
            if (!Map.Inside(target) || _fields.Count >= 40) return;
            _fields.Add(new Field { Id = _nextEntity++, Owner = p.Id, Position = target, Radius = SpellBalance.MeteorRadius,
                Lifetime = SpellBalance.MeteorLifetime, Delay = SpellBalance.MeteorDelay, Kind = "meteor",
                Recipe = new SpellRecipe([Element.Earth, Element.Fire, Element.Fire]) });
        }
        if (slot == 3)
        {
            var ally = _players.Values.Where(q => !q.Alive && Allied(p, q) && q.Id != p.Id && V2.Distance(q.Position, p.Position) < 8)
                .OrderBy(q => V2.Distance(q.Position, p.Position)).FirstOrDefault();
            if (ally == null) return;
            var location = ally.Position;
            Respawn(ally); if (Walkable(location)) ally.Position = location; ally.Health = 600;
        }
        p.Energy -= magick.Cost; p.MagickCooldowns[slot] = magick.Cooldown;
        p.ActionCooldown = 0.2f;
        Emit("magick", p.Position, [], magick.Name);
    }

    private void Hit(Wizard target, SpellRecipe spell, float scale, int owner, V2 force, float damageMultiplier = 1)
    {
        if (!target.Alive) return;
        if (spell.Healing) target.Health = Math.Min(target.MaxHealth, target.Health + SpellBalance.Heal * spell.Count(Element.Life) * scale * HealingPower(owner));
        if (target.Invulnerable > 0) return;
        if (_players.TryGetValue(owner, out var source) && source.Id != target.Id && Allied(source, target) && !Options.FriendlyFire) return;
        float damage = 0;
        foreach (var element in SpellBalance.Offensive)
            damage += ElementDamage(target, owner, element, SpellBalance.Component(spell, element, target.Wet > 0));
        if (spell.Has(Element.Water) && !spell.Steam && !spell.Ice && Resistance(target, Element.Water) > 0.5f)
        {
            target.Wet = 6; target.Burning = 0;
            target.Knockback += force * (18 * scale);
        }
        if (spell.Shape == SpellShape.Force) target.Knockback += force * (45 * scale);
        if (spell.Has(Element.Fire) && !spell.Steam && Resistance(target, Element.Fire) > 0.5f)
        {
            if (target.Wet > 0) target.Wet = Math.Max(0, target.Wet - 12 * scale);
            else { target.Burning = SpellBalance.BurnDuration; target.BurnOwner = owner; target.BurnMultiplier = Outgoing(owner, Element.Fire); }
            target.Chill = Math.Max(0, target.Chill - 100 * scale);
            target.Frozen = Math.Max(0, target.Frozen - 5 * scale);
        }
        if (spell.Has(Element.Cold) && Resistance(target, Element.Cold) > 0.5f)
        {
            target.Burning = 0;
            target.Chill = Math.Min(100, target.Chill + 38 * spell.Count(Element.Cold) * (target.Wet > 0 ? 2 : 1) * scale);
            if (target.Chill >= 99) { target.Frozen = 1.4f; target.Wet = 0; }
        }
        if (spell.Has(Element.Earth)) target.Knockback += force * (10 * scale);
        damage *= damageMultiplier;
        Damage(target, damage * scale, owner, spell.Elements);
        if (_players.TryGetValue(owner, out var caster) && caster.Id != target.Id)
            caster.Energy = Math.Min(100, caster.Energy + damage * scale * 0.015f);
    }

    private void Damage(Wizard target, float amount, int owner, Element[]? elements = null)
    {
        if (!target.Alive || target.Invulnerable > 0 || amount <= 0) return;
        amount *= DamagePower(owner) * RelicBuffs.Incoming(target);
        float absorbed = Math.Min(target.Shield, amount);
        target.Shield -= absorbed; amount -= absorbed;
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
        target.Deaths++; target.Respawn = Map.RespawnSeconds(Options.Mode, target.Deaths);
        RelicBuffs.Clear(target);
        CancelCast(target); target.Selection.Clear(); target.Shield = target.ShieldMax = target.ShieldTime = target.RegenerationRate = 0;
        if (Options.Mode == GameMode.Warfare && !HoldToWin && !ConquestToWin) Scores[target.Team] = Math.Max(0, Scores[target.Team] - 1);
        string text = $"{target.Name} ha caído";
        if (_players.TryGetValue(owner, out var killer) && killer.Id != target.Id)
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
            foreach (var w in _walls.Where(w => w.Health > 0))
                if (Geometry.SegmentCapsule(from, to, w.A, w.B, projectile.Radius + 0.2f) is float t && t < nearest)
                { nearest = t; wallHit = w; obstacleHit = false; target = null; }
            foreach (var q in _players.Values.Where(q => q.Alive && q.Id != projectile.Owner))
                if (Geometry.SegmentCircle(from, to, q.Position, ArenaMap.WizardRadius + projectile.Radius) is float t && t < nearest)
                { nearest = t; target = q; obstacleHit = false; wallHit = null; }
            projectile.Position = V2.Lerp(from, to, nearest);
            if (target != null)
            {
                Hit(target, projectile.Recipe, projectile.Power, projectile.Owner, projectile.Velocity.Normalized);
                projectile.Lifetime = 0;
            }
            if (wallHit != null)
            {
                wallHit.Health -= 170 * projectile.Power * projectile.Recipe.Power * DamagePower(projectile.Owner);
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
            if (w.Recipe.Has(Element.Fire) || w.Recipe.Has(Element.Cold) || w.Recipe.Has(Element.Lightning))
                foreach (var p in _players.Values.Where(p => p.Alive && Geometry.DistanceToSegment(p.Position, w.A, w.B) < 1))
                    Hit(p, w.Recipe, dt, w.Owner, (p.Position - (w.A + w.B) / 2).Normalized);
        }
        _walls.RemoveAll(w => w.Lifetime <= 0 || w.Health <= 0);
        foreach (var f in _fields)
        {
            f.Lifetime -= dt; f.Delay -= dt;
            if (f.Lifetime <= 0 || f.Delay > 0) continue;
            if (f.Kind is "burning_ground" or "frost_ground" or "water_pool")
            {
                foreach (var p in _players.Values.Where(p => p.Alive && V2.Distance(p.Position, f.Position) < f.Radius && Visible(f.Position, p.Position)))
                    Hit(p, f.Recipe, dt * SpellBalance.GroundPower, f.Owner, (p.Position - f.Position).Normalized);
                continue;
            }
            bool trigger = f.Kind == "meteor" || _players.Values.Any(p => EligibleForMine(p, f) && V2.Distance(p.Position, f.Position) < f.TriggerRadius);
            if (!trigger) continue;
            foreach (var p in _players.Values.Where(p => p.Alive && V2.Distance(p.Position, f.Position) < f.Radius && Visible(f.Position, p.Position)))
            {
                if (f.Kind == "healing_mine")
                {
                    if (_players.TryGetValue(f.Owner, out var owner) && Allied(owner, p))
                        p.Health = Math.Min(p.MaxHealth, p.Health + SpellBalance.MineHeal * f.Recipe.Count(Element.Life) * RelicBuffs.Healing(owner));
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
            int blue = _players.Values.Count(p => p.Alive && p.Team == 0 && V2.Distance(p.Position, point.Position) < 3);
            int red = _players.Values.Count(p => p.Alive && p.Team == 1 && V2.Distance(p.Position, point.Position) < 3);
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
        Scores[0] = Scores[1] = Options.Mode == GameMode.Warfare && !ConquestToWin ? Options.Tickets : 0;
        _ticketClock = 0;
        _holdTicks = 0; HoldingTeam = -1; _routes.Clear();
    }
    private void ResetMatch()
    {
        MatchEnded = false; Winner = -1; RestartIn = 0;
        ResetScores(); _projectiles.Clear(); _walls.Clear(); _fields.Clear();
        ResetFrontline();
        foreach (var point in _points) { point.Owner = -1; point.Progress = 0; point.Contested = false; }
        foreach (var item in _pickups) { item.Respawn = 0; if (Map.RandomPickups) RelocatePickup(item); }
        foreach (var p in _players.Values) { p.Kills = p.Deaths = 0; RelicBuffs.Clear(p); p.MaxHealth = Wizard.BaseMaxHealth; Respawn(p); p.Energy = 100; Array.Clear(p.MagickCooldowns); }
        Emit("restart", V2.Zero, [], "Nueva partida");
    }
    private void Respawn(Wizard p)
    {
        _routes.Remove(p.Id);
        ResetSlide(p);
        p.Position = RespawnPosition(p); p.Health = p.MaxHealth;
        p.Wet = p.Burning = p.Chill = p.Frozen = p.Haste = p.Shield = p.ShieldMax = p.ShieldTime = 0;
        p.RegenerationRate = 0;
        p.Ward = []; p.Knockback = V2.Zero; p.Selection.Clear(); CancelCast(p);
        p.BurnMultiplier = 1; p.HitEffectCooldown = 0;
        p.TeleportCooldown = 0;
        p.TeleportNeedsExit = false;
        p.Input.MoveTo = false; p.Input.MoveX = p.Input.MoveY = 0; p.Respawn = 0;
        p.ShieldCharges = 1;
        p.Invulnerable = Options.Mode == GameMode.Arena ? 1 : 1.2f; p.ActionCooldown = 0;
    }

    private V2 RespawnPosition(Wizard p)
    {
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
            goal = (ConquestToWin ? _points.Where(q => q.Id == FrontierTarget(p.Team)) : _points.Where(q => q.Owner != p.Team || q.Contested))
                .OrderBy(q => V2.Distance(q.Position, p.Position)).FirstOrDefault()?.Position
                ?? _points[p.Id % _points.Length].Position;
        float distance = target == null ? 100 : V2.Distance(p.Position, target.Position);
        if (_pickups.Length > 0 && (target == null || distance > 9))
        {
            var item = _pickups.Where(i => i.Respawn <= 0).OrderBy(i => V2.Distance(p.Position, i.Position)).FirstOrDefault();
            if (item != null && V2.Distance(p.Position, item.Position) < 12) goal = item.Position;
        }
        var direction = (goal - p.Position).Normalized;
        if (target != null && distance < 8) direction = direction.Perpendicular * MathF.Sin((float)Time * 0.65f + p.Id);
        if (target != null && distance < 3.5f) direction = (p.Position - target.Position).Normalized;
        if (Map.Id != ArenaMapId.Classic && (target == null || distance > 12 || !Visible(p.Position, target.Position) || !Walkable(p.Position + direction * 1.4f)))
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
        if (ConquestToWin && (target == null || distance > 12 || !Visible(p.Position, target.Position)) && TryAttackFrontlineWall(p, goal)) return;
        if (target == null || distance > 18 || !Visible(p.Position, target.Position)) return;
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

    /// <summary>Copia inmutable del estado. La red la serializa fuera del bloqueo de la simulación.</summary>
    public WorldSnapshot Snapshot() => new(Tick, Time, Options.Mode, MatchEnded, Winner, Math.Max(0, RestartIn), Scores.ToArray(),
        _players.Values.Select(p => new WizardSnapshot(p.Id, p.Name, p.Team, p.Bot, p.Position, p.Facing, p.Health,
            p.MaxHealth, p.Shield, p.Ward.ToArray(), p.Selection.Elements.ToArray(), p.Alive, Math.Max(0, p.Respawn),
            p.Wet, p.Burning, p.Chill, p.Frozen, p.Haste, p.Energy, p.MagickCooldowns.ToArray(), p.Kills, p.Deaths,
            p.Charging != null ? p.ChargeTime / SpellBalance.ChargeSeconds : p.BeamCharge,
            p.Channel != null || p.Charging != null, p.ShieldMax, p.Shield > 0 ? p.ShieldTime : 0, p.WizardType, p.RegenerationRate * RelicBuffs.Healing(p),
            RelicBuffs.Snapshot(p), (p.Charging?.Elements ?? p.Channel?.Elements ?? []).ToArray(),
            p.ShieldCharges, p.Alive ? Math.Max(0, p.Invulnerable) : 0)).ToArray(),
        _projectiles.Select(p => new ProjectileSnapshot(p.Id, p.Owner, p.Position, p.Velocity.Normalized, p.Recipe.Elements.ToArray(), p.Radius)).ToArray(),
        _walls.Select(w => new WallSnapshot(w.Id, w.Owner, w.A, w.B, w.Health, w.Lifetime, w.Recipe.Elements.ToArray())).ToArray(),
        _fields.Select(f => new FieldSnapshot(f.Id, f.Owner, f.Position, f.Radius, f.Lifetime, f.Recipe.Elements.ToArray(), f.Kind, f.TriggerRadius, f.Delay <= 0)).ToArray(),
        _beams.Select(b => b with { Elements = b.Elements.ToArray() }).ToArray(), _points.Select(p => new PointSnapshot(p.Id, p.Position, p.Owner, p.Progress, p.Contested)).ToArray(),
        _events.Select(e => e.Event with { Elements = e.Event.Elements.ToArray() }).ToArray(), Options.ScoreLimit, Options.Tickets,
        Map.Id, HoldingTeam, HoldSeconds, _pickups.Select(i => new PickupSnapshot(i.Id, i.Position, i.Kind, i.Respawn <= 0, Math.Max(0, i.Respawn))).ToArray());
}
