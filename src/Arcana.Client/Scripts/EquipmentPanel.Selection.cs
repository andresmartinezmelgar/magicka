using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private Control _itemPicker = null!;
    private Label _pickerHelp = null!, _pickerEmpty = null!, _pickerStatus = null!;
    private ContentScrollContainer _pickerScroll = null!;
    private GridContainer _pickerGrid = null!;
    private readonly Dictionary<string, Button> _itemButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _trashButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _upgradeButtons = new(StringComparer.Ordinal);
    private static Texture2D? _trashIcon, _equipIcon, _upgradeIcon;
    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_trashIcon), nameof(_equipIcon), nameof(_upgradeIcon))]
    internal static void PrepareIcons()
    {
        _trashIcon ??= GD.Load<Texture2D>("res://Assets/Inventory/trash.svg") ?? throw new InvalidDataException("No se pudo cargar el icono de venta.");
        _equipIcon ??= GD.Load<Texture2D>("res://Assets/Inventory/equip.svg") ?? throw new InvalidDataException("No se pudo cargar el icono de equipar.");
        _upgradeIcon ??= GD.Load<Texture2D>("res://Assets/Inventory/upgrade.svg") ?? throw new InvalidDataException("No se pudo cargar el icono de mejora.");
    }
    internal bool PickerVisible => _itemPicker.Visible;
    internal int PickerColumns => _pickerGrid.Columns;
    internal string[] PickerItems => _itemButtons.Keys.ToArray();

    private void BuildItemPicker()
    {
        var box = new VBoxContainer { Name = "EquipmentItemPicker" }; _itemPicker = box;
        box.AddThemeConstantOverride("separation", 6); _rightHost.AddChild(box); Ui.Full(box);
        BuildBulkActions(box);
        _pickerHelp = Ui.Label("", 13, Ui.Muted); _pickerHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pickerStatus = Ui.Label("", 14, Ui.Gold); _pickerStatus.Name = "EquipmentPickerStatus"; _pickerStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; _pickerStatus.Visible = false;
        _pickerScroll = new ContentScrollContainer { Name = "EquipmentPickerScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_pickerScroll); Ui.VisibleVerticalScroll(_pickerScroll);
        var body = new VBoxContainer { Name = "EquipmentPickerBody", SizeFlagsHorizontal = SizeFlags.ExpandFill }; _pickerScroll.AddChild(body);
        body.AddThemeConstantOverride("separation", 8); body.AddChild(_pickerStatus);
        _pickerGrid = new GridContainer { Name = "EquipmentPickerGrid", Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pickerGrid.AddThemeConstantOverride("h_separation", 10); _pickerGrid.AddThemeConstantOverride("v_separation", 10); body.AddChild(_pickerGrid);
        _pickerEmpty = Ui.Label("No tienes objetos para esta zona.", 17, Ui.Muted); _pickerEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_pickerEmpty); body.AddChild(_pickerHelp);
        _pickerScroll.Resized += UpdatePickerColumns; BuildQuickActions();
    }

    private void OpenItemPicker(EquipmentSlot slot)
    {
        if (_busy) return;
        if (_selectedSlot != slot) ResetQuickActions();
        _selectedSlot = slot; Render();
        _itemButtons.Clear(); _trashButtons.Clear(); _upgradeButtons.Clear(); Clear(_pickerGrid);
        var allChoices = Choices(slot);
        string equipped = _draft?.Loadout.Get(slot) ?? "";
        var choices = EquipmentQuery.Sort(allChoices, equippedId: equipped);
        _optionAItemsTab!.Text = EquipmentCatalogue.SlotName(slot) + $" · {choices.Length}";
        _pickerHelp.Text = "Equipada primero · Favoritos y piezas equipadas protegidos de la venta.";
        PrepareIcons();
        foreach (var item in choices)
        {
            bool selected = item.Id == equipped;
            string title = EquipmentCatalogue.TierName(item.Tier) + " · " + EquipmentCatalogue.FamilyName(item.Family);
            var card = new PanelContainer { Name = "EquipmentCard_" + item.Id, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var style = Ui.Panel(new Color(selected ? "185184" : "122743"), 8, selected ? Ui.Gold : TierColor(item.Tier));
            style.ContentMarginLeft = style.ContentMarginRight = 10;
            style.ContentMarginTop = style.ContentMarginBottom = 10;
            card.AddThemeStyleboxOverride("panel", style); _pickerGrid.AddChild(card);
            var content = new HBoxContainer(); content.AddThemeConstantOverride("separation", 10); card.AddChild(content);
            var description = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            description.AddThemeConstantOverride("separation", 8); content.AddChild(description);
            var label = Ui.Label(title, Ui.IsMobile ? 18 : 17, selected ? Ui.Gold : TierColor(item.Tier));
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.MouseFilter = MouseFilterEnum.Ignore; description.AddChild(label);
            BuildItemPreferences(description, item);
            var equippedLabel = Ui.Label(selected ? "EQUIPADA" : "Sin equipar", 15, selected ? Ui.Gold : Ui.Muted);
            equippedLabel.MouseFilter = MouseFilterEnum.Ignore; description.AddChild(equippedLabel);
            string attributes = string.Join("\n", item.Bonuses.Select(b => StatName(b.Stat) + " " + EquipmentCatalogue.Percent(b.Current)));
            if (RingEffects.IsRing(item.Slot)) attributes += "\n" + RingEffects.Describe(item, ItemLevel(item));
            if (item.Affix != null) attributes += "\nEspecial: " + ItemAffixes.Describe(item.Affix, ItemLevel(item), item.Slot);
            var stats = Ui.Label(attributes, Ui.IsMobile ? 18 : 17);
            stats.Name = "EquipmentItemStats_" + item.Id;
            stats.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            stats.SizeFlagsHorizontal = SizeFlags.ExpandFill; stats.MouseFilter = MouseFilterEnum.Pass;
            stats.TooltipText = string.Join("\n", item.Bonuses.Select(b => StatName(b.Stat) + ": " +
                EquipmentCatalogue.Percent(b.Current) + " (" + EquipmentCatalogue.Percent(b.Minimum) + " / " + EquipmentCatalogue.Percent(b.Maximum) + ")"));
            description.AddChild(stats);
            AddComparison(description, item);
            var entry = _defensePlayer?.Items.FirstOrDefault(i => i.Item.Id == item.Id);
            if (entry != null) description.AddChild(Ui.Label($"Mejoras: {entry.Upgrades} / 10", 15, Ui.Muted));
            var actions = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            actions.AddThemeConstantOverride("separation", 8); content.AddChild(actions);
            var trash = PickerAction(actions, "EquipmentTrash_", item.Id, _trashIcon, Ui.Red, $"+{item.SaleGold} oro",
                $"Vender esta pieza por {item.SaleGold} oro" + (_defensePlayer != null ? " de esta partida." : ".") +
                (!CanSellInventory ? " La venta requiere iniciar sesión y disponer del servicio de ventas." : "") +
                (Ui.IsMobile ? " Doble toque para vender sin confirmar." : " Doble clic para vender sin confirmar."), () => ClickSell(item.Id));
            trash.SetMeta("sale_item_id", item.Id);
            _trashButtons.Add(item.Id, trash);
            if (SaleEquipped(item.Id)) trash.TooltipText = "Pieza equipada: venta bloqueada. Elige otra pieza para poder vender esta.";
            else if (Preferences.Protected(item.Id)) trash.TooltipText = "Favorito: venta bloqueada. Desmarca el favorito para poder vender esta pieza.";
            var equip = PickerAction(actions, "EquipmentEquip_", item.Id, _equipIcon, selected ? Ui.Gold : Ui.Blue,
                selected ? "Quitar" : "Equipar", selected ? "Quitar esta pieza equipada." : "Equipar esta pieza. Se guardará al salir del inventario.",
                () => ChooseItem(selected ? "" : item.Id));
            _itemButtons.Add(item.Id, equip);
            int itemLevel = entry?.Upgrades ?? item.UpgradeLevel;
            bool supportsUpgrade = entry != null || RingEffects.IsRing(item.Slot);
            bool maximum = supportsUpgrade && !ItemAffixes.CanUpgrade(item, itemLevel);
            int cost = maximum ? 0 : DefenseRules.UpgradeCost(item.Tier, itemLevel);
            string currency = entry == null ? "de cuenta" : "de partida";
            long availableGold = _defensePlayer?.Gold ?? _draft?.Gold ?? 0;
            var upgrade = PickerAction(actions, "EquipmentUpgrade_", item.Id, _upgradeIcon, Ui.Health,
                !supportsUpgrade ? "Defensa" : maximum ? "Máximo" : $"{cost} oro",
                !supportsUpgrade ? "Mejorar estadísticas con oro durante una partida de Defensa." : maximum ? "Esta pieza ha alcanzado su límite de mejora." :
                $"Mejorar por {cost} oro {currency}. Mejora {itemLevel + 1} / 10." +
                (RingEffects.IsRing(item.Slot) ? " El efecto del anillo progresa con su nivel." : "") +
                (availableGold < cost ? $" Faltan {cost - availableGold} oro." : ""), () => ClickUpgrade(item.Id));
            _upgradeButtons.Add(item.Id, upgrade);
            string blocked = UpgradeBlockedReason(item.Id);
            if (blocked.Length > 0)
            {
                var reason = Ui.Label(blocked, 14, Ui.Muted); reason.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                description.AddChild(reason);
            }
        }
        _pickerEmpty.Text = "No tienes objetos para esta zona.";
        _pickerEmpty.Visible = choices.Length == 0;
        UpdatePickerActions();
        UpdateBulkActions();
        SwitchOptionAEquipment(false); _pickerScroll.ScrollVertical = 0; UpdatePickerColumns();
    }

    private static Button PickerAction(VBoxContainer actions, string prefix, string id, Texture2D icon, Color color,
        string caption, string tooltip, Action pressed)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 2); actions.AddChild(box);
        var button = Ui.Button("", pressed);
        button.Name = prefix + id; button.SetMeta("item_id", id);
        button.Icon = icon; button.ExpandIcon = true; button.AddThemeConstantOverride("icon_max_width", 25);
        button.AddThemeColorOverride("icon_normal_color", color);
        button.CustomMinimumSize = new(Ui.IsMobile ? 64 : 48, Ui.IsMobile ? 64 : 48);
        button.MouseFilter = MouseFilterEnum.Pass; button.TooltipText = tooltip; box.AddChild(button);
        var label = Ui.Label(caption, 14, color); label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore; box.AddChild(label);
        return button;
    }

    private void UpdatePickerActions()
    {
        foreach (var button in _itemButtons.Values) button.Disabled = InventoryLocked || (_defensePlayer != null && !CanChangeDefenseItems);
        foreach (var (id, button) in _trashButtons) button.Disabled = InventoryLocked || !CanSellInventory || SaleProtected(id);
        foreach (var (id, button) in _upgradeButtons) button.Disabled = !CanUpgradeItem(id);
        UpdatePreferenceActions();
    }

    private void RefreshPickerItems()
    {
        if (_itemPicker == null || !_itemPicker.Visible || InventoryLocked) return;
        int scroll = _pickerScroll.ScrollVertical;
        OpenItemPicker(_selectedSlot); _pickerScroll.ScrollVertical = scroll;
    }

    internal void PressTrash(string id) => _trashButtons[id].EmitSignal(Button.SignalName.Pressed);
    internal void PressUpgrade(string id) => _upgradeButtons[id].EmitSignal(Button.SignalName.Pressed);

    private void UpdatePickerColumns()
    {
        if (_pickerGrid == null) return;
        float width = _pickerScroll.Size.X > 0 ? _pickerScroll.Size.X : GetViewport().GetVisibleRect().Size.X - 100;
        _pickerGrid.Columns = ResponsiveLayout.Columns(Math.Max(0, width), 290, 3, 10);
    }

    private void CloseItemPicker()
    {
        ResetQuickActions();
        _itemPicker.Hide();
        _slots[(int)_selectedSlot].GrabFocus();
    }

    public void HandleBack()
    {
        if (_confirm.Visible) { CancelSale(); return; }
        if (_setsInfo.Visible) { _setsInfo.Hide(); return; }
        if (_savedEquipmentDialog?.Visible == true) { _savedEquipmentDialog.Hide(); return; }
        if (_autoSellDialog?.Visible == true) { _autoSellDialog.Hide(); return; }
        if (_discardExit?.Visible == true) { _discardExit.Hide(); return; }
        if (_showingTotals) { OpenItemPicker(_selectedSlot); return; }
        if (_itemPicker.Visible) CloseItemPicker();
        else Close();
    }

    internal void PressChoice(string id)
    {
        string chosen = id.Length == 0 ? _draft?.Loadout.Get(_selectedSlot) ?? "" : id;
        if (chosen.Length > 0) _itemButtons[chosen].EmitSignal(Button.SignalName.Pressed);
    }
}
