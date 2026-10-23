using Arcana.Core;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private bool CanUpgradeItem(string id)
    {
        if (_defensePlayer != null) return CanUpgradeDefenseItem(id);
        var item = _draft?.Items.FirstOrDefault(i => i.Id == id);
        return !_busy && CanSellInventory && item != null && RingEffects.IsRing(item.Slot) &&
            item.UpgradeLevel < 10 && _draft!.Gold >= DefenseRules.UpgradeCost(item.Tier, item.UpgradeLevel);
    }

    private async Task UpgradePermanentRing(string id)
    {
        var item = _draft?.Items.FirstOrDefault(i => i.Id == id);
        if (item == null || !CanUpgradeItem(id)) return;
        Busy(true, "Mejorando anillo…");
        try
        {
            await _main.Accounts.UpgradeRing(id, item.UpgradeLevel, _lifetime.Token);
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                RebaseDraft(); _needsCacheSave = true;
                Busy(false); Render(); RefreshPickerItems();
                SetPickerMessage("Anillo mejorado. Oro y nivel confirmados en la cuenta.");
                ContinueRequestedClose();
            });
        }
        catch (Exception error)
        {
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                Busy(false); Render(); RefreshPickerItems();
                SetPickerMessage(error.Message + (_main.Accounts.InventoryNeedsRefresh ? " Actualiza el inventario para confirmar la mejora." : ""));
                _closeRequested = false;
            });
        }
    }
}
