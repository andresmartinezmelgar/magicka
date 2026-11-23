using System.Text.Json;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Buffers;

namespace Arcana.Core;

public static partial class Protocol
{
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

    public static byte[] CompressSnapshotPayload(byte[] raw, int quality = 4)
    {
        // Los deltas pequeños de PvP también contienen nombres/campos repetidos.
        // Mantener el filtro de ahorro: nunca enviar Brotli si no ahorra al menos 17 bytes.
        if (raw.Length < MinSnapshotCompressionBytes) return raw;
        // La trama conserva el prefijo y el decodificador de las versiones anteriores.
        int length = BrotliEncoder.GetMaxCompressedLength(raw.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length + 1);
        try
        {
            buffer[0] = 0xA1;
            if (BrotliEncoder.TryCompress(raw, buffer.AsSpan(1, length), out int written, quality, window: 22) && written + 17 < raw.Length)
                return buffer.AsSpan(0, written + 1).ToArray();
            return raw;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public static byte[] SerializeSnapshot(ServerMessage message)
    {
        if (message.Type != "keyframe") return CompressSnapshotPayload(SerializeUtf8(message));
        var basis = new { message.Type, message.BaselineId, message.Snapshot, message.PreparingPlayers, message.ReadyPlayerIds };
        return CompressSnapshotPayload(SerializeUtf8(basis), quality: 5);
    }

    public static ServerMessage? DeserializeFrame(ReadOnlySpan<byte> bytes, bool binary)
    {
        if (!binary) return Deserialize<ServerMessage>(bytes);
        return Deserialize<ServerMessage>(DecompressSnapshotPayload(bytes));
    }
}
