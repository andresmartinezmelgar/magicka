using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private OptionButton _familyFilter = null!, _tierFilter = null!, _statFilter = null!, _sort = null!;
    private EquipmentFilter PickerFilter => new(
        _familyFilter.Selected > 0 ? (EquipmentFamily)(_familyFilter.GetSelectedId() - 1) : null,
        _tierFilter.Selected > 0 ? (ItemTier)(_tierFilter.GetSelectedId() - 1) : null,
        _statFilter.Selected > 0 ? (EquipmentStat)(_statFilter.GetSelectedId() - 1) : null);

    private void BuildPickerFilters(VBoxContainer box)
    {
        var row = new HBoxContainer { Name = "EquipmentFilters" }; box.AddChild(row);
        OptionButton Filter(string name, string all)
        {
            var choice = new OptionButton { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new(0, 46), ClipText = true };
            choice.AddItem(all, 0); choice.AddThemeFontSizeOverride("font_size", 16);
            choice.ItemSelected += _ => { ResetQuickActions(); choice.TooltipText = choice.GetItemText(choice.Selected); if (!_busy) OpenItemPicker(_selectedSlot); };
            row.AddChild(choice); return choice;
        }
        _familyFilter = Filter("EquipmentFamilyFilter", "Elemento: todos");
        foreach (var family in Enum.GetValues<EquipmentFamily>()) _familyFilter.AddItem(EquipmentCatalogue.FamilyName(family), (int)family + 1);
        _tierFilter = Filter("EquipmentTierFilter", "Calidad: todas");
        foreach (var tier in Enum.GetValues<ItemTier>()) _tierFilter.AddItem(EquipmentCatalogue.TierName(tier), (int)tier + 1);
        _statFilter = Filter("EquipmentStatFilter", "Atributo: todos");
        foreach (var stat in Enum.GetValues<EquipmentStat>()) _statFilter.AddItem(StatName(stat), (int)stat + 1);
        _sort = Filter("EquipmentSort", "Orden: calidad");
        foreach (var stat in Enum.GetValues<EquipmentStat>()) _sort.AddItem("↓ " + StatName(stat), (int)stat + 1);
    }

    private void AddComparison(VBoxContainer description, EquipmentItem item)
    {
        if (_draft == null || _draft.Loadout.Get(item.Slot) == item.Id) return;
        var label = Ui.Label("EQUIPO AL CAMBIAR ESTA PIEZA\n" + EquipmentQuery.Preview(_draft, item).Describe(),
            Ui.IsMobile ? 16 : 15, Ui.Muted);
        label.Name = "EquipmentComparison_" + item.Id;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.MouseFilter = MouseFilterEnum.Pass;
        description.AddChild(label);
    }
}
