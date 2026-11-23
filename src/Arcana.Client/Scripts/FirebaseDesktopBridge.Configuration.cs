using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

public sealed partial class FirebaseDesktopBridge
{
    // En exportaciones Godot, AppContext.BaseDirectory puede ser distinto al
    // directorio que contiene el ejecutable Windows.
    private static IEnumerable<string> ConfigurationDirectories(string userDirectory)
    {
        string executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        return new[] { userDirectory, executableDirectory, AppContext.BaseDirectory }
            .Where(dir => !string.IsNullOrWhiteSpace(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public bool TryImportConfiguration(string source, out string error)
    {
        error = "";
        try
        {
            var file = new FileInfo(source);
            if (!file.Exists || file.Length is <= 0 or > 8192)
                throw new InvalidDataException("Selecciona el JSON OAuth descargado desde Google Cloud.");
            string json = File.ReadAllText(source);
            if (!DesktopOAuthConfiguration.TryParse(json, out var configuration, out string reason))
                throw new InvalidDataException(reason);
            Directory.CreateDirectory(_userDirectory);
            string destination = Path.Combine(_userDirectory, "google-desktop-oauth.json");
            string temporary = destination + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            _clientId = configuration!.ClientId;
            _clientSecret = configuration.ClientSecret;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or JsonException or InvalidDataException or KeyNotFoundException)
        {
            error = exception is InvalidDataException ? exception.Message
                : "No se pudo importar el archivo OAuth. Comprueba que sea el JSON de escritorio.";
            return false;
        }
    }

    private static (string Id, string Secret) ReadConfiguration(string userDirectory)
    {
        string envId = System.Environment.GetEnvironmentVariable("ARCANA_GOOGLE_DESKTOP_CLIENT_ID")?.Trim() ?? "";
        string envSecret = System.Environment.GetEnvironmentVariable("ARCANA_GOOGLE_DESKTOP_CLIENT_SECRET")?.Trim() ?? "";
        if (DesktopOAuthConfiguration.IsClientId(envId) && envSecret.Length <= 256) return (envId, envSecret);

        // El JSON «Aplicación de escritorio» descargado de Google Cloud es la opción
        // más compatible. Se mantiene fuera del repositorio y nunca se registra.
        foreach (string directory in ConfigurationDirectories(userDirectory))
        {
            string file = Path.Combine(directory, "google-desktop-oauth.json");
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 8192) continue;
                if (DesktopOAuthConfiguration.TryParse(File.ReadAllText(file), out var configuration, out _))
                    return (configuration!.ClientId, configuration.ClientSecret);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            catch (KeyNotFoundException) { }
        }
        foreach (string directory in ConfigurationDirectories(userDirectory))
        {
            string file = Path.Combine(directory, "google-desktop-client-id.txt");
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 512) continue;
                string id = File.ReadAllText(file).Trim();
                if (DesktopOAuthConfiguration.IsClientId(id) && envSecret.Length <= 256) return (id, envSecret);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return ("", "");
    }

    private static string RandomUrlToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Param(string query, string name)
    {
        foreach (string field in query.TrimStart('?').Split('&'))
        {
            var parts = field.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name)
                return parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace("+", " ")) : "";
        }
        return "";
    }
}
