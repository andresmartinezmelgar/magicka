using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace Arcana.Client;

/// <summary>
/// Login Google de escritorio mediante OAuth 2.0 (navegador del sistema, PKCE y
/// callback solo en 127.0.0.1). Intercambia la identidad de Google por un ID token
/// Firebase; ningún secreto OAuth ni token se registra o almacena en disco.
/// </summary>
public sealed class FirebaseDesktopBridge : IDisposable
{
    // Firebase Web API key: identificador público del proyecto, no credencial Admin.
    private const string FirebaseApiKey = "AIzaSyAq5LTlzFLFswFdfPAlvPtkbkjqUj3yM4M";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _loginCancellation;
    private readonly string _clientId;
    private string _refreshToken = "", _uid = "", _name = "";
    private int _loginBusy;
    private int _generation;

    public event Action<FirebaseAndroidBridge.Session>? SessionReceived;
    public event Action<string>? Error;
    public bool Available => !OS.HasFeature("android");
    public bool Configured => _clientId.Length > 0;
    public FirebaseDesktopBridge(string userDirectory)
    {
        _clientId = ReadClientId(userDirectory);
    }

    private static string ReadClientId(string userDirectory)
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("ARCANA_GOOGLE_DESKTOP_CLIENT_ID");
        string[] candidates =
        [
            fromEnvironment ?? "",
            ReadOptionalFile(Path.Combine(AppContext.BaseDirectory, "google-desktop-client-id.txt")),
            ReadOptionalFile(Path.Combine(userDirectory, "google-desktop-client-id.txt"))
        ];
        return candidates.Select(s => s.Trim()).FirstOrDefault(IsDesktopClientId) ?? "";
    }

    private static string ReadOptionalFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length < 512 ? File.ReadAllText(path) : "";
        }
        catch (IOException) { return ""; }
        catch (UnauthorizedAccessException) { return ""; }
    }

    private static bool IsDesktopClientId(string value) =>
        value.Length is > 35 and < 200 &&
        value.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) &&
        value[..^".apps.googleusercontent.com".Length].All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string RandomUrlToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    public async Task LoginAsync()
    {
        if (Interlocked.Exchange(ref _loginBusy, 1) != 0) return;
        int generation = Volatile.Read(ref _generation);
        try
        {
            if (!Configured)
                throw new InvalidOperationException(
                    "Falta configurar el ID OAuth de tipo Aplicación de escritorio en Google Cloud. " +
                    "Establece ARCANA_GOOGLE_DESKTOP_CLIENT_ID o google-desktop-client-id.txt junto al juego.");
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
                using var googleResponse = await _http.PostAsync("https://oauth2.googleapis.com/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = _clientId,
                        ["code"] = code,
                        ["code_verifier"] = verifier,
                        ["redirect_uri"] = redirect,
                        ["grant_type"] = "authorization_code"
                    }), timeout.Token).ConfigureAwait(false);
                if (!googleResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException("Google rechazó el código OAuth. Comprueba el tipo de cliente de escritorio.");
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
                SessionReceived?.Invoke(new FirebaseAndroidBridge.Session(uid, name, idToken,
                    DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 60, 3600))));
            }
            finally { listener.Stop(); }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || generation != Volatile.Read(ref _generation)) { }
        catch (OperationCanceledException) { Error?.Invoke("El inicio de sesión de Google ha caducado."); }
        catch (Exception error) { Error?.Invoke(error.Message); }
        finally { _loginCancellation = null; Interlocked.Exchange(ref _loginBusy, 0); }
    }

    public async Task RenewAsync()
    {
        string refresh = _refreshToken;
        int generation = Volatile.Read(ref _generation);
        if (refresh.Length == 0) { Error?.Invoke("No existe sesión Google que renovar."); return; }
        try
        {
            using var response = await _http.PostAsync(
                $"https://securetoken.googleapis.com/v1/token?key={FirebaseApiKey}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = refresh
                }), _lifetime.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("La sesión ha caducado. Cierra sesión y vuelve a entrar.");
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_lifetime.Token).ConfigureAwait(false));
            if (generation != Volatile.Read(ref _generation)) return;
            var root = data.RootElement;
            string idToken = root.GetProperty("id_token").GetString() ?? "";
            string next = root.GetProperty("refresh_token").GetString() ?? "";
            long seconds = long.TryParse(root.GetProperty("expires_in").GetString(), out long ttl) ? ttl : 3600;
            if (idToken.Length == 0 || next.Length == 0) throw new InvalidOperationException("Firebase no renovó la sesión.");
            _refreshToken = next;
            SessionReceived?.Invoke(new FirebaseAndroidBridge.Session(_uid, _name, idToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(seconds, 60, 3600))));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || generation != Volatile.Read(ref _generation)) { }
        catch (Exception error) { Error?.Invoke(error.Message); }
    }

    public void SignOut()
    {
        Interlocked.Increment(ref _generation);
        _loginCancellation?.Cancel();
        _refreshToken = "";
        _uid = "";
        _name = "";
    }
    public void Dispose()
    {
        SignOut();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _http.Dispose();
    }
}
