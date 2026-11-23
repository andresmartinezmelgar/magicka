using Arcana.Core;
using System.Text.Json;

namespace Arcana.Client;

/// <summary>Firestore REST documents used by the isolated shop and inventory fixtures.</summary>
internal static class FirestoreFixture
{
    public static object Document(object value) => new { fields = JsonSerializer.SerializeToElement(value, Protocol.Json).EnumerateObject().ToDictionary(p => p.Name, p => FirestoreValue(p.Value)) };
    private static object FirestoreValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new { stringValue = value.GetString() },
        JsonValueKind.Number when value.TryGetInt64(out long integer) => new { integerValue = integer.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        JsonValueKind.Number => new { doubleValue = value.GetDouble() },
        JsonValueKind.Array => new { arrayValue = new { values = value.EnumerateArray().Select(FirestoreValue).ToArray() } },
        JsonValueKind.Object => new { mapValue = new { fields = value.EnumerateObject().ToDictionary(p => p.Name, p => FirestoreValue(p.Value)) } },
        _ => new { nullValue = (object?)null }
    };
}
