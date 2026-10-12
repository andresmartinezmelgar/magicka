using System.Text.Json.Nodes;

namespace Arcana.Core;

/// <summary>
/// Deltas autocontenidos respecto a un keyframe de sala. Nunca se encadenan entre sí:
/// la pérdida de un snapshot descartable no corrompe los siguientes.
/// </summary>
public static class SnapshotDelta
{
    public static byte[] Encode(ServerMessage current, ServerMessage baseline)
    {
        if (current.Snapshot == null || baseline.Snapshot == null)
            throw new InvalidDataException("Snapshot requerido.");
        var now = JsonSerializerNode(current);
        var old = JsonSerializerNode(baseline);
        var changes = Diff(old, now);
        var envelope = new JsonObject
        {
            ["type"] = "delta",
            ["baseTick"] = baseline.Snapshot.Tick,
            ["changes"] = changes ?? new JsonObject()
        };
        return Protocol.SerializeUtf8(envelope);
    }

    public static ServerMessage Decode(ReadOnlySpan<byte> data, ServerMessage baseline)
    {
        if (baseline.Snapshot == null) throw new InvalidDataException("Falta el keyframe.");
        var packet = JsonNode.Parse(data) as JsonObject ?? throw new InvalidDataException("Delta inválido.");
        if (packet["type"]?.GetValue<string>() != "delta" ||
            packet["baseTick"]?.GetValue<long>() != baseline.Snapshot.Tick)
            throw new InvalidDataException("Keyframe incompatible.");
        var root = JsonSerializerNode(baseline);
        var patched = Merge(root, packet["changes"]) ?? throw new InvalidDataException("Delta vacío.");
        return Protocol.Deserialize<ServerMessage>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(patched, Protocol.Json))
            ?? throw new InvalidDataException("Delta no reconstruible.");
    }

    private static JsonNode JsonSerializerNode(ServerMessage message) =>
        System.Text.Json.JsonSerializer.SerializeToNode(message, Protocol.Json)
        ?? throw new InvalidDataException("Estado no serializable.");

    private static JsonNode? Diff(JsonNode? previous, JsonNode? current)
    {
        if (System.Text.Json.JsonSerializer.Serialize(previous) == System.Text.Json.JsonSerializer.Serialize(current))
            return null;
        if (previous is JsonObject a && current is JsonObject b)
        {
            var result = new JsonObject();
            foreach (var field in b)
            {
                var changed = Diff(a[field.Key], field.Value);
                if (changed != null) result[field.Key] = changed;
            }
            return result;
        }
        // Las colecciones se sustituyen enteras: evita diferencias ambiguas de índices.
        return current?.DeepClone() ?? new JsonObject { ["$null"] = true };
    }

    private static JsonNode? Merge(JsonNode? previous, JsonNode? changes)
    {
        if (previous is JsonObject a && changes is JsonObject b)
        {
            foreach (var field in b)
                a[field.Key] = Merge(a[field.Key]?.DeepClone(), field.Value);
            return a;
        }
        if (changes is JsonObject marker && marker.Count == 1 && marker["$null"]?.GetValue<bool>() == true) return null;
        return changes?.DeepClone();
    }
}
