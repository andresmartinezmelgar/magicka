using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Arcana.Core;

/// <summary>Reconstruye bytes exactos desde una base comprobada; nunca modifica la APK instalada.</summary>
public static class BinaryUpdateDelta
{
    public static void Apply(string basePath, string patchPath, string targetPath, string installedVersion,
        string targetVersion, byte[] expectedHash, long expectedBytes, CancellationToken token = default)
    {
        if (expectedBytes <= 0 || expectedBytes > ReleaseUpdatePlan.MaximumBytes || expectedHash.Length != 32)
            throw new InvalidDataException("Destino del parche inválido.");
        if (Path.GetFullPath(targetPath) == Path.GetFullPath(basePath) || Path.GetFullPath(targetPath) == Path.GetFullPath(patchPath))
            throw new InvalidDataException("El parche no puede sustituir su archivo base.");
        string temporary = targetPath + ".part";
        try
        {
            using var source = File.OpenRead(basePath);
            using var patch = File.OpenRead(patchPath);
            using var compressed = new GZipStream(patch, CompressionMode.Decompress);
            using var reader = new BinaryReader(compressed, new UTF8Encoding(false, true), leaveOpen: true);
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("ARCDLT01"u8)) throw new InvalidDataException("Formato de parche desconocido.");
            string baseline = ReadVersion(reader), target = ReadVersion(reader);
            long baseBytes = reader.ReadInt64(), targetBytes = reader.ReadInt64();
            byte[] baseHash = reader.ReadBytes(32), targetHash = reader.ReadBytes(32);
            int operations = reader.ReadInt32();
            if (baseline != installedVersion || target != targetVersion || baseBytes != source.Length ||
                targetBytes != expectedBytes || operations <= 0 || operations > 262144 ||
                baseHash.Length != 32 || targetHash.Length != 32 ||
                !CryptographicOperations.FixedTimeEquals(targetHash, expectedHash))
                throw new InvalidDataException("El parche no corresponde a esta versión.");
            byte[] buffer = new byte[65536];
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                int count;
                while ((count = source.Read(buffer)) != 0)
                { token.ThrowIfCancellationRequested(); hasher.AppendData(buffer, 0, count); }
                if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), baseHash))
                    throw new InvalidDataException("La APK instalada no coincide con la base del parche.");
            }
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
            using (var output = File.Create(temporary))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                long written = 0;
                for (int instruction = 0; instruction < operations; instruction++)
                {
                    token.ThrowIfCancellationRequested();
                    byte opcode = reader.ReadByte();
                    long offset = opcode == 0 ? reader.ReadInt64() : 0;
                    long length = reader.ReadInt64();
                    if (opcode > 1 || length <= 0 || length > expectedBytes - written ||
                        opcode == 0 && (offset < 0 || offset > source.Length || length > source.Length - offset))
                        throw new InvalidDataException("Instrucción de parche fuera de límites.");
                    if (opcode == 0) source.Position = offset;
                    long remaining = length;
                    while (remaining > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(buffer.Length, remaining);
                        if (opcode == 0) source.ReadExactly(buffer.AsSpan(0, count));
                        else compressed.ReadExactly(buffer.AsSpan(0, count));
                        output.Write(buffer, 0, count);
                        hasher.AppendData(buffer, 0, count);
                        written += count; remaining -= count;
                    }
                }
                if (written != expectedBytes || compressed.ReadByte() != -1 ||
                    !CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), expectedHash))
                    throw new InvalidDataException("La APK reconstruida no coincide con la Release firmada.");
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, targetPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ReadVersion(BinaryReader reader)
    {
        int length = reader.ReadUInt16();
        if (length is < 1 or > 40) throw new InvalidDataException("Versión de parche inválida.");
        byte[] raw = reader.ReadBytes(length);
        if (raw.Length != length) throw new EndOfStreamException();
        return new UTF8Encoding(false, true).GetString(raw);
    }
}
