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
    public ArenaDefinition Map { get; private set; }
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
        Map = ArenaMap.Get(Options.Map, Options.Mode == GameMode.Defense
            ? Options.SurvivalMap ?? SurvivalMapSpec.New() : null);
        if (!Map.SupportsMode(Options.Mode)) throw new ArgumentException("El modo no es compatible con el mapa.", nameof(options));
        if (!Enum.IsDefined(Options.DefenseFlow) || IsDefense && Options.MaxPlayers is < 1 or > DefenseRules.MaxDefenders)
            throw new ArgumentException("Defensa admite entre uno y cuatro defensores.", nameof(options));
        if (!Map.IsPractice) Map.PrepareNavigation();
        _random = IsDefense ? new SurvivalRandom(Options.Seed) : new Random(Options.Seed);
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
            _statistics.Bind(opponent, spawn: false);
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
        _statistics.Bind(wizard);
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
        _statistics.Bind(wizard, spawn: false);
        wizard.Position = TeamSpawn(team, _players.Values.Count(p => p.Team == team) - 1);
        wizard.Facing = new(team == 0 ? 1 : -1, 0);
        CancelCast(wizard);
        return true;
    }

    public void RemovePlayer(int id)
    {
        if (!_players.Remove(id, out var wizard)) return;
        _unavailableDefenders.Remove(id);
        ClearArcaneTarget(wizard); RemoveArcaneOwner(id);
        PruneLightningContacts(id);
        _orderedPlayers.Remove(wizard); _combatants.Remove(id); _defenders.Remove(id);
        _statistics.Retire(id); _statisticsPerformanceTime.Remove(id);
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
        CheckAutomaticRockEquipment(player);
        player.PublicEquipment = PublicEquipment.Empty;
    }

    public void EquipPlayer(int id, PreparedEquipment equipment)
    {
        EquipPlayer(id, equipment.Modifiers);
        if (!IsDefense) _players[id].PublicEquipment = equipment.View;
    }

    private void Emit(string kind, V2 position, Element[] elements, string text, float radius = 0, int actorId = 0)
    {
        _events.Add((Tick, new(_nextEvent++, kind, position, elements, text, radius) { ActorId = actorId }));
        if (_events.Count > 160) _events.RemoveRange(0, _events.Count - 160);
    }

}
