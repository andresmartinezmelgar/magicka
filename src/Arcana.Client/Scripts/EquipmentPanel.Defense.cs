using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Adapta el inventario compartido a la partida. Solo envía comandos al mundo autoritativo.</summary>
public partial class EquipmentPanel
{
    private DefensePlayerSnapshot? _defensePlayer;
    private string[]? _defenseFavorites;
    private EquipmentStyle[]? _defenseAutoSellStyles;
    private int _defensePreferenceRevision;
    private void SyncDefensePreferences()
    {
        if (_defensePlayer == null || _draft == null) return;
        if (_defenseFavorites != null && _defenseFavorites.SequenceEqual(_defensePlayer.Favorites) &&
            _defenseAutoSellStyles!.SequenceEqual(_defensePlayer.AutoSellStyles)) return;
        _defenseFavorites = _defensePlayer.Favorites; _defenseAutoSellStyles = _defensePlayer.AutoSellStyles;
        Preferences.Favorites = new(_defenseFavorites, StringComparer.Ordinal);
        _defensePreferenceRevision++;
    }
    private readonly Dictionary<string, int> _itemLevels = new(StringComparer.Ordinal);
    private void UpdateItemLevels(DefensePlayerSnapshot player)
    {
        _itemLevels.Clear();
        foreach (var item in player.Items) _itemLevels[item.Item.Id] = item.Upgrades;
    }
    private WizardSnapshot? _defenseWizard;
    private bool _defenseMatchEnded;
    private Action? _resumeDefense;
    private Action<ClientMessage>? _defenseSubmit;
    private string _defenseInventoryStamp = "";
    private Func<DefensePlayerSnapshot, bool>? _defensePending;
    private Action? _defenseConfirmed;
    private ulong _defensePendingAt;
    private string _defensePendingMessage = "";
    internal bool TemporaryInventory => _defensePlayer != null;
    private bool CanChangeDefenseItems => _defenseWizard?.Alive == true && !_defenseMatchEnded;
    private bool CanSellInventory => _defensePlayer != null ? CanChangeDefenseItems :
        !_main.InventoryAutoSelling && !_main.Accounts.InventoryNeedsRefresh && _main.Accounts.SignedIn &&
        (!_main.Accounts.IsDirectFirebaseSession || _main.Accounts.ServerAddress.Length > 0);

    internal void BuildDefense(Main main, DefensePlayerSnapshot player, WizardSnapshot wizard, bool ended, Action<ClientMessage> submit,
        Action? resume = null)
    {
        _defensePlayer = player; UpdateItemLevels(player); _defenseWizard = wizard; _defenseMatchEnded = ended; _defenseSubmit = submit;
        _resumeDefense = resume;
        Build(main, "");
    }

    private AccountProfile DefenseProfile(DefensePlayerSnapshot player)
    {
        var loadout = new EquipmentLoadout();
        foreach (var item in player.Items.Where(i => i.Equipped)) loadout = loadout.With(item.Item.Slot, item.Item.Id);
        return new("defense-" + player.PlayerId, _defenseWizard!.Name, player.Gold, 0, 0, loadout,
            player.Items.Select(i => i.Item).ToArray());
    }

    private void RenderDefenseActions()
    {
        if (_defensePlayer == null) return;
        _inventoryMenu.GetPopup().SetItemDisabled(0, _busy || !CanChangeDefenseItems || _defensePlayer.Gold < 100 ||
            _defensePlayer.Items.Length >= DefenseRules.InventoryLimit);
    }

    internal void UpdateDefenseInventory(DefensePlayerSnapshot player, WizardSnapshot wizard, bool ended)
    {
        _defensePlayer = player; UpdateItemLevels(player); _defenseWizard = wizard; _defenseMatchEnded = ended;
        UpdateProgressStats();
        SyncDefensePreferences();
        string stamp = player.Gold + ":" + wizard.Alive + ":" + ended + ":" + _defensePreferenceRevision + ":" +
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
            string message = _defensePendingMessage;
            _defensePending = null; _defenseConfirmed = null;
            Busy(false); Render(); SetStatus(message, routine: true);
            RefreshPickerItems();
            if (PickerVisible) { _pickerStatus.Text = message; _pickerStatus.Visible = true; }
            confirmed?.Invoke();
            if (!_closing) ContinueRequestedClose();
        }
        else if (Time.GetTicksMsec() - _defensePendingAt > 6000)
        {
            _defensePending = null; _defenseConfirmed = null; _closeRequested = false;
            _defenseBatch.Clear();
            CloseFailed(new InvalidOperationException("La partida no confirmó la operación."));
            UpdatePickerActions();
        }
    }

    private void DefenseOperation(ClientMessage command, Func<DefensePlayerSnapshot, bool> confirmed, Action? after = null,
        string message = "Equipo de partida actualizado.")
    {
        _defensePending = confirmed; _defenseConfirmed = after; _defensePendingAt = Time.GetTicksMsec();
        _defensePendingMessage = message;
        Busy(true, "Actualizando equipo de partida…"); _defenseSubmit!(command);
    }

    private void BuyDefenseItem()
    {
        if (_busy || !CanChangeDefenseItems || _defensePlayer == null || _inventoryMenu.GetPopup().IsItemDisabled(0)) return;
        var ids = _defensePlayer.Items.Select(i => i.Item.Id).ToHashSet(StringComparer.Ordinal);
        int sold = _defensePlayer.AutoSold;
        DefenseOperation(new() { Type = "defense_supply" }, p => p.Items.Any(i => !ids.Contains(i.Item.Id)) || p.AutoSold > sold,
            message: "Pieza comprada. El oro y el inventario están actualizados.");
    }

    private bool CanUpgradeDefenseItem(string id)
    {
        var entry = _defensePlayer?.Items.FirstOrDefault(i => i.Item.Id == id);
        return !_busy && CanChangeDefenseItems && entry != null && ItemAffixes.CanUpgrade(entry.Item, entry.Upgrades) && _defensePlayer!.Gold >= DefenseRules.UpgradeCost(entry.Item.Tier, entry.Upgrades);
    }

    private void UpgradeDefenseItem(string id)
    {
        if (!CanUpgradeDefenseItem(id)) return;
        int before = _defensePlayer!.Items.Single(i => i.Item.Id == id).Upgrades;
        DefenseOperation(new() { Type = "defense_upgrade", ItemId = id },
            p => p.Items.Any(i => i.Item.Id == id && i.Upgrades > before),
            message: "Mejora confirmada. Las estadísticas y el oro están actualizados.");
    }

    private void SellDefenseItem()
    {
        if (_busy || !CanSellInventory || _selling.Length == 0 || SaleProtected(_selling)) return;
        string id = _selling; _selling = "";
        SellDefenseId(id, message: "Venta confirmada. El oro y las piezas están actualizados.");
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
