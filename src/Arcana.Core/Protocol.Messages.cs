using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

// Un mensaje contiene una intención, nunca daño, vida ni una posición impuesta por el cliente.
public sealed class ClientMessage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool CompactActions { get; set; }
    public string Type { get; set; } = "";
    public long Sequence { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long PingId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long InspectionRequestId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int InspectPlayerId { get; set; }
    public int Protocol { get; set; } = Core.Protocol.Version;
    public string GameVersion { get; set; } = BuildInfo.Version;
    public string Name { get; set; } = "Mago";
    public string Password { get; set; } = "";
    public string RoomId { get; set; } = "default";
    public string RoomPassword { get; set; } = "";
    public int Team { get; set; } = -1;
    public WizardType WizardType { get; set; }
    public string AccountToken { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string ResumeToken { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BaselineId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float FrameTime95 { get; set; }
    public Element Element { get; set; }
    public int Slot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public float FramesPerSecond { get; set; }
    public string ItemId { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public int DefenseBotId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DefenseUpgradeFromLevel { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EquipmentLoadout? Loadout { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EquipmentStyle? AutoSellStyle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SurvivalEquipmentSetup? SurvivalEquipmentSetup { get; set; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public bool MoveTo { get; set; }
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float AimX { get; set; }
    public float AimY { get; set; }
}

public sealed record ServerInfo(string Name, int Port, int DiscoveryPort, GameMode Mode,
    int Players, int MaxPlayers, int Protocol, bool PasswordRequired, string? InstanceId = null, string? GameVersion = null, bool RewardsAvailable = false);

public sealed record DiscoveryReply(string Magic, string Nonce, ServerInfo Server);

public sealed class ServerMessage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool CompactActions { get; set; }
    public string Type { get; set; } = "";
    public int PlayerId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long PingId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long BaselineId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string ResumeToken { get; set; } = "";
    public string Error { get; set; } = "";
    public ServerInfo? Server { get; set; }
    public RoomInfo? Room { get; set; }
    public WorldSnapshot? Snapshot { get; set; }
    public bool RequiresReady { get; set; }
    public int PreparingPlayers { get; set; }
    public int[]? ReadyPlayerIds { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AccountNotice { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public long InspectionRequestId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EquipmentInspection? Inspection { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MatchStatisticsReport? Statistics { get; set; }
}
