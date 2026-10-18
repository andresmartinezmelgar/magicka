using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private InventoryPreferences Preferences => DevicePreferences.Inventory(_draft?.Uid ?? "", TemporaryInventory);
    private readonly HashSet<string> _saleSelection = new(StringComparer.Ordinal);
    private readonly List<Button> _preferenceActions = [];
    private bool _bulkMode, _confirmingBatch;
    private Button? _bulkToggle, _bulkAll, _bulkSell;
    private HBoxContainer? _bulkRow;
    private OptionButton? _savedTeams;
    private LineEdit? _teamName;
    private Button? _applyTeam;
    private Label? _teamStatus;
    private readonly Queue<string> _defenseBatch = new();
    private int _batchSold, _batchCount;
    private long _batchGold;

    private void BuildBulkActions(VBoxContainer box, HBoxContainer heading)
    {
        var row = new HBoxContainer { Name = "EquipmentBulkActions", Visible = false }; _bulkRow = row; box.AddChild(row);
        Button Add(string text, Action action)
        {
            var button = Ui.Button(text, action); button.CustomMinimumSize = new(0, 44);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.AddThemeFontSizeOverride("font_size", 16);
            row.AddChild(button); return button;
        }
        _bulkToggle = Add("Venta múltiple", () => { if (_busy) return; _bulkMode = !_bulkMode; _saleSelection.Clear(); ResetQuickActions(); RefreshPickerItems(); });
        _bulkToggle.Reparent(heading); heading.MoveChild(_bulkToggle, 1);
        _bulkToggle.SizeFlagsHorizontal = SizeFlags.Fill;
        _bulkToggle.Name = "EquipmentBulkToggle";
        _bulkAll = Add("Marcar visibles", () =>
        {
            if (_busy || _draft == null) return;
            foreach (var id in PickerItems.Where(id => !Preferences.Protected(id) && !Equipped(id))) _saleSelection.Add(id);
            RefreshPickerItems();
        });
        _bulkAll.Name = "EquipmentBulkAll";
        _bulkSell = Add("Vender selección", ConfirmBatchSale); _bulkSell.Name = "EquipmentBulkSell";
    }
    private bool Equipped(string id) => _draft != null && Enum.GetValues<EquipmentSlot>().Any(s => _draft.Loadout.Get(s) == id);
    private void BuildItemPreferences(VBoxContainer box, EquipmentItem item)
    {
        var favorite = Ui.Button(Preferences.Protected(item.Id) ? "★ FAVORITO · protegido" : "☆ Marcar favorito", () =>
        {
            if (_busy) return;
            ResetQuickActions();
            if (!Preferences.Favorites.Remove(item.Id)) Preferences.Favorites.Add(item.Id);
            _saleSelection.Remove(item.Id);
            if (!TemporaryInventory && !DevicePreferences.SaveInventory())
                SetPickerMessage("No se pudo guardar el favorito en este dispositivo. Se mantiene durante esta sesión.");
            RefreshPickerItems();
        });
        favorite.Name = "EquipmentFavorite_" + item.Id; favorite.CustomMinimumSize = new(0, 42);
        favorite.AddThemeFontSizeOverride("font_size", 14); favorite.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(favorite); _preferenceActions.Add(favorite);
        if (!_bulkMode) return;
        var check = new CheckBox { Name = "EquipmentSelect_" + item.Id, Text = Preferences.Protected(item.Id) ? "Protegido" : Equipped(item.Id) ? "Equipada · selección manual" : "Seleccionar venta",
            ButtonPressed = _saleSelection.Contains(item.Id), Disabled = _busy || Preferences.Protected(item.Id), CustomMinimumSize = new(0, 44) };
        check.AddThemeFontSizeOverride("font_size", 14); check.ClipText = true;
        check.Toggled += selected => { if (selected) _saleSelection.Add(item.Id); else _saleSelection.Remove(item.Id); UpdateBulkActions(); };
        box.AddChild(check); _preferenceActions.Add(check);
    }
    private void SetPickerMessage(string text) { _pickerStatus.Text = text; _pickerStatus.Visible = true; }
    private void UpdatePreferenceActions()
    {
        _preferenceActions.RemoveAll(b => !GodotObject.IsInstanceValid(b) || !b.IsInsideTree());
        foreach (var button in _preferenceActions)
            button.Disabled = _busy || button is CheckBox && Preferences.Protected(button.Name.ToString()["EquipmentSelect_".Length..]);
        UpdateBulkActions();
    }
    private void UpdateBulkActions()
    {
        if (_bulkToggle == null) return;
        _bulkToggle.Disabled = _busy; _bulkToggle.Text = _bulkMode ? "Cancelar selección" : "Venta múltiple";
        _bulkAll!.Visible = _bulkSell!.Visible = _bulkMode;
        _bulkRow!.Visible = _bulkMode;
        var items = _draft == null ? [] : Preferences.Sellable(_draft, _saleSelection);
        _saleSelection.IntersectWith(items.Select(i => i.Id));
        _bulkAll.Disabled = _busy || !CanSellInventory;
        _bulkSell.Disabled = _busy || !CanSellInventory || items.Length == 0;
        _bulkSell.Text = $"Vender {items.Length} · +{items.Sum(i => (long)i.SaleGold)} oro";
        if (!CanSellInventory) SetPickerMessage(SaleBlockedReason);
    }
    private string UpgradeBlockedReason(string id)
    {
        var entry = _defensePlayer?.Items.FirstOrDefault(i => i.Item.Id == id);
        string protection = Preferences.Protected(id) ? "Favorito: venta bloqueada. " : "";
        if (entry == null) return protection + "Mejoras: solo durante Defensa.";
        if (!CanChangeDefenseItems) return protection + "Mejoras bloqueadas: mago caído o partida terminada.";
        if (entry.Upgrades >= 10 || entry.Item.Bonuses.All(b => b.Current >= b.Maximum)) return protection + "Límite de mejora alcanzado.";
        int cost = DefenseRules.UpgradeCost(entry.Item.Tier, entry.Upgrades);
        return protection + (_defensePlayer!.Gold < cost ? $"Para mejorar faltan {cost - _defensePlayer.Gold} oro." : "");
    }

    private void BuildSavedEquipment(VBoxContainer box)
    {
        var panel = new VBoxContainer { Name = "EquipmentSavedTeams" }; panel.AddThemeConstantOverride("separation", 4); box.AddChild(panel);
        var label = Ui.Label("EQUIPOS GUARDADOS · en este dispositivo", 15, Ui.Gold); panel.AddChild(label);
        var row = new HBoxContainer(); panel.AddChild(row);
        _savedTeams = new OptionButton { Name = "EquipmentSavedTeam", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new(100, 44) };
        row.AddChild(_savedTeams); _savedTeams.ItemSelected += _ => RenderSavedEquipment();
        _applyTeam = Ui.Button("Aplicar", ApplySavedTeam); _applyTeam.Name = "EquipmentApplyTeam"; row.AddChild(_applyTeam); _actions.Add(_applyTeam);
        var remove = Ui.Button("Borrar equipo", () =>
        {
            if (_busy || _savedTeams.Selected < 0 || _savedTeams.Selected >= Preferences.Loadouts.Count) return;
            Preferences.Loadouts.RemoveAt(_savedTeams.Selected); DevicePreferences.SaveInventory(); _savedTeams.Clear(); RenderSavedEquipment();
        }); row.AddChild(remove); _actions.Add(remove);
        var saveRow = new HBoxContainer(); panel.AddChild(saveRow);
        _teamName = new LineEdit { Name = "EquipmentTeamName", PlaceholderText = "Nombre del equipo", MaxLength = 24,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 44) }; saveRow.AddChild(_teamName);
        var save = Ui.Button("Guardar actual", SaveCurrentTeam); save.Name = "EquipmentSaveTeam"; saveRow.AddChild(save); _actions.Add(save);
        _teamStatus = Ui.Label("", 14, Ui.Muted); _teamStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; panel.AddChild(_teamStatus);
    }
    private void RenderSavedEquipment()
    {
        if (_savedTeams == null || _draft == null) return;
        int index = Math.Clamp(_savedTeams.Selected, 0, Math.Max(0, Preferences.Loadouts.Count - 1));
        _savedTeams.Clear();
        foreach (var saved in Preferences.Loadouts) _savedTeams.AddItem(saved.Name);
        if (Preferences.Loadouts.Count == 0)
        { _savedTeams.AddItem("Sin equipos guardados"); _applyTeam!.Disabled = true; _teamStatus!.Text = "Escribe un nombre para guardar las cuatro posiciones actuales."; return; }
        _savedTeams.Select(index);
        int missing = InventoryPreferences.Missing(Preferences.Loadouts[index], _draft);
        _applyTeam!.Disabled = _busy || missing > 0;
        _teamStatus!.Text = missing > 0 ? $"Faltan {missing} piezas de este equipo. Puedes actualizarlo guardando de nuevo con el mismo nombre." :
            "Aplicar cambia las cuatro posiciones; se guardarán al salir. Guardar el mismo nombre actualiza ese equipo.";
    }
    private void SaveCurrentTeam()
    {
        if (_busy || _draft == null) return;
        try
        {
            Preferences.Save(_teamName!.Text, _draft.Loadout, _draft);
            RenderSavedEquipment();
            _savedTeams!.Select(Preferences.Loadouts.FindIndex(l => l.Name.Equals(_teamName.Text.Trim(), StringComparison.OrdinalIgnoreCase)));
            RenderSavedEquipment();
            _teamStatus!.Text = DevicePreferences.SaveInventory() ? "Equipo guardado en este dispositivo." : "No se pudo guardar el equipo en el dispositivo.";
        }
        catch (Exception error) { _teamStatus!.Text = error.Message; }
    }
    private void ApplySavedTeam()
    {
        if (_busy || _draft == null || _savedTeams!.Selected < 0 || _savedTeams.Selected >= Preferences.Loadouts.Count) return;
        try
        {
            _draft = AccountInventory.ApplyLoadout(_draft, Preferences.Loadouts[_savedTeams.Selected].Loadout);
            ResetQuickActions(); Render(); RefreshPickerItems(); SetStatus("Equipo aplicado. Se guardará al salir del inventario.");
        }
        catch (Exception error) { _teamStatus!.Text = "No se puede aplicar: " + error.Message; }
    }

    private void ConfirmBatchSale()
    {
        if (_busy || _draft == null || !CanSellInventory) return;
        var items = Preferences.Sellable(_draft, _saleSelection);
        if (items.Length == 0) return;
        ResetQuickActions(); _confirmingBatch = true; _selling = items[0].Id; _saleOwner = _draft.Uid;
        _saleItem.Text = $"{items.Length} OBJETOS SELECCIONADOS";
        _saleItem.AddThemeColorOverride("font_color", Ui.Gold);
        _saleState.Text = $"{items.Count(i => Equipped(i.Id))} piezas equipadas se quitarán del equipo";
        Clear(_saleStats);
        foreach (var item in items)
        {
            Ui.TableCell(_saleStats, $"+{item.SaleGold}", true, Ui.Gold);
            Ui.TableCell(_saleStats, EquipmentCatalogue.ItemName(item));
        }
        long gold = items.Sum(i => (long)i.SaleGold);
        _saleGold.Text = $"+{gold} oro"; _confirm.OkButtonText = $"Vender {items.Length} · +{gold} oro";
        _saleWarning.Text = "Venta definitiva. Los favoritos están excluidos. Si se interrumpe la conexión, se conservarán los objetos pendientes.";
        MoveChild(_saleDimmer, GetChildCount() - 1); UpdateSaleDensity(); _confirm.PopupCentered(SaleModalSize());
        _confirm.GetCancelButton().GrabFocus();
    }
    private async Task SellBatch()
    {
        if (_draft == null || !CanSellInventory || _saleOwner != _draft.Uid ||
            !TemporaryInventory && _saleOwner != _main.Accounts.Profile?.Uid) { CancelSale(); return; }
        var items = Preferences.Sellable(_draft, _saleSelection);
        string owner = _saleOwner;
        _confirm.Hide(); _confirmingBatch = false; _selling = "";
        _batchSold = _batchCount = 0; _batchGold = 0;
        _batchCount = items.Length;
        if (TemporaryInventory)
        {
            foreach (var item in items) _defenseBatch.Enqueue(item.Id);
            SellNextDefenseBatch(); return;
        }
        Busy(true, $"Vendiendo 0/{items.Length} objetos…");
        string failure = "";
        foreach (var item in items)
        {
            if (_lifetime.IsCancellationRequested) return;
            if (_main.Accounts.Profile?.Uid != owner) { failure = "La cuenta ha cambiado."; break; }
            if (Preferences.Protected(item.Id)) continue;
            try
            {
                await _main.Accounts.Sell(item.Id, _lifetime.Token);
                _batchSold++; _batchGold += item.SaleGold;
                SaleProgress(owner, _batchSold, _batchCount, _batchGold);
            }
            catch (Exception error)
            {
                failure = error.Message;
                if (await RecoverSaleState(owner))
                {
                    var remaining = _main.Accounts.Profile!.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
                    var sold = items.Where(i => !remaining.Contains(i.Id)).ToArray();
                    _batchSold = sold.Length; _batchGold = sold.Sum(i => (long)i.SaleGold);
                }
                break;
            }
        }
        _main.Post(() =>
        {
            if (_lifetime.IsCancellationRequested) return;
            _main.SaveAccountCache(); RebaseDraft(); Render();
            string result = $"Vendidos {_batchSold}/{_batchCount} objetos · +{_batchGold} oro confirmado." +
                (_main.Accounts.InventoryNeedsRefresh ? " Venta sin confirmar. Actualiza el inventario antes de volver a vender." :
                failure.Length > 0 && _batchSold < _batchCount ? " " + failure + " Los objetos restantes siguen en el inventario." : "");
            Busy(false, result); RefreshPickerItems(); SetPickerMessage(result); ContinueRequestedClose();
        });
    }
    private void SellNextDefenseBatch()
    {
        if (!CanSellInventory) _defenseBatch.Clear();
        while (_defenseBatch.Count > 0)
        {
            string id = _defenseBatch.Dequeue();
            var item = _draft?.Items.FirstOrDefault(i => i.Id == id);
            if (item == null || Preferences.Protected(id)) continue;
            DefenseOperation(new() { Type = "defense_sell", ItemId = id }, p => p.Items.All(i => i.Item.Id != id), () =>
            {
                _saleSelection.Remove(id); _batchSold++; _batchGold += item.SaleGold; SellNextDefenseBatch();
            }, $"Vendidos {_batchSold + 1}/{_batchCount} objetos.");
            return;
        }
        string result = $"Vendidos {_batchSold}/{_batchCount} objetos · +{_batchGold} oro.";
        Busy(false, result); RefreshPickerItems(); SetPickerMessage(result);
    }
}
