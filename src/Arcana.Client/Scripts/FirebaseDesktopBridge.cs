using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Login Google de escritorio mediante OAuth 2.0 (navegador del sistema, PKCE y
/// callback solo en 127.0.0.1). Intercambia la identidad de Google por un ID token
/// Firebase. El refresh token se guarda cifrado con DPAPI CurrentUser en Windows,
/// nunca como texto plano ni dentro del ZIP distribuido.
/// </summary>
public sealed class FirebaseDesktopBridge : IDisposable
{
    // Firebase Web API key: identificador público del proyecto, no credencial Admin.
    private const string FirebaseApiKey = "AIzaSyAq5LTlzFLFswFdfPAlvPtkbkjqUj3yM4M";
    private readonly System.Net.Http.HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loginCancellation;
    private string _clientId;
    private string _clientSecret;
    private readonly string _userDirectory;
    private readonly DesktopSessionStore _sessionStore;
    private string _refreshToken = "", _uid = "", _name = "";
    private int _loginBusy;
    private int _generation;

    public event Action<FirebaseAndroidBridge.Session>? SessionReceived;
    public event Action<string>? Error;
    public bool Available => !OS.HasFeature("android");
    public bool Configured => _clientId.Length > 0;
    public FirebaseDesktopBridge(string userDirectory)
    {
        _userDirectory = userDirectory;
        _sessionStore = new DesktopSessionStore(userDirectory);
        (_clientId, _clientSecret) = ReadConfiguration(userDirectory);
    }

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

    private static async Task<string> WaitForCode(TcpListener listener, string redirect,
        string state, CancellationToken token)
    {
        // Solo acepta conexiones IPv4 locales. Límite de lectura para impedir peticiones enormes.
        using var browser = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
        using var stream = browser.GetStream();
        byte[] buffer = new byte[8192];
        int length = 0;
        while (length < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }
        string request = Encoding.ASCII.GetString(buffer, 0, length);
        string first = request.Split("\r\n", 2)[0];
        string[] fields = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 2 || fields[0] != "GET" || !fields[1].StartsWith("/?", StringComparison.Ordinal))
            throw new InvalidOperationException("El navegador no devolvió una respuesta OAuth válida.");
        var callback = new Uri(new Uri(redirect), fields[1]);
        string code = Param(callback.Query, "code");
        string returnedState = Param(callback.Query, "state");
        string browserError = Param(callback.Query, "error");
        bool valid = returnedState == state && code.Length is > 5 and < 4096 && browserError.Length == 0;
        const string success = "<!doctype html><html lang='es'><meta charset='utf-8'><title>Arcana Arena</title><body><p>Autenticación recibida. Puedes volver a Arcana Arena.</p></body></html>";
        const string failure = "<!doctype html><html lang='es'><meta charset='utf-8'><title>Arcana Arena</title><body><p>No se ha completado la autenticación. Vuelve al juego.</p></body></html>";
        byte[] body = Encoding.UTF8.GetBytes(valid ? success : failure);
        string status = valid ? "200 OK" : "400 Bad Request";
        byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
        if (!valid) throw new InvalidOperationException("Google canceló la conexión o devolvió un estado de seguridad incorrecto.");
        return code;
    }

    private bool TryPersistSession()
    {
        try
        {
            _sessionStore.Save(_uid, _name, _refreshToken);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or CryptographicException or InvalidDataException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Recupera la cuenta anterior sin abrir el navegador ni almacenar ID tokens en disco.</summary>
    public async Task RestoreAsync()
    {
        if (!_sessionStore.Supported || _refreshToken.Length > 0 || Volatile.Read(ref _loginBusy) != 0) return;
        var previous = _sessionStore.Load();
        if (previous == null) return;
        _uid = previous.Uid;
        _name = previous.DisplayName;
        _refreshToken = previous.RefreshToken;
        await RenewAsync(silent: true).ConfigureAwait(false);
    }

    public async Task LoginAsync()
    {
        if (Interlocked.Exchange(ref _loginBusy, 1) != 0) return;
        // Una nueva selección manual anula cualquier renovación anterior en vuelo.
        int generation = Interlocked.Increment(ref _generation);
        try
        {
            if (!Configured)
                throw new InvalidOperationException(
                    "Falta configurar el ID OAuth de tipo Aplicación de escritorio en Google Cloud. " +
                    "Descarga google-desktop-oauth.json desde Google Cloud y colócalo junto al juego.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            _loginCancellation = timeout;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start(1);
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = $"http://127.0.0.1:{port}/";
                string verifier = RandomUrlToken() + RandomUrlToken();
                string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                string state = RandomUrlToken();
                string authorize = "https://accounts.google.com/o/oauth2/v2/auth?" +
                    "client_id=" + Uri.EscapeDataString(_clientId) +
                    "&response_type=code&scope=openid%20profile&access_type=online" +
                    "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                    "&code_challenge_method=S256&code_challenge=" + challenge +
                    "&state=" + state;
                if (OS.ShellOpen(authorize) != Godot.Error.Ok)
                    throw new InvalidOperationException("No se pudo abrir el navegador predeterminado.");
                string code = await WaitForCode(listener, redirect, state, timeout.Token).ConfigureAwait(false);
                var tokenFields = new Dictionary<string, string>
                {
                    ["client_id"] = _clientId,
                    ["code"] = code,
                    ["code_verifier"] = verifier,
                    ["redirect_uri"] = redirect,
                    ["grant_type"] = "authorization_code"
                };
                // En los clientes OAuth de escritorio el client_secret no es
                // confidencial; Google puede exigirlo incluso con PKCE.
                if (_clientSecret.Length > 0) tokenFields["client_secret"] = _clientSecret;
                using var googleResponse = await _http.PostAsync("https://oauth2.googleapis.com/token",
                    new FormUrlEncodedContent(tokenFields), timeout.Token).ConfigureAwait(false);
                if (!googleResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException("Google rechazó el código OAuth. Utiliza el JSON descargado de un cliente de tipo Aplicación de escritorio.");
                using var google = JsonDocument.Parse(await googleResponse.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                string googleIdToken = google.RootElement.GetProperty("id_token").GetString()
                    ?? throw new InvalidOperationException("Google no devolvió un ID token.");
                using var firebaseResponse = await _http.PostAsJsonAsync(
                    $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={FirebaseApiKey}",
                    new
                    {
                        requestUri = "http://localhost",
                        postBody = "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com",
                        returnSecureToken = true
                    }, timeout.Token).ConfigureAwait(false);
                if (!firebaseResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException("Firebase Auth rechazó la identidad de Google. Comprueba el proveedor Google y los dominios autorizados.");
                using var firebase = JsonDocument.Parse(await firebaseResponse.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
                if (generation != Volatile.Read(ref _generation)) return;
                var data = firebase.RootElement;
                string uid = data.GetProperty("localId").GetString() ?? "";
                string idToken = data.GetProperty("idToken").GetString() ?? "";
                string refresh = data.GetProperty("refreshToken").GetString() ?? "";
                string name = data.TryGetProperty("displayName", out var display) ? display.GetString() ?? "Mago" : "Mago";
                if (uid.Length == 0 || idToken.Length == 0 || refresh.Length == 0)
                    throw new InvalidOperationException("Firebase devolvió una sesión incompleta.");
                long seconds = long.TryParse(data.GetProperty("expiresIn").GetString(), out long ttl) ? ttl : 3600;
                _uid = uid;
                _name = name;
                _refreshToken = refresh;
                bool stored = TryPersistSession();
                SessionReceived?.Invoke(new FirebaseAndroidBridge.Session(uid, name, idToken,
                    DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 60, 3600))));
                if (!stored) Error?.Invoke("Google conectado, pero Windows no pudo recordar la sesión. Deberás entrar de nuevo al reiniciar.");
            }
            finally { listener.Stop(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || generation != Volatile.Read(ref _generation)) { }
        catch (OperationCanceledException) { Error?.Invoke("El inicio de sesión de Google ha caducado."); }
        catch (Exception error) { Error?.Invoke(error.Message); }
        finally { _loginCancellation = null; Interlocked.Exchange(ref _loginBusy, 0); }
    }

    public async Task RenewAsync(bool silent = false)
    {
        string refresh = _refreshToken;
        int generation = Volatile.Read(ref _generation);
        if (refresh.Length == 0)
        {
            if (!silent) Error?.Invoke("No existe sesión Google que renovar.");
            return;
        }
        try
        {
            using var response = await _http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={FirebaseApiKey}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = refresh
                }), _lifetime.Token).ConfigureAwait(false);
            if (generation != Volatile.Read(ref _generation)) return;
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
                {
                    // El refresh token ha sido revocado o rechazado: no volver a usarlo al iniciar.
                    _refreshToken = "";
                    try { _sessionStore.Delete(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                throw new InvalidOperationException("No se pudo renovar Google. Si la sesión ha caducado, entra de nuevo.");
            }
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_lifetime.Token).ConfigureAwait(false));
            if (generation != Volatile.Read(ref _generation)) return;
            var root = data.RootElement;
            string uid = root.TryGetProperty("user_id", out var userId)
                ? userId.GetString() ?? "" : "";
            if (uid.Length > 0 && uid != _uid)
            {
                _refreshToken = "";
                try { _sessionStore.Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw new InvalidDataException("La sesión guardada corresponde a otra cuenta Google.");
            }
            string idToken = root.GetProperty("id_token").GetString() ?? "";
            string next = root.GetProperty("refresh_token").GetString() ?? "";
            long seconds = long.TryParse(root.GetProperty("expires_in").GetString(), out long ttl) ? ttl : 3600;
            if (idToken.Length == 0 || next.Length == 0) throw new InvalidDataException("Firebase no renovó la sesión.");
            _refreshToken = next;
            bool stored = TryPersistSession();
            SessionReceived?.Invoke(new FirebaseAndroidBridge.Session(_uid, _name, idToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 60, 3600))));
            if (!stored) Error?.Invoke("Google conectado, pero no se pudo actualizar la sesión guardada en Windows.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || generation != Volatile.Read(ref _generation)) { }
        catch (Exception error)
        {
            if (!silent) Error?.Invoke(error.Message);
            else Error?.Invoke("No se pudo restaurar la sesión de Google. Puedes iniciar sesión manualmente.");
        }
    }

    public void SignOut()
    {
        Interlocked.Increment(ref _generation);
        _loginCancellation?.Cancel();
        _refreshToken = "";
        _uid = "";
        _name = "";
        try { _sessionStore.Delete(); }
        catch (IOException) { Error?.Invoke("No se pudieron borrar las credenciales guardadas en Windows."); }
        catch (UnauthorizedAccessException) { Error?.Invoke("Windows ha denegado borrar la sesión guardada."); }
    }
    public void Dispose()
    {
        // Cerrar Arcana Arena NO equivale a cerrar sesión: conservar el token
        // cifrado en DPAPI para restaurarlo en el siguiente arranque.
        Interlocked.Increment(ref _generation);
        _loginCancellation?.Cancel();
        _refreshToken = "";
        _uid = "";
        _name = "";
        _lifetime.Cancel();
        _lifetime.Dispose();
        _http.Dispose();
    }
}
