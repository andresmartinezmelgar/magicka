using Arcana.Core;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private bool _needsCacheSave, _closeRequested;
    private Action? _afterClose;

    // Las lecturas y ventas recuperan datos del servidor sin perder la selección pendiente.
    // Una pieza vendida o retirada del inventario nunca se vuelve a equipar.
    private void RebaseDraft()
    {
        var latest = _defensePlayer == null ? _main.Accounts.Profile : DefenseProfile(_defensePlayer);
        if (latest == null) { _draft = null; _savedLoadout = new(); return; }
        var loadout = latest.Loadout;
        if (_draft?.Uid == latest.Uid)
            foreach (var slot in Enum.GetValues<EquipmentSlot>())
            {
                string id = _draft.Loadout.Get(slot);
                if (id == _savedLoadout.Get(slot)) continue;
                loadout = loadout.With(slot, id.Length == 0 || latest.Items.Any(i => i.Id == id && i.Slot == slot) ? id : "");
            }
        _savedLoadout = latest.Loadout;
        _draft = latest with { Loadout = loadout };
        if (_defensePlayer == null) _onlineInventory |= _main.Accounts.SignedIn || _main.Accounts.HasDirectFirebaseSession;
    }

    public void Close(Action? afterClose = null)
    {
        if (_closing) return;
        if (_busy) { _closeRequested = true; _afterClose = afterClose; return; }
        _closing = true; _afterClose = afterClose;
        CancelSale();
        CloseItemPicker();
        if (_defensePlayer != null) { SaveDefenseAndClose(); return; }
        if (!HasChanges) { SaveCacheAndClose(); return; }
        Busy(true, "Guardando configuración…");
        if (_onlineInventory) { _ = SaveAndClose(); return; }
        try
        {
            _main.Accounts.EquipOffline(_draft!.Loadout);
            MarkSaved(); SaveCacheAndClose();
        }
        catch (Exception error) { CloseFailed(error); }
    }

    private async Task SaveAndClose()
    {
        try
        {
            await _main.Accounts.SaveLoadout(_draft!.Loadout, _lifetime.Token);
            _main.Post(() =>
            {
                if (_lifetime.IsCancellationRequested) return;
                MarkSaved(); SaveCacheAndClose();
            });
        }
        catch (Exception error)
        {
            _main.Post(() => { if (!_lifetime.IsCancellationRequested) CloseFailed(error); });
        }
    }

    private void MarkSaved()
    {
        _draft = _main.Accounts.Profile;
        _savedLoadout = _draft!.Loadout;
        _needsCacheSave = true;
    }

    private void SaveCacheAndClose()
    {
        if (_needsCacheSave && !_main.SaveAccountCache())
        {
            CloseFailed(new InvalidOperationException("No se pudo guardar la configuración en este dispositivo."));
            return;
        }
        _lifetime.Cancel(); QueueFree();
        Closed?.Invoke(); _afterClose?.Invoke();
    }

    private void CloseFailed(Exception error)
    {
        _closing = false;
        Render(); Busy(false, error.Message + " La configuración sigue en el inventario; vuelve a intentar salir para guardarla.");
    }

    private void ContinueRequestedClose()
    {
        if (!_closeRequested) return;
        _closeRequested = false;
        Close(_afterClose);
    }
}
