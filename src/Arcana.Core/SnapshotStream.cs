using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcana.Core;

/// <summary>Prepara una vista una vez; el llamante mantiene el snapshot inmutable durante su difusión.</summary>
public sealed class PreparedSnapshotFrame
{
    internal ServerMessage Message { get; }
    internal JsonNode Json { get; }

    public PreparedSnapshotFrame(ServerMessage message)
    {
        Message = SnapshotSender.Snapshot(message);
        Json = SnapshotDelta.Node(SnapshotSender.Snapshot(Message, true));
    }
}

/// <summary>Bases confirmadas y deltas independientes: descartar una trama nunca invalida la siguiente.</summary>
public sealed class SnapshotSender
{
    private readonly object _gate = new();
    private ServerMessage _baseline;
    private JsonNode _baselineJson;
    private MinionDeltaCodec _minions;
    private ServerMessage? _pending;
    private long _id = 1;
    private int _frames;
    public SnapshotSender(ServerMessage welcome)
    { _baseline = Snapshot(welcome); _baselineJson = SnapshotDelta.Node(Snapshot(_baseline, true)); _minions = new(_baseline.Snapshot?.Defense?.Minions ?? []); }

    internal static ServerMessage Snapshot(ServerMessage frame, bool excludeMinions = false) => new() { Type = "snapshot",
        Snapshot = excludeMinions && frame.Snapshot?.Defense is { } d ? frame.Snapshot with { Defense = d with { Minions = [] } } : frame.Snapshot,
        PreparingPlayers = frame.PreparingPlayers, ReadyPlayerIds = frame.ReadyPlayerIds };

    public void Acknowledge(long id)
    {
        lock (_gate)
            if (_pending?.BaselineId == id)
            { _baseline = Snapshot(_pending); _baselineJson = SnapshotDelta.Node(Snapshot(_baseline, true)); _minions = new(_baseline.Snapshot!.Defense?.Minions ?? []); _id = id; _pending = null; _frames = 0; }
    }

    public byte[] Encode(ServerMessage current, out byte[]? keyframe) => EncodePrepared(new(current), out keyframe);

    public byte[] EncodePrepared(PreparedSnapshotFrame prepared, out byte[]? keyframe)
    {
        lock (_gate)
        {
            var current = prepared.Message;
            _frames++;
            if (_pending == null && _frames >= 40)
                _pending = new ServerMessage { Type = "keyframe", BaselineId = _id + 1, Snapshot = current.Snapshot,
                    PreparingPlayers = current.PreparingPlayers, ReadyPlayerIds = current.ReadyPlayerIds };
            // Si el keyframe se descarta o se pierde su confirmación, volver a enviarlo.
            keyframe = _pending != null && (_frames - 40) % 20 == 0 ? Protocol.SerializeSnapshot(_pending) : null;
            return Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(prepared.Json, _baselineJson, _baseline.Snapshot!.Tick, _id,
                current.Snapshot?.Defense is { } defense ? _minions.Encode(defense.Minions) : null));
        }
    }
}

public sealed class SnapshotReceiver
{
    private sealed class Baseline(ServerMessage message)
    {
        private JsonNode? _fullJson, _withoutMinionsJson;
        private MinionDeltaCodec? _minions;

        internal ServerMessage Decode(JsonObject packet)
        {
            bool hasMinions = packet["minions"] != null;
            var json = hasMinions
                ? (_withoutMinionsJson ??= SnapshotDelta.Node(SnapshotSender.Snapshot(message, true)))
                : (_fullJson ??= SnapshotDelta.Node(message));
            if (hasMinions) _minions ??= new(message.Snapshot?.Defense?.Minions ?? []);
            return SnapshotDelta.Decode(packet, message, json, _minions);
        }
    }

    private readonly Dictionary<long, Baseline> _baselines = [];
    private long _latestId = 1;
    public SnapshotReceiver(ServerMessage welcome) => _baselines[1] = new(SnapshotSender.Snapshot(welcome));

    /// <summary>Lee el discriminante sin construir un ServerMessage ni un árbol del contenido.</summary>
    public static string? ReadMessageType(ReadOnlySpan<byte> raw)
    {
        var reader = new Utf8JsonReader(raw, new JsonReaderOptions { MaxDepth = Protocol.Json.MaxDepth });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Respuesta de red inválida.");
        string? type = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (reader.Read()) throw new JsonException("Datos adicionales en la respuesta.");
                return type;
            }
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Campo de respuesta inválido.");
            bool isType = reader.ValueTextEquals("type"u8);
            if (!reader.Read()) throw new JsonException("Respuesta de red incompleta.");
            if (isType)
            {
                if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))
                    throw new JsonException("Tipo de respuesta inválido.");
                // Igual que System.Text.Json: si un campo se repite, prevalece el último.
                type = reader.GetString();
            }
            else reader.Skip();
        }
        throw new JsonException("Respuesta de red incompleta.");
    }

    public void Accept(ServerMessage keyframe)
    {
        if (keyframe.Snapshot == null || keyframe.BaselineId < 2 || keyframe.BaselineId > _latestId + 1)
            throw new InvalidDataException("Base de red inválida.");
        if (keyframe.BaselineId < _latestId) return;
        _latestId = keyframe.BaselineId;
        _baselines[_latestId] = new(SnapshotSender.Snapshot(keyframe));
        // Las bases solo avanzan de una en una. Basta retirar la anterior a las dos conservadas.
        _baselines.Remove(_latestId - 2);
    }
    public ServerMessage? Decode(ReadOnlySpan<byte> raw)
    {
        var node = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException("Delta inválido.");
        long id = node["baseId"]?.GetValue<long>() ?? 1;
        return _baselines.TryGetValue(id, out var baseline) ? baseline.Decode(node) : null;
    }
}
