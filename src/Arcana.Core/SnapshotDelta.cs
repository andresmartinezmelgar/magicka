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
        if (patched is JsonObject result) result["type"] = "snapshot";
        return Protocol.Deserialize<ServerMessage>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(patched, Protocol.Json))
            ?? throw new InvalidDataException("Delta no reconstruible.");
    }

    private static JsonNode JsonSerializerNode(ServerMessage message) =>
        System.Text.Json.JsonSerializer.SerializeToNode(message, Protocol.Json)
        ?? throw new InvalidDataException("Estado no serializable.");

    private static JsonNode? Diff(JsonNode? previous, JsonNode? current)
    {
        if (JsonNode.DeepEquals(previous, current))
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
        if (previous is JsonArray oldArray && current is JsonArray newArray &&
            oldArray.Count == newArray.Count)
        {
            var changes = new JsonObject();
            for (int i = 0; i < newArray.Count; i++)
            {
                var changed = Diff(oldArray[i], newArray[i]);
                if (changed != null) changes[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = changed;
            }
            return new JsonObject { ["$array"] = changes };
        }
        // Al cambiar el número de entidades, sustituir la colección completa.

        return current?.DeepClone() ?? new JsonObject { ["$null"] = true };
    }

    private static JsonNode? Merge(JsonNode? previous, JsonNode? changes)
    {
        if (changes is JsonObject nullMarker && nullMarker.Count == 1 && nullMarker["$null"]?.GetValue<bool>() == true) return null;
        if (previous is JsonArray array && changes is JsonObject arrayPatch &&
            arrayPatch["$array"] is JsonObject positions)
        {
            foreach (var position in positions)
            {
                int index = int.Parse(position.Key, System.Globalization.CultureInfo.InvariantCulture);
                if ((uint)index >= (uint)array.Count) throw new InvalidDataException("Índice de delta inválido.");
                array[index] = Merge(array[index]?.DeepClone(), position.Value);
            }
            return array;
        }
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
