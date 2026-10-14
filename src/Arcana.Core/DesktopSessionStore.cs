using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Arcana.Core;

/// <summary>
/// Sesión recuperable de Windows. El token de renovación nunca debe registrarse
/// ni incluirse en un paquete de distribución.
/// </summary>
public sealed class DesktopSavedSession
{
    public string ProjectId { get; init; } = "";
    public string Uid { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string RefreshToken { get; init; } = "";
}

/// <summary>
/// Almacena el refresh token de Firebase cifrado con DPAPI CurrentUser.
/// Solo la misma cuenta de Windows puede descifrarlo; Android no lo utiliza.
/// </summary>
public sealed class DesktopSessionStore(string userDirectory)
{
    public const string FirebaseProjectId = "arcana-arena-f2997";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArcanaArena/Firebase/Desktop/CurrentUser/v1");
    private readonly string _path = Path.Combine(Path.GetFullPath(userDirectory), "google-desktop-session.dat");

    public bool Supported => OperatingSystem.IsWindows();

    public void Save(string uid, string name, string refreshToken)
    {
        if (!Supported) return;
        Validate(uid, name, refreshToken);
        var session = new DesktopSavedSession
        {
            ProjectId = FirebaseProjectId, Uid = uid, DisplayName = name, RefreshToken = refreshToken
        };
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public DesktopSavedSession? Load()
    {
        if (!Supported || !File.Exists(_path)) return null;
        try
        {
            if (new FileInfo(_path).Length is <= 0 or > 32768)
                throw new InvalidDataException("Archivo de sesión fuera de límites.");
            byte[] encrypted = File.ReadAllBytes(_path);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var session = JsonSerializer.Deserialize<DesktopSavedSession>(plain)
                    ?? throw new InvalidDataException("Sesión incompleta.");
                if (session.ProjectId != FirebaseProjectId)
                    throw new InvalidDataException("La sesión pertenece a otro proyecto Firebase.");
                Validate(session.Uid, session.DisplayName, session.RefreshToken);
                return session;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception error) when (error is CryptographicException or JsonException or InvalidDataException)
        {
            // Si el archivo se ha alterado o pertenece a otro usuario, se exige volver a entrar.
            try { Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Delete()
    {
        if (!Supported) return;
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    private static void Validate(string uid, string name, string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(uid) || uid.Length > 256 || name.Length > 512 ||
            string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length is < 16 or > 8192)
            throw new InvalidDataException("La sesión Google tiene campos inválidos.");
    }
}
