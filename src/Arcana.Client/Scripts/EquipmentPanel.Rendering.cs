using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private void UpdateEquipmentResponsive()
    {
        float width = GetViewport().GetVisibleRect().Size.X;
        _avatarPane.CustomMinimumSize = new(Math.Clamp(width * .32f, 178, 430), 0);
        bool shortView = GetViewport().GetVisibleRect().Size.Y <= 500;
        _salesHelp.Visible = !shortView;
        _totalsTitle.Visible = !shortView;
        _sets.Visible = !shortView || _sets.Text != "Sin conjuntos activos.";
        _totalsSummary.AddThemeConstantOverride("separation", shortView ? 4 : 8);
        foreach (var button in _equipmentToolbar.GetChildren().OfType<Button>())
        { button.CustomMinimumSize = new(0, 40); button.AddThemeFontSizeOverride("font_size", width < 800 ? 13 : 15); }
        foreach (var tab in new[] { _optionAItemsTab, _optionAStatsTab })
            if (tab != null)
            {
                tab.AddThemeFontSizeOverride("font_size", width < 800 ? 14 : 17);
                foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
                {
                    var style = (StyleBoxFlat)tab.GetThemeStylebox(state).Duplicate();
                    style.ContentMarginLeft = style.ContentMarginRight = 8;
                    style.ContentMarginTop = style.ContentMarginBottom = 4;
                    tab.AddThemeStyleboxOverride(state, style);
                }
            }
        _status.Visible = _status.Text.Length > 0 && !_routineStatus;
        if (_setsScroll != null) _setsScroll.CustomMinimumSize = new(Math.Max(240, Math.Min(800, width - 100)), Math.Max(150, Math.Min(400, GetViewport().GetVisibleRect().Size.Y - 180)));
        UpdatePickerColumns(); if (_confirm.Visible) ResizeSaleModal();
    }

    private void Render()
    {
        var account = _draft;
        _profile.Text = account == null ? "Sin cuenta" : $"{account.Gold} oro{(_defensePlayer == null ? "" : " temporal")} · {account.Items.Length}/{(_defensePlayer == null ? EquipmentCatalogue.InventoryLimit : DefenseRules.InventoryLimit)} objetos";
        if (BotInventory) _profile.Text = _defenseWizard!.Name + "\n" + _profile.Text;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var view = _slots[(int)slot]; var choices = Choices(slot);
            int index = Array.FindIndex(choices, i => i.Id == account?.Loadout.Get(slot));
            var item = index >= 0 ? choices[index] : null;
            _avatar.UpdateSlot(slot, item, slot == _selectedSlot, item == null ? null : ItemLevel(item));
            view.Disabled = _busy;
        }
        RenderDetails();
        RenderSavedEquipment();
        var breakdown = PublicEquipment.Prepare(account ?? new("", "", 0, 0, 0, new(), []), _itemLevels);
        _totalsEmpty.Visible = !RenderTotals(_totals, breakdown.View.Stats, breakdown.View.Items);
        UpdateProgressStats();
        var m = breakdown.Modifiers;
        var examples = new List<string>();
        if (account != null && m.Incoming != 1)
            examples.Add("Daño recibido: 100 → " + (100 * m.Incoming).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (account != null && m.ShieldCapacity != 1)
            examples.Add("Escudo básico: 420 → " + (420 * m.ShieldCapacity).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " HP");
        _combatPreview.Text = examples.Count > 0 ? (BotInventory ? "Con el equipo del bot · " : "Con tu equipo · ") + string.Join(" · ", examples) : "";
        string specialEffects = ItemAffixes.Summary(m);
        if (specialEffects.Length > 0) _combatPreview.Text += (_combatPreview.Text.Length > 0 ? "\n\n" : "") + "EFECTOS ESPECIALES ACTIVOS\n" + NumberedImprovements(specialEffects) +
            "\nLos efectos iguales usan el valor más alto; no se suman entre piezas.";
        _combatPreview.Visible = _combatPreview.Text.Length > 0;
        var activeSets = EquipmentCatalogue.FocusedFamilies.Where(f => m.Pieces(f) >= 2).ToArray();
        _sets.Text = activeSets.Length == 0 ? "Sin conjuntos activos." : "Conjuntos activos · " +
            string.Join(" · ", activeSets.Select(f => EquipmentCatalogue.FamilyName(f) + " " + m.Pieces(f) + "/4"));
        _sets.Visible = activeSets.Length > 0 || GetViewport().GetVisibleRect().Size.Y > 500;
        _sets.TooltipText = "Solo cuentan las piezas Míticas de la misma familia. Con 3 piezas sigue activo el bonus de 2. El bonus de 4 sustituye al de 2.\n"
            + string.Join("\n", activeSets.Select(f => Set(EquipmentCatalogue.FamilyName(f), m.Pieces(f), SetDescription(f, false), SetDescription(f, true))));
        _salesHelp.Text = BotInventory ? "El oro de las ventas se abona al bot. Favoritos y piezas equipadas protegidos." :
            _main.InventoryAutoSelling && !TemporaryInventory ? "Terminando la venta automática. Puedes salir de esta pantalla." :
            "Favoritos y equipados protegidos. Los Míticos solo se venden manualmente.";
        if (!_busy && _status.Text.Length == 0) SetStatus(account == null ? "Inicia sesión con Google para recuperar tu inventario y conseguir objetos en PvP." :
            BotInventory ? "Elige una pieza para actualizar el equipo del bot." :
            "Pulsa una posición para elegir una pieza. La configuración se guarda al salir del inventario.", routine: true);
        if (_defensePlayer != null) RenderDefenseActions();
    }

    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested || _defensePlayer != null || _draft?.Uid == _main.Accounts.Profile?.Uid) return;
        RebaseDraft(); _onlineInventory = _main.Accounts.SignedIn || _main.Accounts.HasDirectFirebaseSession;
        _saleSelection.Clear(); _bulkMode = false;
        CancelSale(); CloseItemPicker(); Render();
        SetStatus("Cuenta cambiada. Inventario cargado y selección reiniciada.");
    }

    private static string Set(string name, int pieces, string two, string four) => name + " " + pieces + "/4 · " + (pieces == 4 ? four : pieces >= 2 ? two : "sin bonificación");

    private int ItemLevel(EquipmentItem item) => _itemLevels.GetValueOrDefault(item.Id, item.UpgradeLevel);

    private void RenderDetails()
    {
        Clear(_details); Clear(_detailEffects); var item = Selected();
        _details.Visible = _detailTitle.Visible = false;
        _detailTitle.Text = EquipmentCatalogue.SlotName(_selectedSlot) + " · ATRIBUTOS INDIVIDUALES";
        if (item != null)
        {
            RenderItemStats(_details, item);
            EquipmentIcons.AddBadges(_detailEffects, item.Slot, item.Family, item.Affix, ItemLevel(item));
            _detailTitle.Visible = _details.Visible;
            string effects = ItemEffects(item, ItemLevel(item), _draft == null ? null : EquipmentModifiers.Calculate(_draft, _itemLevels));
            if (effects.Length > 0) AddItemEffects(_detailEffects, $"Nivel {ItemLevel(item)}/10\n" + effects);
        }
        _empty.Text = item == null ? "Sin pieza equipada. Pulsa una posición para abrir sus objetos ordenados por calidad." :
            BotInventory ? "Pulsa una posición para comparar sus objetos." :
            "Pulsa una posición para comparar sus objetos. Los cambios se guardan al salir del inventario.";
    }

    public static Color TierColor(ItemTier tier) => tier switch
    { ItemTier.Silver => new("c4cbd7"), ItemTier.Gold => new("e8c574"), ItemTier.Platinum => new("82dfd6"), ItemTier.Diamond => new("73bfff"), _ => new("cf9cff") };
}
