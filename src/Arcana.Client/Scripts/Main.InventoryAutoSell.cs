using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private readonly InventoryAutoSeller _inventoryAutoSeller = new();
    private bool _preparingAutoSales;
    private double _autoSaleClock;
    internal bool InventoryAutoSelling => _preparingAutoSales || _inventoryAutoSeller.Running;
    private bool CanAutoSell => !_playing && !Loading && !_reconnecting &&
        !(GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion()) &&
        !(GodotObject.IsInstanceValid(_itemShopPanel) && !_itemShopPanel!.IsQueuedForDeletion());

    private void TickInventoryAutoSell(double delta)
    {
        _autoSaleClock -= delta;
        if (_autoSaleClock > 0 || InventoryAutoSelling || !CanAutoSell || !Accounts.SignedIn || Accounts.Profile is not { } profile) return;
        _autoSaleClock = 2;
        var preferences = DevicePreferences.Inventory(profile.Uid, false);
        if (preferences.AutoSellRules.Count == 0 ||
            !Accounts.InventoryNeedsRefresh && preferences.AutomaticSales(profile).Length == 0) return;
        _preparingAutoSales = true;
        _ = RunInventoryAutoSell(preferences, profile.Uid, Accounts.Token);
    }

    private async Task RunInventoryAutoSell(InventoryPreferences preferences, string owner, string session)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        bool SameAccount() => Accounts.SignedIn && Accounts.Profile?.Uid == owner && Accounts.Token == session;
        try
        {
            await EnsureShopServer(timeout.Token);
            if (!SameAccount() || !CanAutoSell) return;
            if (Accounts.InventoryNeedsRefresh) await Accounts.Refresh(timeout.Token);
            if (!SameAccount()) return;
            int sold = await _inventoryAutoSeller.Run(Accounts, preferences, owner, () => CanAutoSell && SameAccount(), timeout.Token);
            if (sold > 0 && SameAccount()) _hud?.RewardNotice($"Venta automática: {sold} objetos vendidos.");
        }
        catch (Exception error)
        {
            _autoSaleClock = 30;
            Diagnostics.Warn("inventory.auto_sale", "No se pudo confirmar la venta automática. Se consultará el inventario antes de reintentar.", error);
        }
        finally
        {
            _preparingAutoSales = false;
            if (SameAccount()) SaveAccountCache();
            if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion()) _equipmentPanel.AutomaticSalesFinished();
            if (GodotObject.IsInstanceValid(_itemShopPanel) && !_itemShopPanel!.IsQueuedForDeletion()) _itemShopPanel.ResumeAfterAutoSales();
        }
    }
}
