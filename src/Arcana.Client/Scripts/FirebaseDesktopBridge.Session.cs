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
