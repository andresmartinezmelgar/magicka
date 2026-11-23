using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class AccountProfilePanel
{
    public void UpdateGoogleStatus(string message, bool busy = false)
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _message = message;
        _busy = busy;
        Render();
    }

    public void RefreshStatisticsAccess() => Render();

    private void Render()
    {
        if (!IsInsideTree()) return;
        RenderAdvertising();
        _statistics.Visible = _main.CanViewMatchStatistics;
        bool google = _main.GoogleIdentityKnown || _main.Accounts.SignedIn;
        bool firestoreError = _message.StartsWith("Error al leer Firestore:", StringComparison.Ordinal);
        _heading.Text = google
            ? "Google conectado" + (_main.GoogleIdentityName.Length > 0 ? " · " + _main.GoogleIdentityName : "")
            : "Conecta tu cuenta de Google";
        _status.Text = firestoreError
            ? _message.Contains("403", StringComparison.Ordinal) || _message.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase)
                ? "Firestore: error 403. Google está conectado, pero faltan permisos de lectura en Firebase."
                : "Google está conectado, pero no se ha podido sincronizar Firestore. " + _message
            : _main.Accounts.SignedIn
                ? "Cuenta e inventario sincronizados con Firebase."
                : google ? "Google autenticado. Esperando datos de Firebase."
                : _message.StartsWith("Error", StringComparison.OrdinalIgnoreCase) ||
                  _message.Contains("configur", StringComparison.OrdinalIgnoreCase) ||
                  _message.Contains("JSON OAuth", StringComparison.OrdinalIgnoreCase)
                    ? _message
                    : "Inicia sesión para sincronizar tu perfil.";
        _login.Visible = !google;
        _login.Text = _main.DesktopGoogleNeedsConfiguration ? "Configurar y entrar con Google" : "Entrar con Google";
        _logout.Visible = google;
        _deleteAccount.Visible = _main.Accounts.SignedIn;
        _deleteAccount.Disabled = _busy;
        if (_deletionMessage.Length > 0) _status.Text = _deletionMessage;
        _login.Disabled = _busy;
        _logout.Disabled = _busy;
        _checkUpdate.Disabled = _checkingUpdate;
        _checkUpdate.Text = _checkingUpdate ? "Buscando…" : "Buscar actualización";
        if (!_checkingUpdate && (_updateIdentity != _main.UpdateIdentityUid || _updateStatus.Text.Length == 0))
        {
            _updateIdentity = _main.UpdateIdentityUid;
            _updateStatus.Text = "Versión instalada: " + BuildInfo.Version;
        }
    }
}
