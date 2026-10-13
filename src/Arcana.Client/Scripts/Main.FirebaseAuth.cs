using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private const string FirebaseProjectId = "arcana-arena-f2997";
    private FirebaseAndroidBridge? _google;
    private FirebaseDesktopBridge? _desktopGoogle;
    private long _sessionGeneration;
    private double _sessionCheckTimer;
    private DateTimeOffset _lastRenewRequest;
    private string _googleStatus = "";
    private bool _googleIdentityKnown;
    private string _googleIdentityName = "";
    /// <summary>Autenticación SDK completada aunque Firestore haya denegado los permisos.</summary>
    public bool GoogleIdentityKnown => _googleIdentityKnown;
    public string GoogleIdentityName => _googleIdentityName;
    public bool GoogleLoginAvailable => _google?.Available == true || _desktopGoogle?.Available == true;
    public bool DesktopGoogleNeedsConfiguration => _desktopGoogle is { Configured: false };

    public void ImportDesktopGoogleConfiguration(string path)
    {
        if (_desktopGoogle == null) return;
        if (!_desktopGoogle.TryImportConfiguration(path, out string error))
        {
            GoogleStatus("Error de configuración: " + error);
            return;
        }
        GoogleStatus("Configuración de Google guardada. Abriendo el navegador…", true);
        _ = _desktopGoogle.LoginAsync();
    }
    public string GoogleLoginStatus => _googleStatus;

    private void InitializeGoogleAuthentication()
    {
        _google = new FirebaseAndroidBridge();
        _google.SessionReceived += OnGoogleSession;
        _google.Error += error => Post(() => GoogleStatus("Error de Google: " + error));
        _google.SignedOut += () => Post(() =>
        {
            _googleIdentityKnown = false;
            _googleIdentityName = "";
            if (!Accounts.SignedIn) GoogleStatus("Sin sesión de Google. Puedes practicar sin conexión.");
        });
        if (_google.Available)
        {
            GoogleStatus("Comprobando la sesión guardada de Google…");
            _google.Restore();
        }
        else if (OS.HasFeature("android"))
            GoogleStatus("No se ha cargado el plugin nativo de Firebase. Reinstala la APK oficial.");
        else
        {
            _desktopGoogle = new FirebaseDesktopBridge(ProjectSettings.GlobalizePath("user://"));
            _desktopGoogle.SessionReceived += OnGoogleSession;
            _desktopGoogle.Error += error => Post(() => GoogleStatus("Error de Google: " + error));
            GoogleStatus(_desktopGoogle.Configured
                ? "Google disponible en Windows. Entra con tu navegador."
                : "Para activar Google en Windows, importa una vez el JSON OAuth de escritorio.");
        }
    }

    public void StartGoogleLogin()
    {
        if (!GoogleLoginAvailable)
        {
            GoogleStatus(OS.HasFeature("android")
                ? "La APK no contiene el plugin Firebase de Android."
                : "Google Sign-In directo está disponible en Android. En Windows se conserva la práctica sin conexión.");
            return;
        }
        if (_desktopGoogle != null)
        {
            if (!_desktopGoogle.Configured)
            {
                GoogleStatus("Selecciona el JSON OAuth de Aplicación de escritorio descargado de Google Cloud.");
                if (GodotObject.IsInstanceValid(_accountProfilePanel) &&
                    !_accountProfilePanel!.IsQueuedForDeletion())
                    _accountProfilePanel.OpenDesktopGoogleConfiguration();
                return;
            }
            GoogleStatus("Abriendo Google en el navegador del ordenador…", true);
            _ = _desktopGoogle.LoginAsync();
        }
        else
        {
            GoogleStatus("Selecciona tu cuenta de Google en la ventana de Android…", true);
            _google!.Login();
        }
    }

    /// <summary>Reintenta Firestore con la sesión guardada, sin volver a mostrar el selector Google.</summary>
    public void RetryGoogleSession()
    {
        if (!GoogleLoginAvailable)
        {
            GoogleStatus("Firebase Android no está disponible.");
            return;
        }
        GoogleStatus("Renovando la sesión y reintentando el acceso a Firestore…", true);
        if (_desktopGoogle != null) _ = _desktopGoogle.RenewAsync();
        else _google!.Renew();
    }

    private async void OnGoogleSession(FirebaseAndroidBridge.Session session)
    {
        long generation = Interlocked.Read(ref _sessionGeneration);
        Post(() =>
        {
            if (generation != Interlocked.Read(ref _sessionGeneration)) return;
            _googleIdentityKnown = true;
            _googleIdentityName = session.Name;
            GoogleStatus("Google autenticado. Comprobando el acceso al perfil…", true);
        });
        try
        {
            await Accounts.SetFirebaseSession(FirebaseProjectId, session.Uid, session.Name,
                session.IdToken, session.ExpiresAt, _lifetime.Token).ConfigureAwait(false);
            Post(() =>
            {
                if (generation != Interlocked.Read(ref _sessionGeneration)) return;
                SaveAccountCache();
                GoogleStatus("Google conectado. Perfil e inventario cargados desde Firebase.");
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Post(() =>
            {
                if (generation != Interlocked.Read(ref _sessionGeneration)) return;
                GoogleStatus("Error al leer Firestore: " + error.Message);
                Diagnostics.Warn("firebase.profile_failed", "La autenticación o lectura de Firestore ha fallado.", error);
            });
        }
    }

    public async Task StopGoogleSession()
    {
        Interlocked.Increment(ref _sessionGeneration);
        _googleIdentityKnown = false;
        _googleIdentityName = "";
        _google?.Logout();
        _desktopGoogle?.SignOut();
        await Accounts.SignOut(_lifetime.Token);
        SaveAccountCache();
        GoogleStatus("Sesión cerrada. No se necesita ningún servidor de cuentas para entrar.");
    }

    private void GoogleStatus(string message, bool busy = false)
    {
        _googleStatus = message;
        if (GodotObject.IsInstanceValid(_accountProfilePanel) && !_accountProfilePanel!.IsQueuedForDeletion())
            _accountProfilePanel!.UpdateGoogleStatus(message, busy);
        if (GodotObject.IsInstanceValid(_launcher) && !_launcher.IsQueuedForDeletion())
            _launcher.UpdateAccountStatus();
    }

    private void TickGoogleAuth(double delta)
    {
        if (!GoogleLoginAvailable || !Accounts.HasDirectFirebaseSession) return;
        _sessionCheckTimer += delta;
        if (_sessionCheckTimer < 15) return;
        _sessionCheckTimer = 0;
        if (Accounts.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(12) &&
            DateTimeOffset.UtcNow - _lastRenewRequest > TimeSpan.FromMinutes(1))
        {
            _lastRenewRequest = DateTimeOffset.UtcNow;
            if (_desktopGoogle != null) _ = _desktopGoogle.RenewAsync();
            else _google?.Renew();
        }
    }
}
