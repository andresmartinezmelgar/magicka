using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private Control _itemPicker = null!;
    private Label _pickerTitle = null!, _pickerHelp = null!, _pickerEmpty = null!;
    private ContentScrollContainer _pickerScroll = null!;
    private GridContainer _pickerGrid = null!;
    private Button _unequip = null!;
    private readonly Dictionary<string, Button> _itemButtons = new(StringComparer.Ordinal);
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
        _unequip = Ui.Button("Quitar pieza equipada", () => ChooseItem(""));
        _unequip.Name = "EquipmentUnequip";
        _unequip.CustomMinimumSize = new(0, Ui.IsMobile ? 60 : 48); box.AddChild(_unequip);
    }

    private void OpenItemPicker(EquipmentSlot slot)
    {
        if (_busy) return;
        _selectedSlot = slot; Render();
        _itemButtons.Clear(); Clear(_pickerGrid);
        var choices = Choices(slot);
        string equipped = _draft?.Loadout.Get(slot) ?? "";
        _pickerTitle.Text = EquipmentCatalogue.SlotName(slot).ToUpperInvariant() + $" · {choices.Length} objetos";
        _pickerHelp.Text = "De mayor a menor calidad: Mítico, Diamante, Platino, Oro y Plata. Pulsa una pieza para equiparla y ver sus detalles.";
        foreach (var item in choices)
        {
            bool selected = item.Id == equipped;
            string title = EquipmentCatalogue.TierName(item.Tier) + " · " + EquipmentCatalogue.FamilyName(item.Family);
            string attributes = string.Join("\n", item.Bonuses.Select(b => StatName(b.Stat) + " " + EquipmentCatalogue.Percent(b.Current)));
            var button = Ui.Button(title + "\n" + attributes + "\n" + (selected ? "EQUIPADA" : "Ver y equipar"), () => ChooseItem(item.Id));
            button.Name = "EquipmentItem_" + item.Id;
            button.SetMeta("item_id", item.Id);
            button.Alignment = HorizontalAlignment.Left;
            button.MouseFilter = MouseFilterEnum.Pass;
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new(0, Ui.IsMobile ? 210 : 180);
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 18 : 17);
            button.AddThemeColorOverride("font_color", selected ? Ui.Gold : Ui.Text);
            var normal = Ui.Panel(new Color(selected ? "185184" : "122743"), 8, selected ? Ui.Gold : TierColor(item.Tier));
            normal.ContentMarginLeft = normal.ContentMarginRight = 12;
            normal.ContentMarginTop = normal.ContentMarginBottom = 12;
            button.AddThemeStyleboxOverride("normal", normal);
            button.TooltipText = EquipmentCatalogue.ItemName(item) + "\n" + attributes;
            _pickerGrid.AddChild(button); _itemButtons.Add(item.Id, button);
        }
        _pickerEmpty.Visible = choices.Length == 0;
        _unequip.Disabled = equipped.Length == 0;
        _itemPicker.Show(); _pickerScroll.ScrollVertical = 0; UpdatePickerColumns();
    }

    private void UpdatePickerColumns()
    {
        if (_pickerGrid == null) return;
        float width = _pickerScroll.Size.X > 0 ? _pickerScroll.Size.X : GetViewport().GetVisibleRect().Size.X - 100;
        _pickerGrid.Columns = Math.Clamp(ResponsiveLayout.Columns(Math.Max(0, width), 290, 3, 10), 2, 3);
    }

    private void CloseItemPicker()
    {
        _itemPicker.Hide();
        _slots[(int)_selectedSlot].GrabFocus();
    }

    public void HandleBack()
    {
        if (_itemPicker.Visible) CloseItemPicker();
        else Close();
    }

    internal void PressChoice(string id)
    {
        if (id.Length == 0) _unequip.EmitSignal(Button.SignalName.Pressed);
        else _itemButtons[id].EmitSignal(Button.SignalName.Pressed);
    }
}
