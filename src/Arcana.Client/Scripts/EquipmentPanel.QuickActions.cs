using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private const double ItemDoubleClickSeconds = 0.35;
    private Godot.Timer _saleClickTimer = null!;
    private string _pendingSaleClick = "", _pendingSaleAccount = "", _lastUpgradeClick = "";
    private EquipmentSlot _pendingSaleSlot;
    private ulong _pointerFrame = ulong.MaxValue, _lastUpgradeAt;
    private Rect2 _lastUpgradeRect;

    private void BuildQuickActions()
    {
        _saleClickTimer = new Godot.Timer { OneShot = true, WaitTime = ItemDoubleClickSeconds };
        AddChild(_saleClickTimer);
        _saleClickTimer.Timeout += () =>
        {
            string id = _pendingSaleClick, owner = _pendingSaleAccount;
            var slot = _pendingSaleSlot;
            CancelPendingSaleClick();
            if (_lifetime.IsCancellationRequested || !PickerVisible || _draft?.Uid != owner || _selectedSlot != slot ||
                !_trashButtons.TryGetValue(id, out var button) || button.Disabled) return;
            ConfirmSale(id);
        };
    }

    public override void _Input(InputEvent input)
    {
        // Solo el ratón y el tacto activan el atajo. Teclado y mando conservan la confirmación.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } or InputEventScreenTouch)
            _pointerFrame = Engine.GetProcessFrames();
        else if (input is InputEventKey or InputEventJoypadButton or InputEventAction)
        { _pointerFrame = ulong.MaxValue; _lastUpgradeClick = ""; }
        Vector2? pointer = input switch
        {
            InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse => mouse.Position,
            InputEventScreenTouch { Pressed: true } touch => touch.Position,
            _ => null
        };
        // Un mensaje de estado puede mover las tarjetas al confirmar la primera mejora.
        // Consume el segundo toque en su posición original para no pulsar otra acción.
        if (pointer.HasValue && _lastUpgradeClick.Length > 0 && PickerVisible &&
            Time.GetTicksMsec() - _lastUpgradeAt <= ItemDoubleClickSeconds * 1000 && _lastUpgradeRect.HasPoint(pointer.Value))
        { GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventScreenDrag || input is InputEventMouseMotion motion &&
            (motion.ButtonMask & MouseButtonMask.Left) != 0 && motion.Relative.Length() > 4)
            CancelPendingSaleClick();
    }

    private void ClickSell(string id)
    {
        if (InventoryLocked || !CanSellInventory || !PickerVisible || !_trashButtons.TryGetValue(id, out var button) || button.Disabled) return;
        _lastUpgradeClick = "";
        if (_pointerFrame != Engine.GetProcessFrames())
        { CancelPendingSaleClick(); ConfirmSale(id); return; }
        if (_pendingSaleClick == id && _pendingSaleAccount == _draft?.Uid && _pendingSaleSlot == _selectedSlot && _saleClickTimer.TimeLeft > 0)
        {
            CancelPendingSaleClick();
            if (SelectSaleItem(id) != null) _ = Sell();
            return;
        }
        CancelPendingSaleClick();
        _pendingSaleClick = id; _pendingSaleAccount = _draft?.Uid ?? ""; _pendingSaleSlot = _selectedSlot;
        _saleClickTimer.Start();
    }

    private void ClickUpgrade(string id)
    {
        CancelPendingSaleClick();
        if (!CanUpgradeItem(id)) return;
        if (_pointerFrame == Engine.GetProcessFrames())
        {
            ulong now = Time.GetTicksMsec();
            // El servidor puede responder y reconstruir los botones antes del segundo toque.
            // El gesto pertenece al ID de la pieza, no a la instancia del botón.
            if (_lastUpgradeClick == id && now - _lastUpgradeAt <= ItemDoubleClickSeconds * 1000) return;
            _lastUpgradeClick = id; _lastUpgradeAt = now;
            _lastUpgradeRect = _upgradeButtons[id].GetGlobalRect();
        }
        if (_defensePlayer != null) UpgradeDefenseItem(id); else _ = UpgradePermanentRing(id);
    }

    private void CancelPendingSaleClick()
    {
        _saleClickTimer?.Stop(); _pendingSaleClick = ""; _pendingSaleAccount = "";
    }
    private void ResetQuickActions()
    {
        CancelPendingSaleClick(); _lastUpgradeClick = "";
    }
}
