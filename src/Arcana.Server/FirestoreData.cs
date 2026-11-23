using System.Text.Json;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

internal static class FirestoreData
{
    public static Dictionary<string, object> Map<T>(T value) => (Dictionary<string, object>)Convert(JsonSerializer.SerializeToElement(value, Protocol.Json));
    private static object Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToArray(),
        JsonValueKind.String => value.GetString()!, JsonValueKind.True => true, JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out long n) ? (object)n : value.GetDouble(), _ => null!
    };
    public static T Read<T>(DocumentSnapshot snapshot) => Protocol.Deserialize<T>(JsonSerializer.Serialize(snapshot.ToDictionary(), Protocol.Json))
        ?? throw new InvalidDataException("Documento de cuenta inválido.");
}
