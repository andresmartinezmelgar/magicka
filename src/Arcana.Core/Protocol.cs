using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

public static class Protocol
{
    public const int Version = 46;
    public const int GamePort = 27015;
    public const int DiscoveryPort = 27016;
    public const string DiscoveryMagic = "ARCANA_DISCOVER_V30";
    public const int LoadingTimeoutSeconds = 120;
    public const int MaxMessageBytes = 4096;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        MaxDepth = 32
    };
    // Los mensajes de combate no incluyen datos del saludo ni campos de otras acciones.
    // Los nombres se mantienen para que el servidor pueda deserializar con el protocolo actual.
    private static readonly string[] BinaryActionNames =
        ["input", "element", "magick", "inspect", "cast", "release", "clear", "melee", "self", "nova", "ready"];
    private static short QuantizeAxis(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Eje no finito.");
        return (short)MathF.Round(Math.Clamp(value, -1f, 1f) * 32767f);
    }
    public static byte[] SerializeBinaryAction(ClientMessage message)
    {
        int code = Array.IndexOf(BinaryActionNames, message.Type);
        if (code < 0) return SerializeAction(message);
        using var stream = new MemoryStream(48);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0xB1); writer.Write((byte)code); writer.Write(message.Sequence);
        switch (message.Type)
        {
            case "input":
                writer.Write(QuantizeAxis(message.MoveX)); writer.Write(QuantizeAxis(message.MoveY)); writer.Write(message.MoveTo);
                if (message.MoveTo) { writer.Write(message.TargetX); writer.Write(message.TargetY); }
                writer.Write(message.AimX); writer.Write(message.AimY); break;
            case "element": writer.Write((byte)message.Element); break;
            case "magick": writer.Write((byte)message.Slot); break;
            case "inspect": writer.Write(message.InspectionRequestId); writer.Write(message.InspectPlayerId); break;
        }
        return stream.ToArray();
    }
    public static ClientMessage DeserializeBinaryAction(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 0xB1 || bytes[1] >= BinaryActionNames.Length)
            throw new InvalidDataException("Acción binaria inválida.");
        using var stream = new MemoryStream(bytes.ToArray(), false);
        using var reader = new BinaryReader(stream);
        reader.ReadByte();
        var message = new ClientMessage { Type = BinaryActionNames[reader.ReadByte()], Sequence = reader.ReadInt64() };
        try
        {
            switch (message.Type)
            {
                case "input":
                    message.MoveX = reader.ReadInt16() / 32767f; message.MoveY = reader.ReadInt16() / 32767f;
                    message.MoveTo = reader.ReadBoolean();
                    if (message.MoveTo) { message.TargetX = reader.ReadSingle(); message.TargetY = reader.ReadSingle(); }
                    message.AimX = reader.ReadSingle();
                    message.AimY = reader.ReadSingle(); break;
                case "element": message.Element = (Element)reader.ReadByte(); break;
                case "magick": message.Slot = reader.ReadByte(); break;
                case "inspect": message.InspectionRequestId = reader.ReadInt64(); message.InspectPlayerId = reader.ReadInt32(); break;
            }
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Acción binaria truncada.", e); }
        if (stream.Position != stream.Length) throw new InvalidDataException("Acción binaria con datos adicionales.");
        return message;
    }
    public static byte[] SerializeAction(ClientMessage message) => message.Type switch
    {
        "ping" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, pingId = message.PingId }, Json),
        "baseline_ack" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, baselineId = message.BaselineId }, Json),
        "defense_performance" or "match_performance" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence,
            framesPerSecond = message.FramesPerSecond, frameTime95 = message.FrameTime95 }, Json),
        "lobby-team" => JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence, team = message.Team }, Json),
        "input" => SerializeInput(message),
        "element" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence, element = message.Element
        }, Json),
        "magick" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence, slot = message.Slot
        }, Json),
        "inspect" => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = message.Type, sequence = message.Sequence,
            inspectionRequestId = message.InspectionRequestId, inspectPlayerId = message.InspectPlayerId
        }, Json),
        "cast" or "release" or "clear" or "melee" or "self" or "nova" or "ready" or
        "statistics" or "lobby-ready" or "lobby-start" or "defense_restart" =>
            JsonSerializer.SerializeToUtf8Bytes(new { type = message.Type, sequence = message.Sequence }, Json),
        _ => SerializeUtf8(message) // Saludo y extensiones futuras conservan el formato completo.
    };
    public static byte[] SerializePong(long pingId) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = "pong", pingId }, Json);
    private static byte[] SerializeInput(ClientMessage message)
    {
        // Los ejes del joystick ya están normalizados; los objetivos se transmiten solo si están activos.
        // El apuntado sigue siendo preciso y no se redondea en esta fase.
        if (message.MoveTo)
            return JsonSerializer.SerializeToUtf8Bytes(new { type = "input", sequence = message.Sequence,
                moveX = message.MoveX, moveY = message.MoveY, moveTo = true,
                targetX = message.TargetX, targetY = message.TargetY,
                aimX = message.AimX, aimY = message.AimY }, Json);
        return JsonSerializer.SerializeToUtf8Bytes(new { type = "input", sequence = message.Sequence,
            moveX = message.MoveX, moveY = message.MoveY,
            aimX = message.AimX, aimY = message.AimY }, Json);
    }
    // Fotogramas comprimidos: prefijo 0xA1 + Brotli. El saludo permanece en JSON.
    // Límite descomprimido para impedir cargas excesivas de memoria.
    public const int MaxSnapshotBytes = 256 * 1024;
    public const int MinSnapshotCompressionBytes = 256;
    public static byte[] DecompressSnapshotPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0xA1)
            throw new InvalidDataException("Cabecera de compresión inválida.");
        using var input = new MemoryStream(data[1..].ToArray());
        using var decoder = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] chunk = new byte[8192];
        int n;
        while ((n = decoder.Read(chunk)) > 0)
        {
            if (output.Length + n > MaxSnapshotBytes)
                throw new InvalidDataException("Límite de descompresión superado.");
            output.Write(chunk, 0, n);
        }
        return output.ToArray();
    }
    public static byte[] CompressSnapshotPayload(byte[] raw)
    {
        // Los deltas pequeños de PvP también contienen nombres/campos repetidos.
        // Mantener el filtro de ahorro: nunca enviar Brotli si no ahorra al menos 17 bytes.
        if (raw.Length < MinSnapshotCompressionBytes) return raw;
        // Wire-compatible Brotli. Quality 3 reduces bytes without lowering snapshot frequency.
        int length = BrotliEncoder.GetMaxCompressedLength(raw.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length + 1);
        try
        {
            buffer[0] = 0xA1;
            if (BrotliEncoder.TryCompress(raw, buffer.AsSpan(1, length), out int written, quality: 3, window: 22) && written + 17 < raw.Length)
                return buffer.AsSpan(0, written + 1).ToArray();
            return raw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    public static byte[] SerializeSnapshot(ServerMessage message) => CompressSnapshotPayload(SerializeUtf8(message));
    public static ServerMessage? DeserializeFrame(ReadOnlySpan<byte> bytes, bool binary)
    {
        if (!binary) return Deserialize<ServerMessage>(bytes);
        return Deserialize<ServerMessage>(DecompressSnapshotPayload(bytes));
    }
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static byte[] SerializeUtf8<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);
    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Json);
}

// Un mensaje contiene una intención, nunca daño, vida ni una posición impuesta por el cliente.
public sealed class ClientMessage
{
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
