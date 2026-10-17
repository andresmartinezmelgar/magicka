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
        return Encode(current, Node(baseline), baseline.Snapshot.Tick, 1);
    }

    internal static byte[] Encode(ServerMessage current, JsonNode old, long tick, long baseId, byte[]? minions = null)
    {
        var envelope = new JsonObject { ["type"] = "delta", ["baseTick"] = tick, ["baseId"] = baseId,
            ["changes"] = Diff(old, Node(current)) ?? new JsonObject() };
        if (minions != null) envelope["minions"] = Convert.ToBase64String(minions);
        return Protocol.SerializeUtf8(envelope);
    }

    public static ServerMessage Decode(ReadOnlySpan<byte> data, ServerMessage baseline)
    {
        if (baseline.Snapshot == null) throw new InvalidDataException("Falta el keyframe.");
        var packet = JsonNode.Parse(data) as JsonObject ?? throw new InvalidDataException("Delta inválido.");
        if (packet["type"]?.GetValue<string>() != "delta" ||
            packet["baseTick"]?.GetValue<long>() != baseline.Snapshot.Tick)
            throw new InvalidDataException("Keyframe incompatible.");
        string? minionData = packet["minions"]?.GetValue<string>();
        var root = Node(minionData == null ? baseline : SnapshotSender.Snapshot(baseline, true));
        var patched = Merge(root, packet["changes"]) ?? throw new InvalidDataException("Delta vacío.");
        if (patched is JsonObject result) result["type"] = "snapshot";
        var message = Protocol.Deserialize<ServerMessage>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(patched, Protocol.Json))
            ?? throw new InvalidDataException("Delta no reconstruible.");
        if (minionData != null)
        {
            if (minionData.Length > 65536 || message.Snapshot?.Defense == null) throw new InvalidDataException("Enemigos fuera de Supervivencia.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(minionData); }
            catch (FormatException error) { throw new InvalidDataException("Estado de enemigos inválido.", error); }
            var minions = new MinionDeltaCodec(baseline.Snapshot.Defense?.Minions ?? []).Decode(bytes);
            message.Snapshot = message.Snapshot with { Defense = message.Snapshot.Defense with { Minions = minions } };
        }
        return message;
    }

    internal static JsonNode Node(ServerMessage message) =>
        System.Text.Json.JsonSerializer.SerializeToNode(message, Protocol.Json)
        ?? throw new InvalidDataException("Estado no serializable.");

    private static JsonNode? Diff(JsonNode? previous, JsonNode? current)
    {
        if (JsonNode.DeepEquals(previous, current))
            return null;
        if (previous is JsonObject a && current is JsonObject b)
        {
            var result = new JsonObject();
            foreach (var field in a)
                if (!b.ContainsKey(field.Key)) result[field.Key] = new JsonObject { ["$null"] = true };
            foreach (var field in b)
            {
                var changed = Diff(a[field.Key], field.Value);
                if (changed != null) result[field.Key] = changed;
            }
            return result;
        }
        if (previous is JsonArray entitiesBefore && current is JsonArray entitiesNow && entitiesBefore.Count > 0 && entitiesNow.Count > 0 &&
            EntityKeys(entitiesBefore) is { } beforeKeys && EntityKeys(entitiesNow) is { } nowKeys &&
            !beforeKeys.SequenceEqual(nowKeys))
        {
            var before = beforeKeys.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => entitiesBefore[p.index]);
            var updates = new JsonObject();
            for (int i = 0; i < nowKeys.Length; i++)
            {
                var change = Diff(before.GetValueOrDefault(nowKeys[i]), entitiesNow[i]);
                if (change != null) updates[nowKeys[i]] = change;
            }
            return new JsonObject { ["$entities"] = new JsonObject {
                ["order"] = new JsonArray(nowKeys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()), ["updates"] = updates } };
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

    private static string[]? EntityKeys(JsonArray array)
    {
        var keys = new string[array.Count]; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject obj) return null;
            var id = obj["id"] ?? obj["playerId"] ?? (obj["item"] as JsonObject)?["id"];
            if (id == null || !seen.Add(keys[i] = id.ToJsonString())) return null;
        }
        return keys;
    }

    private static JsonNode? Merge(JsonNode? previous, JsonNode? changes)
    {
        if (previous is JsonArray entities && changes is JsonObject entityPatch && entityPatch["$entities"] is JsonObject patch)
        {
            var ids = EntityKeys(entities) ?? throw new InvalidDataException("Colección sin identificadores.");
            var lookup = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => entities[p.index]);
            var order = patch["order"] as JsonArray ?? throw new InvalidDataException("Orden de entidades ausente.");
            if (order.Count > 4096) throw new InvalidDataException("Demasiadas entidades.");
            var updates = patch["updates"] as JsonObject ?? throw new InvalidDataException("Cambios de entidades ausentes.");
            var result = new JsonArray(); var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in order)
            {
                string key = entry?.GetValue<string>() ?? throw new InvalidDataException("Identificador ausente.");
                if (!seen.Add(key)) throw new InvalidDataException("Entidad repetida.");
                var value = lookup.GetValueOrDefault(key);
                result.Add(updates.TryGetPropertyValue(key, out var change) ? Merge(value?.DeepClone(), change) : value?.DeepClone());
            }
            return result;
        }
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
