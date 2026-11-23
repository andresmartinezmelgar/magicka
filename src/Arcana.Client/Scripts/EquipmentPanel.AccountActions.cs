using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private void SetStatus(string message, bool routine = false)
    {
        _status.Text = message; _status.TooltipText = message; _routineStatus = routine;
        _status.Visible = message.Length > 0 && !routine;
    }

    private void Busy(bool value, string message = "")
    {
        _busy = value; foreach (var button in _actions) button.Disabled = InventoryLocked;
        if (message.Length > 0) SetStatus(message);
        UpdatePickerActions();
        if (_itemPicker != null && _itemPicker.Visible && message.Length > 0)
            SetPickerMessage(message);
        RenderSavedEquipment();
        if (_defensePlayer != null) RenderDefenseActions();
        else { _inventoryMenu.GetPopup().SetItemDisabled(0, InventoryLocked); _inventoryMenu.GetPopup().SetItemDisabled(1, InventoryLocked); }
    }

    private async Task ConnectInventoryServer()
    {
        try
        {
            await _main.EnsureShopServer(_lifetime.Token).ConfigureAwait(false);
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                Render(); RefreshPickerItems(); UpdatePickerActions();
            });
        }
        catch (Exception) { /* La consulta del inventario no depende del servidor de ventas. */ }
    }

    private async Task Refresh()
    {
        if (_defensePlayer != null) return;
        if (InventoryLocked) return;
        if (!_main.Accounts.SignedIn) { Busy(false, "Inicia sesión con Google para actualizar el inventario de tu cuenta."); return; }
        Busy(true, "Cargando inventario…");
        try { await _main.Accounts.Refresh(_lifetime.Token); Done("Inventario y equipo actualizados."); }
        catch (Exception error) { Failed(error); }
    }

    private void ChooseItem(string id)
    {
        if (InventoryLocked || _draft == null || TemporaryInventory && !CanChangeDefenseItems) return;
        ResetQuickActions();
        try
        {
            if (BotInventory)
            {
                var slot = _selectedSlot;
                _ = AccountInventory.Equip(_draft, new InventoryRequest(id, slot));
                DefenseOperation(new() { Type = "defense_equip", ItemId = id, Slot = (int)slot },
                    p => DefenseProfile(p).Loadout.Get(slot) == id,
                    message: "Equipo del bot actualizado.");
                return;
            }
            if (AutomaticSurvivalEquipment)
            {
                // Confirmar solo la ranura elegida evita sobrescribir botín recibido mientras el menú está abierto.
                _ = AccountInventory.Equip(_draft, new InventoryRequest(id, _selectedSlot));
                var slot = _selectedSlot;
                DefenseOperation(new() { Type = "defense_equip", ItemId = id, Slot = (int)slot },
                    p => p.ManualEquipmentSlots.Contains(slot) && DefenseProfile(p).Loadout.Get(slot) == id,
                    message: "Elección manual confirmada. Esta ranura se mantiene; las demás siguen en automático.");
                return;
            }
            _draft = AccountInventory.Equip(_draft, new InventoryRequest(id, _selectedSlot));
            Render();
            RefreshPickerItems();
            if (id.Length > 0 && PickerVisible) _pickerScroll.ScrollVertical = 0;
            string message = HasChanges ? "Cambios pendientes. Se guardarán al salir del inventario." : "La configuración coincide con el equipo guardado.";
            SetStatus(message, routine: true);
            if (PickerVisible) { _pickerStatus.Text = message; _pickerStatus.Visible = true; }
        }
        catch (Exception error) { SetStatus(error.Message); }
    }

    private async Task Sell()
    {
        if (InventoryLocked || _selling.Length == 0) return;
        if (_confirmingBatch) { await SellBatch(); return; }
        if (SaleProtected(_selling) || !CanSellInventory || _saleOwner != _draft?.Uid ||
            (_defensePlayer == null && _saleOwner != _main.Accounts.Profile?.Uid)) { CancelSale(); return; }
        _confirm.Hide();
        if (_defensePlayer != null) { SellDefenseItem(); return; }
        string itemId = _selling, owner = _saleOwner;
        Busy(true, "Eliminando objeto y abonando oro…");
        try { await PreparePermanentSale(itemId); var result = await _main.Accounts.Sell(itemId, _lifetime.Token); Done(result.Message); }
        catch (Exception error)
        {
            if (!_main.Accounts.InventoryNeedsRefresh) Failed(error);
            else if (await RecoverSaleState(owner))
                Done(_main.Accounts.Profile!.Items.Any(i => i.Id == itemId)
                    ? "Inventario sincronizado. La pieza sigue disponible; puedes reintentar la venta."
                    : "Venta confirmada al sincronizar. El objeto y el oro están actualizados.");
            else Failed(new InvalidOperationException("Venta sin confirmar. Actualiza el inventario antes de volver a vender.", error));
        }
        finally { _selling = ""; }
    }

    private void Done(string message) => _main.Post(() =>
    {
        bool saved = _main.SaveAccountCache();
        if (_lifetime.IsCancellationRequested) return;
        RebaseDraft();
        Render(); Busy(false, saved ? message : message + " No se pudo actualizar la copia del dispositivo.");
        RefreshPickerItems();
        ContinueRequestedClose();
    });

    private void Failed(Exception error) => _main.Post(() =>
    {
        if (_lifetime.IsCancellationRequested) return;
        RebaseDraft(); Render(); Busy(false, error is OperationCanceledException ? "Operación cancelada." : error.Message);
        RefreshPickerItems();
        ContinueRequestedClose();
    });

    public override void _ExitTree() { GetViewport().SizeChanged -= UpdateEquipmentResponsive; _lifetime.Cancel(); _lifetime.Dispose(); }
}
