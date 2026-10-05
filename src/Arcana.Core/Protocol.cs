using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public static class Protocol
{
    public const int Version = 8;
    public const int GamePort = 27015;
    public const int DiscoveryPort = 27016;
    public const string DiscoveryMagic = "ARCANA_DISCOVER_V8";
    public const int MaxMessageBytes = 4096;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        MaxDepth = 16
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);
}

// Un mensaje contiene una intención, nunca daño, vida ni una posición impuesta por el cliente.
public sealed class ClientMessage
{
    public string Type { get; set; } = "";
    public long Sequence { get; set; }
    public int Protocol { get; set; } = Core.Protocol.Version;
    public string Name { get; set; } = "Mago";
    public string Password { get; set; } = "";
    public string RoomId { get; set; } = "default";
    public string RoomPassword { get; set; } = "";
    public int Team { get; set; } = -1;
    public WizardType WizardType { get; set; }
    public Element Element { get; set; }
    public int Slot { get; set; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public bool MoveTo { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float AimX { get; set; }
    public float AimY { get; set; }
}

public sealed record ServerInfo(string Name, int Port, int DiscoveryPort, GameMode Mode,
    int Players, int MaxPlayers, int Protocol, bool PasswordRequired, string? InstanceId = null);
public sealed record DiscoveryReply(string Magic, string Nonce, ServerInfo Server);
public sealed class ServerMessage
{
    public string Type { get; set; } = "";
    public int PlayerId { get; set; }
    public string Error { get; set; } = "";
    public ServerInfo? Server { get; set; }
    public RoomInfo? Room { get; set; }
    public WorldSnapshot? Snapshot { get; set; }
}

public sealed record WizardSnapshot(int Id, string Name, int Team, bool Bot, V2 Position,
    V2 Facing, float Health, float MaxHealth, float Shield, Element[] Ward, Element[] Selected,
    bool Alive, float Respawn, float Wet, float Burning, float Chill, float Frozen, float Haste,
    float Energy, float[] Cooldowns, int Kills, int Deaths, float Charge, bool Casting,
    float ShieldMax = 0, float ShieldSeconds = 0, WizardType WizardType = WizardType.Balanced, float RegenerationRate = 0,
    BuffSnapshot[]? Buffs = null, Element[]? CastingElements = null, int ShieldCharges = 1, float InvulnerableSeconds = 0)
{
    [JsonIgnore] public Element[] DisplayElements => Selected.Length > 0 ? Selected : CastingElements ?? [];
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
public sealed record GameEvent(long Id, string Kind, V2 Position, Element[] Elements, string Text, float Radius = 0);
public sealed record WorldSnapshot(long Tick, double Time, GameMode Mode, bool MatchEnded,
    int Winner, float RestartIn, int[] Scores, WizardSnapshot[] Players, ProjectileSnapshot[] Projectiles,
    WallSnapshot[] Walls, FieldSnapshot[] Fields, BeamSnapshot[] Beams, PointSnapshot[] Points, GameEvent[] Events,
    int ScoreLimit = 15, int StartingTickets = 75, ArenaMapId Map = ArenaMapId.Classic,
    int HoldingTeam = -1, float HoldSeconds = 0, PickupSnapshot[]? Pickups = null);
