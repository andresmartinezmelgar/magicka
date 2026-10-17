using System.Text.Json.Nodes;

namespace Arcana.Core;

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

    public byte[] Encode(ServerMessage current, out byte[]? keyframe)
    {
        lock (_gate)
        {
            _frames++;
            if (_pending == null && _frames >= 40)
                _pending = new ServerMessage { Type = "keyframe", BaselineId = _id + 1, Snapshot = current.Snapshot,
                    PreparingPlayers = current.PreparingPlayers, ReadyPlayerIds = current.ReadyPlayerIds };
            // Si el keyframe se descarta o se pierde su confirmación, volver a enviarlo.
            keyframe = _pending != null && (_frames - 40) % 20 == 0 ? Protocol.SerializeSnapshot(_pending) : null;
            return Protocol.CompressSnapshotPayload(SnapshotDelta.Encode(Snapshot(current, true), _baselineJson, _baseline.Snapshot!.Tick, _id,
                current.Snapshot?.Defense is { } defense ? _minions.Encode(defense.Minions) : null));
        }
    }
}

public sealed class SnapshotReceiver
{
    private readonly Dictionary<long, ServerMessage> _baselines = [];
    private long _latestId = 1;
    public SnapshotReceiver(ServerMessage welcome) => _baselines[1] = SnapshotSender.Snapshot(welcome);
    public void Accept(ServerMessage keyframe)
    {
        if (keyframe.Snapshot == null || keyframe.BaselineId < 2 || keyframe.BaselineId > _latestId + 1)
            throw new InvalidDataException("Base de red inválida.");
        if (keyframe.BaselineId < _latestId) return;
        _latestId = keyframe.BaselineId;
        _baselines[_latestId] = SnapshotSender.Snapshot(keyframe);
        foreach (long id in _baselines.Keys.Where(id => id < _latestId - 1).ToArray()) _baselines.Remove(id);
    }
    public ServerMessage? Decode(ReadOnlySpan<byte> raw)
    {
        var node = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidDataException("Delta inválido.");
        long id = node["baseId"]?.GetValue<long>() ?? 1;
        return _baselines.TryGetValue(id, out var baseline) ? SnapshotDelta.Decode(raw, baseline) : null;
    }
}
