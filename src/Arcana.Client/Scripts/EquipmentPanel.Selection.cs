using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private Control _itemPicker = null!;
    private Label _pickerTitle = null!, _pickerHelp = null!, _pickerEmpty = null!, _pickerStatus = null!;
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
        _itemPicker = new Control { Name = "EquipmentItemPicker", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        AddChild(_itemPicker); Ui.Full(_itemPicker);
        var backdrop = new ColorRect { Color = new(0.01f, 0.02f, 0.04f, 0.97f) };
        _itemPicker.AddChild(backdrop); Ui.Full(backdrop);
        var margin = new MarginContainer(); _itemPicker.AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 12 : 24);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0a1d33"), 11, new Color("2b719d")));
        margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); panel.AddChild(box);
        var heading = new HBoxContainer(); heading.AddThemeConstantOverride("separation", 12); box.AddChild(heading);
        _pickerTitle = Ui.Label("", Ui.IsMobile ? 24 : 25, Ui.Gold);
        _pickerTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pickerTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill; heading.AddChild(_pickerTitle);
        var close = Ui.Button("Volver al inventario", CloseItemPicker);
        close.CustomMinimumSize = new(0, Ui.IsMobile ? 60 : 48); heading.AddChild(close);
        _pickerHelp = Ui.Label("", Ui.IsMobile ? 17 : 16, Ui.Muted);
        _pickerHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_pickerHelp);
        BuildPickerFilters(box);
        _pickerStatus = Ui.Label("", 16, Ui.Gold); _pickerStatus.Name = "EquipmentPickerStatus";
        _pickerStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart; _pickerStatus.Visible = false; box.AddChild(_pickerStatus);
        _pickerScroll = new ContentScrollContainer { Name = "EquipmentPickerScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(_pickerScroll); Ui.VisibleVerticalScroll(_pickerScroll);
        _pickerGrid = new GridContainer { Name = "EquipmentPickerGrid", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _pickerGrid.AddThemeConstantOverride("h_separation", 10);
        _pickerGrid.AddThemeConstantOverride("v_separation", 10); _pickerScroll.AddChild(_pickerGrid);
        _pickerScroll.Resized += UpdatePickerColumns;
        _pickerEmpty = Ui.Label("No tienes objetos para esta posición.", 19, Ui.Muted);
        _pickerEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_pickerEmpty);
        BuildQuickActions();
    }

    private void OpenItemPicker(EquipmentSlot slot)
    {
        if (_busy) return;
        if (_selectedSlot != slot) ResetQuickActions();
        _selectedSlot = slot; Render();
        _itemButtons.Clear(); _trashButtons.Clear(); _upgradeButtons.Clear(); Clear(_pickerGrid);
        var allChoices = Choices(slot);
        var choices = allChoices.Where(PickerFilter.Matches).ToArray();
        string equipped = _draft?.Loadout.Get(slot) ?? "";
        _pickerTitle.Text = EquipmentCatalogue.SlotName(slot).ToUpperInvariant() + $" · {choices.Length}/{allChoices.Length} objetos";
        _pickerHelp.Text = "De mayor a menor calidad. Iconos de cada pieza: vender, equipar o quitar y mejorar." +
            (_defensePlayer == null ? " Las mejoras con oro están disponibles en Defensa." : $" Oro de partida: {_defensePlayer.Gold}.");
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
            var equippedLabel = Ui.Label(selected ? "EQUIPADA" : "Sin equipar", 15, selected ? Ui.Gold : Ui.Muted);
            equippedLabel.MouseFilter = MouseFilterEnum.Ignore; description.AddChild(equippedLabel);
            string attributes = string.Join("\n", item.Bonuses.Select(b => StatName(b.Stat) + " " + EquipmentCatalogue.Percent(b.Current)));
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
            var equip = PickerAction(actions, "EquipmentEquip_", item.Id, _equipIcon, selected ? Ui.Gold : Ui.Blue,
                selected ? "Quitar" : "Equipar", selected ? "Quitar esta pieza equipada." : "Equipar esta pieza. Se guardará al salir del inventario.",
                () => ChooseItem(selected ? "" : item.Id));
            _itemButtons.Add(item.Id, equip);
            bool maximum = entry != null && (entry.Upgrades >= 10 || item.Bonuses.All(b => b.Current >= b.Maximum));
            int cost = maximum ? 0 : DefenseRules.UpgradeCost(item.Tier, entry?.Upgrades ?? 0);
            var upgrade = PickerAction(actions, "EquipmentUpgrade_", item.Id, _upgradeIcon, Ui.Health,
                entry == null ? "Defensa" : maximum ? "Máximo" : $"{cost} oro",
                entry == null ? "Mejorar estadísticas con oro durante una partida de Defensa." : maximum ? "Esta pieza ha alcanzado su límite de mejora." :
                $"Mejorar esta pieza por {cost} oro de partida. Suma un punto porcentual a cada atributo; diez puntos sobre su valor inicial. Mejora {entry.Upgrades + 1} / 10." +
                (_defensePlayer!.Gold < cost ? $" Faltan {cost - _defensePlayer.Gold} oro." : ""), () => ClickUpgrade(item.Id));
            _upgradeButtons.Add(item.Id, upgrade);
        }
        _pickerEmpty.Text = allChoices.Length == 0 ? "No tienes objetos para esta posición." : "No hay objetos que coincidan con estos filtros.";
        _pickerEmpty.Visible = choices.Length == 0;
        UpdatePickerActions();
        _itemPicker.Show(); _pickerScroll.ScrollVertical = 0; UpdatePickerColumns();
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
        foreach (var button in _itemButtons.Values) button.Disabled = _busy || (_defensePlayer != null && !CanChangeDefenseItems);
        foreach (var button in _trashButtons.Values) button.Disabled = _busy || !CanSellInventory;
        foreach (var (id, button) in _upgradeButtons) button.Disabled = !CanUpgradeDefenseItem(id);
    }

    private void RefreshPickerItems()
    {
        if (_itemPicker == null || !_itemPicker.Visible || _busy) return;
        int scroll = _pickerScroll.ScrollVertical;
        OpenItemPicker(_selectedSlot); _pickerScroll.ScrollVertical = scroll;
    }

    internal void PressTrash(string id) => _trashButtons[id].EmitSignal(Button.SignalName.Pressed);
    internal void PressUpgrade(string id) => _upgradeButtons[id].EmitSignal(Button.SignalName.Pressed);

    private void UpdatePickerColumns()
    {
        if (_pickerGrid == null) return;
        float width = _pickerScroll.Size.X > 0 ? _pickerScroll.Size.X : GetViewport().GetVisibleRect().Size.X - 100;
        _pickerGrid.Columns = Math.Clamp(ResponsiveLayout.Columns(Math.Max(0, width), 290, 3, 10), 2, 3);
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
        if (_itemPicker.Visible) CloseItemPicker();
        else Close();
    }

    internal void PressChoice(string id)
    {
        string chosen = id.Length == 0 ? _draft?.Loadout.Get(_selectedSlot) ?? "" : id;
        if (chosen.Length > 0) _itemButtons[chosen].EmitSignal(Button.SignalName.Pressed);
    }
}
