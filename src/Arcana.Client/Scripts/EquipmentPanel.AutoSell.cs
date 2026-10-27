using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private AcceptDialog? _autoSellDialog;
    private VBoxContainer? _autoSellList;
    private bool InventoryLocked => _busy || !TemporaryInventory && _main.InventoryAutoSelling;

    private EquipmentStyle? AutoSellLimit(EquipmentStyle style) => (TemporaryInventory
        ? _defensePlayer!.AutoSellStyles : Preferences.AutoSellRules.Select(r => r.Style))
        .Where(s => s.SameKind(style)).Select(s => (EquipmentStyle?)s).FirstOrDefault();

    private bool AutoSells(EquipmentStyle style) => AutoSellLimit(style) is { } limit && limit.Tier >= style.Tier;

    private void BuildAutoSellButton(VBoxContainer box, EquipmentItem item)
    {
        var style = EquipmentStyle.Of(item);
        bool active = AutoSells(style);
        var button = Ui.Button(active ? "Mantener ítems de menor calidad" : "Vender ítems de menor calidad",
            () => SetAutoSell(style, !AutoSells(style), item.Id));
        button.Name = "EquipmentAutoSell_" + item.Id; button.CustomMinimumSize = new(0, 44);
        button.Disabled = style.Tier == ItemTier.Silver && !active;
        button.MouseFilter = MouseFilterEnum.Pass;
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.AddThemeFontSizeOverride("font_size", 14);
        var displayedLimit = active ? AutoSellLimit(style) ?? style : style;
        button.TooltipText = displayedLimit.DescribeAutoSell() +
            (active ? "\nPulsa para desactivar esta regla de venta automática.\n" : "\nPulsa para activar esta regla de venta automática.\n") +
            "Vende solo las piezas nuevas de la misma ranura y familia, con calidad estrictamente inferior al límite indicado.\n" +
            "Conserva las de calidad igual o superior, las que ya tienes, los favoritos y el equipo. Sustituye el límite anterior para esta ranura y familia.\n" +
            "Puedes desactivarlo desde Opciones → Venta automática.";
        if (TemporaryInventory) button.TooltipText += "\n" + DefenseRules.InventoryFullNotice;
        if (style.Tier == ItemTier.Silver && !active) button.TooltipText += "\nPlata es la calidad mínima; este ajuste no se puede activar.";
        box.AddChild(button); if (style.Tier > ItemTier.Silver || active) _preferenceActions.Add(button);
        if (AutoSellLimit(style) is { } limit)
        {
            var status = Ui.Label("Venta automática activa · " + limit.DescribeAutoSell() + ". Solo botín nuevo.", 14, Ui.Muted);
            status.Name = "EquipmentAutoSellStatus_" + item.Id;
            status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(status);
        }
    }

    private void SetAutoSell(EquipmentStyle style, bool enabled, string itemId = "")
    {
        if (InventoryLocked || _draft == null || enabled && style.Tier == ItemTier.Silver) return;
        ResetQuickActions();
        string message = enabled ? "Venta automática activada · " + style.DescribeAutoSell() + ". Solo botín nuevo; los objetos que ya tienes se conservan."
            : "Venta automática desactivada para " + EquipmentCatalogue.SlotName(style.Slot) + " · " + EquipmentCatalogue.FamilyName(style.Family) +
                (TemporaryInventory ? ". Las piezas nuevas se conservarán mientras haya espacio." : ". Se conservará el botín nuevo.");
        if (TemporaryInventory)
        {
            message += " " + DefenseRules.InventoryFullNotice;
            DefenseOperation(new() { Type = "defense_auto_sell", AutoSellStyle = style, ItemId = itemId, Slot = enabled ? 1 : 0 },
                p => enabled ? p.AutoSellStyles.Contains(style) : !p.AutoSellStyles.Any(s => s.SameKind(style)),
                () => { RefreshPickerItems(); RenderAutoSellRules(); }, message);
            return;
        }
        Preferences.SetAutoSell(style, enabled, _draft);
        bool saved = DevicePreferences.SaveInventory();
        RefreshPickerItems(); RenderAutoSellRules();
        SetPickerMessage(saved ? message : "No se pudo guardar el ajuste en el dispositivo.", bringIntoView: !saved);
    }

    private void OpenAutoSellRules()
    {
        if (_autoSellDialog == null)
        {
            _autoSellDialog = new AcceptDialog { Name = "EquipmentAutoSellDialog", Title = "Venta automática", OkButtonText = "Cerrar" };
            AddChild(_autoSellDialog); Ui.ConfigureDialog(_autoSellDialog);
            var scroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            _autoSellDialog.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
            _autoSellList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _autoSellList.AddThemeConstantOverride("separation", 10); scroll.AddChild(_autoSellList);
        }
        RenderAutoSellRules();
        var size = GetViewport().GetVisibleRect().Size;
        _autoSellDialog.PopupCentered(new((int)Math.Min(600, size.X - 32), (int)Math.Min(340, size.Y - 48)));
    }

    private void RenderAutoSellRules()
    {
        if (_autoSellList == null) return;
        Clear(_autoSellList);
        _autoSellList.AddChild(Ui.Label("VENTA AUTOMÁTICA", 20, Ui.Gold));
        var help = Ui.Label("Solo objetos nuevos de la misma ranura y familia, con calidad inferior al límite indicado. Los de calidad igual o superior, actuales, favoritos y equipados se conservan.\n" +
            "Las reglas antiguas de calidad exacta se desactivan al actualizar. Activa el nuevo límite desde una pieza.\n" +
            (TemporaryInventory ? "Estos ajustes se guardan con esta partida.\n" + DefenseRules.InventoryFullNotice : "Estos ajustes se guardan para tu cuenta en este dispositivo."), 15, Ui.Muted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; _autoSellList.AddChild(help);
        var styles = TemporaryInventory ? _defensePlayer!.AutoSellStyles : Preferences.AutoSellRules.Select(r => r.Style).ToArray();
        foreach (var style in styles.OrderBy(s => s.Slot).ThenBy(s => s.Tier).ThenBy(s => s.Family))
        {
            var row = new HBoxContainer(); _autoSellList.AddChild(row);
            var label = Ui.Label(style.DescribeAutoSell(), 15, TierColor(style.Tier));
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(label);
            var keep = Ui.Button("Conservar", () => SetAutoSell(style, false)); keep.CustomMinimumSize = new(100, 44);
            keep.MouseFilter = MouseFilterEnum.Pass;
            keep.Disabled = InventoryLocked; row.AddChild(keep); _preferenceActions.Add(keep);
        }
        if (styles.Length == 0) _autoSellList.AddChild(Ui.Label("No hay estilos con venta automática.", 15, Ui.Muted));
    }

    internal void AutomaticSalesFinished()
    {
        if (_lifetime.IsCancellationRequested || TemporaryInventory) return;
        RebaseDraft(); Render(); Busy(false); RefreshPickerItems();
        if (_main.Accounts.InventoryNeedsRefresh)
            SetPickerMessage("Venta automática sin confirmar. Actualiza el inventario antes de volver a vender.");
        else if (_main.Accounts.SignedIn) _ = Refresh();
    }
}
