using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Adapta el inventario compartido a la partida. Solo envía comandos al mundo autoritativo.</summary>
public partial class EquipmentPanel
{
    private DefensePlayerSnapshot? _defensePlayer;
    private WizardSnapshot? _defenseWizard;
    private bool _defenseMatchEnded;
    private Action<ClientMessage>? _defenseSubmit;
    private Button? _defenseSupply, _defenseUpgrade;
    private string _defenseInventoryStamp = "";
    private Func<DefensePlayerSnapshot, bool>? _defensePending;
    private Action? _defenseConfirmed;
    private ulong _defensePendingAt;
    internal bool TemporaryInventory => _defensePlayer != null;
    private bool CanChangeDefenseItems => _defenseWizard?.Alive == true && !_defenseMatchEnded;
    private bool CanSellInventory => _defensePlayer != null ? CanChangeDefenseItems :
        _main.Accounts.SignedIn && (!_main.Accounts.IsDirectFirebaseSession || _main.Accounts.ServerAddress.Length > 0);

    internal void BuildDefense(Main main, DefensePlayerSnapshot player, WizardSnapshot wizard, bool ended, Action<ClientMessage> submit)
    {
        _defensePlayer = player; _defenseWizard = wizard; _defenseMatchEnded = ended; _defenseSubmit = submit;
        Build(main, "");
    }

    private AccountProfile DefenseProfile(DefensePlayerSnapshot player)
    {
        var loadout = new EquipmentLoadout();
        foreach (var item in player.Items.Where(i => i.Equipped)) loadout = loadout.With(item.Item.Slot, item.Item.Id);
        return new("defense-" + player.PlayerId, _defenseWizard!.Name, player.Gold, 0, 0, loadout,
            player.Items.Select(i => i.Item).ToArray());
    }

    private void BuildDefenseActions(VBoxContainer inventory)
    {
        _defenseUpgrade = Action(inventory, "Mejorar pieza", UpgradeDefenseItem);
        _defenseUpgrade.Name = "DefenseInventoryUpgrade";
        _defenseSupply = Action(inventory, "Comprar pieza aleatoria · 100 oro", BuyDefenseItem);
        _defenseSupply.Name = "DefenseInventorySupply";
    }

    private void RenderDefenseActions()
    {
        if (_defenseSupply == null || _defenseUpgrade == null || _defensePlayer == null) return;
        var selected = Selected();
        var entry = _defensePlayer.Items.FirstOrDefault(i => i.Item.Id == selected?.Id);
        int cost = DefenseRules.UpgradeCost(entry?.Upgrades ?? 0);
        _defenseSupply.Disabled = _busy || !CanChangeDefenseItems || _defensePlayer.Gold < 100 ||
            _defensePlayer.Items.Length >= DefenseRules.InventoryLimit;
        _defenseUpgrade.Disabled = _busy || !CanChangeDefenseItems || entry == null || entry.Upgrades >= 10 ||
            entry.Item.Bonuses.All(b => b.Current >= b.Maximum) || _defensePlayer.Gold < cost;
        _defenseUpgrade.Text = entry == null ? "Elige una pieza para mejorar" : $"Mejorar pieza · +{entry.Upgrades} / 10 · {cost} oro";
        _defenseUpgrade.TooltipText = "Suma un punto porcentual a cada atributo, hasta el máximo de la pieza.";
    }

    internal void UpdateDefenseInventory(DefensePlayerSnapshot player, WizardSnapshot wizard, bool ended)
    {
        _defensePlayer = player; _defenseWizard = wizard; _defenseMatchEnded = ended;
        string stamp = player.Gold + ":" + wizard.Alive + ":" + ended + ":" +
            string.Join('|', player.Items.Select(i => $"{i.Item.Id}:{i.Upgrades}:{i.Equipped}"));
        if (stamp != _defenseInventoryStamp)
        {
            _defenseInventoryStamp = stamp;
            RebaseDraft(); Render();
            RefreshPickerItems();
        }
        if (_defensePending == null) return;
        if (_defensePending(player))
        {
            var confirmed = _defenseConfirmed;
            _defensePending = null; _defenseConfirmed = null;
            Busy(false); Render(); SetStatus("Equipo de partida actualizado.", routine: true);
            RefreshPickerItems();
            if (PickerVisible) { _pickerStatus.Text = "Venta confirmada. El oro y las piezas están actualizados."; _pickerStatus.Visible = true; }
            confirmed?.Invoke();
            if (!_closing) ContinueRequestedClose();
        }
        else if (Time.GetTicksMsec() - _defensePendingAt > 6000)
        {
            _defensePending = null; _defenseConfirmed = null; _closeRequested = false;
            CloseFailed(new InvalidOperationException("La partida no confirmó la operación."));
            UpdatePickerActions();
        }
    }

    private void DefenseOperation(ClientMessage command, Func<DefensePlayerSnapshot, bool> confirmed, Action? after = null)
    {
        _defensePending = confirmed; _defenseConfirmed = after; _defensePendingAt = Time.GetTicksMsec();
        Busy(true, "Actualizando equipo de partida…"); _defenseSubmit!(command);
    }

    private void BuyDefenseItem()
    {
        if (_busy || !CanChangeDefenseItems || _defensePlayer == null || _defenseSupply!.Disabled) return;
        var ids = _defensePlayer.Items.Select(i => i.Item.Id).ToHashSet(StringComparer.Ordinal);
        DefenseOperation(new() { Type = "defense_supply" }, p => p.Items.Any(i => !ids.Contains(i.Item.Id)));
    }

    private void UpgradeDefenseItem()
    {
        if (_busy || _defenseUpgrade!.Disabled || _defensePlayer == null) return;
        var item = Selected(); if (item == null) return;
        int before = _defensePlayer.Items.Single(i => i.Item.Id == item.Id).Upgrades;
        DefenseOperation(new() { Type = "defense_upgrade", ItemId = item.Id },
            p => p.Items.Any(i => i.Item.Id == item.Id && i.Upgrades > before));
    }

    private void SellDefenseItem()
    {
        if (_busy || !CanSellInventory || _selling.Length == 0) return;
        string id = _selling; _selling = "";
        DefenseOperation(new() { Type = "defense_sell", ItemId = id }, p => p.Items.All(i => i.Item.Id != id));
    }

    private void SaveDefenseAndClose()
    {
        if (!HasChanges) { FinishDefenseClose(); return; }
        var desired = _draft!.Loadout;
        DefenseOperation(new() { Type = "defense_loadout", Loadout = desired },
            p => DefenseProfile(p).Loadout == desired, FinishDefenseClose);
    }

    private void FinishDefenseClose()
    {
        _lifetime.Cancel(); QueueFree(); Closed?.Invoke(); _afterClose?.Invoke();
    }
}
