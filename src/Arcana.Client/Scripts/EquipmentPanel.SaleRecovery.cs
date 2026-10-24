namespace Arcana.Client;

public partial class EquipmentPanel
{
    private async Task<bool> RecoverSaleState(string owner)
    {
        if (_lifetime.IsCancellationRequested || _main.Accounts.Profile?.Uid != owner) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            var profile = await _main.Accounts.Refresh(timeout.Token);
            return profile.Uid == owner && !_main.Accounts.InventoryNeedsRefresh;
        }
        catch (Exception) { return false; }
    }

    private string SaleBlockedReason => TemporaryInventory ? "No puedes vender con el mago caído o al terminar la partida." :
        _main.InventoryAutoSelling ? "Terminando la venta automática. Puedes salir de esta pantalla." :
        _main.Accounts.InventoryNeedsRefresh ? "Venta sin confirmar. Pulsa «Actualizar» en el inventario antes de volver a vender." :
        "Venta bloqueada: inicia sesión y conecta con el servidor de ventas.";

    private void SaleProgress(string owner, int sold, int count, long gold) => _main.Post(() =>
    {
        if (_lifetime.IsCancellationRequested || _main.Accounts.Profile?.Uid != owner || !_busy) return;
        string text = $"Vendiendo {sold}/{count} objetos · +{gold} oro confirmado…";
        SetStatus(text); if (PickerVisible) SetPickerMessage(text);
    });
}
