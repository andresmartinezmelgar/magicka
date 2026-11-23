using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

public static partial class Protocol
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
    // Fotogramas comprimidos: prefijo 0xA1 + Brotli. El saludo permanece en JSON.
    // Límite descomprimido para impedir cargas excesivas de memoria.
    public const int MaxSnapshotBytes = 256 * 1024;
    public const int MinSnapshotCompressionBytes = 96;
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    public static byte[] SerializeUtf8<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Json);
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);
    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Json);
}
