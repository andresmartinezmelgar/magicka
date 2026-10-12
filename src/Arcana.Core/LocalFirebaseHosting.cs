using System.Diagnostics;

namespace Arcana.Core;

/// <summary>
/// Activación voluntaria de Firebase para el servidor incluido en Windows.
/// Nunca se copian claves al paquete exportado ni se heredan credenciales por accidente.
/// </summary>
public static class LocalFirebaseHosting
{
    public const string ConfigFileName = "server.local.json";
    public const string CredentialFileName = "service-account.json";

    public static string DirectoryPath(string userDirectory) =>
        Path.Combine(Path.GetFullPath(userDirectory), "firebase");

    public static bool Configure(ProcessStartInfo process, string userDirectory)
    {
        ArgumentNullException.ThrowIfNull(process);
        string directory = DirectoryPath(userDirectory);
        string config = Path.Combine(directory, ConfigFileName);
        if (!File.Exists(config)) return false;

        string credential = Path.Combine(directory, CredentialFileName);
        if (!File.Exists(credential))
            throw new InvalidOperationException(
                "Firebase local: falta firebase/service-account.json en la carpeta de datos del juego. " +
                "Coloca ahí la credencial privada descargada desde tu proyecto Firebase (nunca en GitHub).");

        var configInfo = new FileInfo(config);
        var credentialInfo = new FileInfo(credential);
        if (configInfo.Length is < 2 or > 16 * 1024 || credentialInfo.Length is < 2 or > 64 * 1024)
            throw new InvalidOperationException("Firebase local: los archivos de configuración están vacíos o son demasiado grandes.");

        process.Environment["ARCANA_FIREBASE_CONFIG"] = config;
        process.Environment["GOOGLE_APPLICATION_CREDENTIALS"] = credential;
        process.Environment["ARCANA_REWARD_OUTBOX"] = Path.Combine(directory, "reward-outbox");
        return true;
    }
}
