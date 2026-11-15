using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class DefensePanel
{
    private readonly record struct EquippedState(string Id, int Level, bool CanImprove, bool Affordable);
    private sealed record EquippedCell(Button Button, TextureRect Piece, TextureRect Effect,
        PanelContainer EffectFrame, ElementIcon RingElement, ElementTextLabel Level);
    private readonly Dictionary<EquipmentSlot, EquippedCell> _equippedCells = [];
    private readonly Dictionary<EquipmentSlot, EquippedState> _equippedStates = [];
    internal int EquippedStripSlotsForVerification => _equippedCells.Count;

    private void BuildEquippedStrip(VBoxContainer parent)
    {
        var strip = new HBoxContainer { Name = "SurvivalEquippedItems" };
        strip.AddThemeConstantOverride("separation", 2); parent.AddChild(strip);
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var button = new Button { Name = "SurvivalEquipped_" + slot, CustomMinimumSize = new(42, 60),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None,
                TooltipText = EquipmentCatalogue.SlotName(slot) + " · Sin equipar. Pulsa para ver esta ranura." };
            strip.AddChild(button);
            button.Pressed += () => OpenEquippedSlot(slot);
            var body = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            body.AddThemeConstantOverride("separation", 0); button.AddChild(body);
            body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            body.OffsetLeft = body.OffsetRight = 0; body.OffsetTop = 3; body.OffsetBottom = -3;
            var art = new Control { CustomMinimumSize = new(0, 35), MouseFilter = MouseFilterEnum.Ignore }; body.AddChild(art);
            var piece = EquipmentIcons.View(EquipmentIcons.Slot(slot), 0, Ui.Muted);
            piece.Position = new(4, 1); piece.Size = new(28, 28); art.AddChild(piece);
            var frame = new PanelContainer { Position = new(23, 15), Size = new(19, 19),
                MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            var style = Ui.Panel(new("0b192b"), 4); style.ContentMarginLeft = style.ContentMarginRight = 1;
            style.ContentMarginTop = style.ContentMarginBottom = 1; frame.AddThemeStyleboxOverride("panel", style); art.AddChild(frame);
            var effect = EquipmentIcons.View(EquipmentIcons.Slot(slot), 17); frame.AddChild(effect);
            var ringElement = new ElementIcon { CustomMinimumSize = new(17, 17), Visible = false }; frame.AddChild(ringElement);
            var level = Ui.Label("—", 12, Ui.Muted); level.HorizontalAlignment = HorizontalAlignment.Center;
            level.MouseFilter = MouseFilterEnum.Ignore; body.AddChild(level);
            _equippedCells.Add(slot, new(button, piece, effect, frame, ringElement, level));
        }
    }

    private void OpenEquippedSlot(EquipmentSlot slot)
    {
        if (_player == null || _wizard == null) return;
        OpenShop(); ShowSection(1); _equipmentInventory?.PressInspect(slot);
    }

    private void UpdateEquippedStrip(DefensePlayerSnapshot player)
    {
        foreach (var (slot, cell) in _equippedCells)
        {
            var entry = player.Items.FirstOrDefault(i => i.Equipped && i.Item.Slot == slot);
            bool canImprove = entry != null && ItemAffixes.CanUpgrade(entry.Item, entry.Upgrades);
            int price = canImprove ? DefenseRules.SurvivalUpgradeCost(entry!.Item.Tier, entry.Upgrades) : 0;
            var state = new EquippedState(entry?.Item.Id ?? "", entry?.Upgrades ?? 0, canImprove, canImprove && player.Gold >= price);
            if (_equippedStates.GetValueOrDefault(slot) == state) continue;
            _equippedStates[slot] = state;
            cell.Piece.Modulate = entry == null ? new Color(Ui.Muted, .35f) : EquipmentIcons.FamilyColor(entry.Item.Family);
            cell.Level.Text = entry == null ? "—" : $"{entry.Upgrades}/10" + (state.Affordable ? "+" : "");
            cell.Level.AddThemeColorOverride("font_color", state.Affordable ? Ui.Gold : Ui.Muted);
            var border = entry == null ? new Color("30445b") : EquipmentPanel.TierColor(entry.Item.Tier);
            foreach (string mode in new[] { "normal", "hover", "pressed" })
            {
                var style = Ui.Panel(new(mode == "normal" ? "101f33" : "213853"), 5, mode == "normal" ? border : Ui.Gold);
                style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 2;
                cell.Button.AddThemeStyleboxOverride(mode, style);
            }
            var active = entry == null ? null : ItemAffixes.Active(entry.Item.Affix, slot);
            cell.EffectFrame.Visible = active != null || entry != null && RingEffects.IsRing(slot);
            cell.RingElement.Value = slot == EquipmentSlot.MagicRing && entry != null ? RingEffects.ElementFor(entry.Item.Family) : null;
            cell.RingElement.Visible = cell.RingElement.Value != null;
            cell.Effect.Visible = !cell.RingElement.Visible;
            if (cell.EffectFrame.Visible)
            {
                cell.Effect.Texture = active != null ? EquipmentIcons.Affix(active.Kind) : slot == EquipmentSlot.GoldRing
                    ? EquipmentIcons.Affix(ItemAffixKind.CoinDividend) : EquipmentIcons.Slot(EquipmentSlot.MagicRing);
                cell.Effect.Modulate = active != null || slot == EquipmentSlot.GoldRing ? Ui.Gold : EquipmentIcons.FamilyColor(entry!.Item.Family);
            }
            cell.Button.TooltipText = entry == null ? EquipmentCatalogue.SlotName(slot) + " · Sin equipar. Pulsa para ver esta ranura."
                : EquipmentCatalogue.SlotName(slot) + " · " + EquipmentCatalogue.TierName(entry.Item.Tier) + " · " +
                    EquipmentCatalogue.FamilyName(entry.Item.Family) + $"\nMejoras: {entry.Upgrades}/10" +
                    (canImprove ? $" · Siguiente: {price} de oro" : " · Sin mejoras disponibles") + "\n" +
                    string.Join("\n", entry.Item.Bonuses.Where(b => EquipmentCatalogue.BonusActive(slot, b.Stat))
                        .Select(b => EquipmentCatalogue.StatName(b.Stat) + ": " + EquipmentCatalogue.Percent(b.Current))) + "\n" +
                    EquipmentPanel.ItemEffects(entry.Item, entry.Upgrades, player.Modifiers) + "\nPulsa para ver la pieza y sus mejoras.";
        }
    }
}
