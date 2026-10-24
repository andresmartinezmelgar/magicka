using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private bool _needsCacheSave, _closeRequested;
    private Action? _afterClose;
    private ConfirmationDialog? _discardExit;

    private void ConfirmDiscard()
    {
        if (_busy) return;
        if (!HasChanges) { _needsCacheSave = false; Close(); return; }
        _discardExit ??= new ConfirmationDialog { Name = "DiscardEquipmentChanges", Title = "Salir del inventario",
            DialogText = "Se descartarán los cambios de equipación que todavía no se hayan guardado.",
            OkButtonText = "Descartar y salir", CancelButtonText = "Seguir en el inventario" };
        if (_discardExit.GetParent() == null)
        {
            AddChild(_discardExit); Ui.ConfigureDialog(_discardExit);
            _discardExit.GetLabel().Show(); _discardExit.GetLabel().AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _discardExit.Confirmed += () =>
            {
                _draft = _defensePlayer == null ? _main.Accounts.Profile : DefenseProfile(_defensePlayer);
                _savedLoadout = _draft?.Loadout ?? new();
                _needsCacheSave = false;
                Close();
            };
        }
        var viewport = GetViewport().GetVisibleRect().Size;
        _discardExit.PopupCentered(new((int)Math.Min(500, viewport.X - 32), 180));
        _discardExit.GetCancelButton().GrabFocus();
    }

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await _main.Accounts.SaveLoadout(_draft!.Loadout, timeout.Token);
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
        string reason = error is OperationCanceledException ? "No se pudo confirmar el guardado a tiempo." : error.Message;
        Render(); Busy(false, reason + " Reintenta salir para guardar, o elige «Descartar cambios y salir» en Opciones.");
    }

    private void ContinueRequestedClose()
    {
        if (!_closeRequested) return;
        _closeRequested = false;
        Close(_afterClose);
    }
}
