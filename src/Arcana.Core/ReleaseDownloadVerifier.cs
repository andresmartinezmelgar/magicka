using System.Security.Cryptography;

namespace Arcana.Core;

/// <summary>Valida entradas de SHA256SUMS de Releases sin aceptar rutas ni duplicados.</summary>
public static class ReleaseDownloadVerifier
{
    public static byte[] ReadExpectedHash(string manifest, string assetFilename)
    {
        if (string.IsNullOrWhiteSpace(assetFilename) || assetFilename.IndexOfAny(['/', '\\', '\r', '\n']) >= 0 ||
            assetFilename.Length > 180) throw new InvalidDataException("Nombre de descarga inválido.");
        byte[]? expected = null;
        foreach (string line in manifest.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length < 67 || trimmed[64] != ' ' || trimmed[65] is not (' ' or '*')) continue;
            string file = trimmed[66..];
            if (!string.Equals(file, assetFilename, StringComparison.Ordinal)) continue;
            if (expected != null) throw new InvalidDataException("La lista de hashes contiene entradas duplicadas.");
            try { expected = Convert.FromHexString(trimmed[..64]); }
            catch (FormatException) { throw new InvalidDataException("Hash SHA-256 de la actualización inválido."); }
            if (expected.Length != SHA256.HashSizeInBytes)
                throw new InvalidDataException("Hash SHA-256 incompleto.");
        }
        return expected ?? throw new InvalidDataException("La Release no publica SHA-256 de esta descarga.");
    }
}
