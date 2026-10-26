using Godot;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
    private bool _deletingHistory;
    private ConfirmationDialog? _deleteFirst, _deleteFinal;
    private string? _deleteUid;
    private void ConfirmDeleteHistory()
    {
        if (_loading || _deletingHistory || !_main.CanViewMatchStatistics) return;
        _deleteUid = _main.Accounts.Profile?.Uid;
        ShowDeleteConfirmation(false);
    }
    private void ShowDeleteConfirmation(bool final)
    {
        if (!_main.CanViewMatchStatistics || _main.Accounts.Profile?.Uid != _deleteUid) return;
        var dialog = final ? _deleteFinal : _deleteFirst;
        if (dialog == null)
        {
            dialog = new ConfirmationDialog
            {
                Name = final ? "DeleteStatisticsFinalConfirmation" : "DeleteStatisticsFirstConfirmation",
                Title = final ? "Confirmación final: borrado irreversible" : "Borrar historial global · 1 de 2",
                DialogText = final
                    ? "Se borrarán permanentemente TODOS los informes de Firebase, de todos los dispositivos y versiones, sin importar los filtros. Incluye daño, curación, escudos y demás estadísticas de esas partidas. No se puede deshacer. Los dispositivos podrán enviar informes nuevos."
                    : "Vas a borrar el historial GLOBAL de estadísticas de Firebase, no solo los datos visibles. No se borrarán cuentas, inventario ni oro. Todavía falta una segunda confirmación.",
                OkButtonText = final ? "Borrar definitivamente" : "Continuar al segundo paso",
                CancelButtonText = "Cancelar"
            };
            AddChild(dialog); Ui.ConfigureDialog(dialog);
            dialog.GetLabel().Show();
            dialog.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (final) { _deleteFinal = dialog; dialog.Confirmed += () => _ = DeleteHistory(); }
            else { _deleteFirst = dialog; dialog.Confirmed += () => Callable.From(() => ShowDeleteConfirmation(true)).CallDeferred(); }
            dialog.Canceled += () => _deleteUid = null;
        }
        var viewport = GetViewport().GetVisibleRect().Size;
        dialog.PopupCentered(new((int)Math.Min(560, viewport.X - 32), (int)Math.Min(280, viewport.Y - 48)));
        dialog.GetCancelButton().GrabFocus();
    }
    private async Task DeleteHistory()
    {
        if (_loading || _deletingHistory || !_main.CanViewMatchStatistics || _main.Accounts.Profile?.Uid != _deleteUid) return;
        _deletingHistory = true; _deleteHistory.Disabled = _refresh.Disabled = _more.Disabled = true;
        _status.Text = "Borrando el historial global de Firebase…";
        try
        {
            int count = await _main.DeleteCloudMatchStatistics(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            _reports.Clear(); _pageTokens.Clear(); _nextPageToken = ""; _loadedPages = 0;
            _more.Visible = false; Versions(); Render();
            _status.Text = $"Historial borrado: {count} informes. Los dispositivos pueden registrar partidas nuevas.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (!_lifetime.IsCancellationRequested)
                _status.Text = "No se pudo completar el borrado. Puede haber informes ya eliminados; actualiza para comprobarlo. " +
                    (error is UnauthorizedAccessException ? error.Message : "Comprueba la conexión y las reglas de Firebase.");
        }
        finally
        {
            _deletingHistory = false; _deleteUid = null;
            if (!_lifetime.IsCancellationRequested) _deleteHistory.Disabled = _refresh.Disabled = _more.Disabled = false;
        }
    }
}
