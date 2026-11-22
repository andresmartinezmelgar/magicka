using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class AccountProfilePanel
{
    private Button _advertisingToggle = null!;
    private ElementTextLabel _advertisingStatus = null!;
    private AdvertisingSettings? _advertisingState;
    private AccountClient? _advertisingAccount;
    private long _advertisingSession;
    private bool _advertisingBusy;

    private bool AdvertisingAdmin => _main.Accounts.SignedIn && _main.Accounts.Profile?.Access.IsAdmin == true;

    private void RenderAdvertising()
    {
        _advertisingToggle.Visible = _advertisingStatus.Visible = AdvertisingAdmin;
        if (!AdvertisingAdmin)
        {
            _advertisingAccount = null; _advertisingState = null; _advertisingBusy = false;
            return;
        }
        var account = _main.Accounts;
        if (!ReferenceEquals(_advertisingAccount, account) || _advertisingSession != account.SessionRevision)
        {
            _advertisingAccount = account; _advertisingSession = account.SessionRevision;
            _advertisingState = null; _advertisingBusy = false;
            _ = ChangeAdvertising(); // Primera consulta al abrir el perfil.
        }
        _advertisingToggle.Disabled = _busy || _advertisingBusy;
        _advertisingToggle.Text = _advertisingBusy ? "Consultando anuncios…" : _advertisingState == null
            ? "Consultar anuncios globales" : _advertisingState.Enabled
            ? "Desactivar anuncios para todos" : "Activar anuncios para todos";
    }

    private async Task ChangeAdvertising()
    {
        if (_advertisingBusy || _busy || !AdvertisingAdmin || _lifetime.IsCancellationRequested) return;
        var account = _main.Accounts;
        long session = account.SessionRevision;
        var current = _advertisingState;
        _advertisingBusy = true;
        _advertisingToggle.Disabled = true;
        _advertisingStatus.Text = current == null ? "Consultando estado global…" : "Guardando el cambio…";
        try
        {
            await _main.EnsureShopServer(_lifetime.Token, account).ConfigureAwait(false);
            if (!ReferenceEquals(account, _main.Accounts) || session != account.SessionRevision)
                throw new OperationCanceledException();
            var result = current == null ? await account.Advertising(_lifetime.Token).ConfigureAwait(false)
                : await account.UpdateAdvertising(new(!current.Enabled, current.Revision), _lifetime.Token).ConfigureAwait(false);
            _main.Post(() =>
            {
                if (!AdvertisingResultCurrent(account, session)) return;
                _advertisingState = result; _advertisingBusy = false;
                _main.ForgetShop(account, session);
                _advertisingStatus.Text = (result.Enabled ? "Anuncios activados." : "Anuncios desactivados hasta que un administrador los active.") +
                    " Los administradores y las cuentas con derecho sin anuncios quedan siempre exentos. La renovación extra sigue limitada a una al día.";
                RenderAdvertising();
            });
        }
        catch (Exception error)
        {
            _main.Post(() =>
            {
                if (!AdvertisingResultCurrent(account, session)) return;
                _advertisingState = null; _advertisingBusy = false;
                _advertisingStatus.Text = "No se ha podido confirmar el estado. " + PvpServer.DisplayText(error.Message) +
                    " Pulsa «Consultar anuncios globales» antes de cambiarlo.";
                RenderAdvertising();
            });
        }
    }

    private bool AdvertisingResultCurrent(AccountClient account, long session) => !_lifetime.IsCancellationRequested &&
        IsInsideTree() && AdvertisingAdmin && ReferenceEquals(account, _main.Accounts) &&
        ReferenceEquals(account, _advertisingAccount) && session == account.SessionRevision && session == _advertisingSession;
}
