using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class AccountProfilePanel
{
    /// <summary>Selecciona el JSON OAuth sin copiarlo ni renombrarlo manualmente.</summary>
    public void OpenDesktopGoogleConfiguration()
    {
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        if (!GodotObject.IsInstanceValid(_googleFileDialog))
        {
            _googleFileDialog = new FileDialog
            {
                Title = "Seleccionar JSON OAuth (Aplicación de escritorio)",
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                Filters = new[] { "*.json ; Credenciales OAuth de Google (*.json)" }
            };
            AddChild(_googleFileDialog);
            _googleFileDialog.FileSelected += path => _main.ImportDesktopGoogleConfiguration(path);
        }
        _googleFileDialog!.PopupCentered(new Vector2I(880, 540));
    }

    private async Task CheckUpdate()
    {
        if (_checkingUpdate || _lifetime.IsCancellationRequested) return;
        _checkingUpdate = true; _updateStatus.Text = "Buscando actualizaciones…"; Render();
        string message;
        try { message = await _main.CheckForUpdates(); }
        catch (Exception) { message = "No se ha podido comprobar la actualización."; }
        if (_lifetime.IsCancellationRequested || !IsInsideTree()) return;
        _checkingUpdate = false; _updateIdentity = _main.UpdateIdentityUid;
        _updateStatus.Text = message; Render();
    }

    private async Task Logout()
    {
        if (_busy) return;
        _busy = true;
        Render();
        try
        {
            await _main.StopGoogleSession();
            UpdateGoogleStatus("Sesión cerrada.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { UpdateGoogleStatus("No se pudo cerrar sesión: " + error.Message); }
    }

    private void ConfirmAccountDeletion()
    {
        if (_busy || !_main.Accounts.SignedIn) return;
        var dialog = new ConfirmationDialog { Title = "Eliminar cuenta",
            DialogText = "Se eliminarán tu cuenta de Arcana Arena, oro, inventario y bestiario. No elimina tu cuenta de Google. Esta acción no se puede deshacer. Debes haber iniciado sesión con Google en los últimos cinco minutos." };
        dialog.GetOkButton().Text = "Eliminar cuenta y datos"; AddChild(dialog);
        dialog.Confirmed += () => { dialog.QueueFree(); _ = DeleteAccount(); };
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new Vector2I(480, 260));
    }

    private async Task DeleteAccount()
    {
        if (_busy) return;
        _busy = true; _deletionMessage = "Solicitando la eliminación…"; Render();
        try
        {
            await _main.RequestAccountDeletion(_lifetime.Token);
            await _main.StopGoogleSession();
            _deletionMessage = "Eliminación solicitada. El servidor completará el borrado aunque cierres la aplicación.";
        }
        catch (Exception error) { _deletionMessage = error.Message; }
        finally { if (IsInsideTree()) { _busy = false; Render(); } }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= ResizeProfileCard;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
