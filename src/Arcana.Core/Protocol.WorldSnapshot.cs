using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

public sealed record WizardSnapshot(int Id, string Name, int Team, bool Bot, V2 Position,
    V2 Facing, float Health, float MaxHealth, float Shield, Element[] Ward, Element[] Selected,
    bool Alive, float Respawn, float Wet, float Burning, float Chill, float Frozen, float Haste,
    float Energy, float[] Cooldowns, int Kills, int Deaths, float Charge, bool Casting,
    float ShieldMax = 0, float ShieldSeconds = 0, WizardType WizardType = WizardType.Balanced, float RegenerationRate = 0,
    BuffSnapshot[]? Buffs = null, Element[]? CastingElements = null, int ShieldCharges = 1, float InvulnerableSeconds = 0,
    EquipmentAppearance? Equipment = null, float ReanimationCost = 60,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long SelectionSequence = 0)
{
    [JsonIgnore] public Element[] DisplayElements => Selected.Length > 0 ? Selected : CastingElements ?? [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MatchDamageSnapshot? Damage { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool ArcaneMarked { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Stunned { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SpellInterruption? CastInterruption { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool WaterSlowed { get; init; }
}

public sealed record BuffSnapshot(BuffKind Kind, int Stacks);

public sealed record PickupSnapshot(int Id, V2 Position, BuffKind Kind, bool Active, float Respawn);

public sealed record ProjectileSnapshot(int Id, int Owner, V2 Position, V2 Direction,
    Element[] Elements, float Radius);

public sealed record WallSnapshot(int Id, int Owner, V2 A, V2 B, float Health, float Lifetime, Element[] Elements);

public sealed record FieldSnapshot(int Id, int Owner, V2 Position, float Radius, float Lifetime, Element[] Elements, string Kind,
    float TriggerRadius = 0.85f, bool Armed = true);

public sealed record BeamSnapshot(int Owner, V2 From, V2 To, Element[] Elements, SpellShape Shape,
    float Power = 1, string Interaction = "direct");

public sealed record PointSnapshot(int Id, V2 Position, int Owner, float Progress, bool Contested = false);

public sealed record GameEvent(long Id, string Kind, V2 Position, Element[] Elements, string Text, float Radius = 0)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int ActorId { get; init; }
}

public sealed record WorldSnapshot(long Tick, double Time, GameMode Mode, bool MatchEnded,
    int Winner, float RestartIn, int[] Scores, WizardSnapshot[] Players, ProjectileSnapshot[] Projectiles,
    WallSnapshot[] Walls, FieldSnapshot[] Fields, BeamSnapshot[] Beams, PointSnapshot[] Points, GameEvent[] Events,
    int ScoreLimit = 15, int StartingTickets = 75, ArenaMapId Map = ArenaMapId.Classic,
    int HoldingTeam = -1, float HoldSeconds = 0, PickupSnapshot[]? Pickups = null,
    bool WaitingForPlayers = false, int LobbyHostId = 0, int[]? LobbyReadyPlayerIds = null,
    int BestOf = 1, int[]? SeriesWins = null, string RewardStatus = "", bool AwaitingRematch = false, DefenseSnapshot? Defense = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SurvivalMapSpec? SurvivalMap = null)
{
    // Metadato opcional: evita unir el informe de una ronda con jugadores de la siguiente.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StatisticsRunId { get; init; }
}
