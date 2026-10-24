using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private AcceptDialog? _autoSellDialog;
    private VBoxContainer? _autoSellList;
    private bool InventoryLocked => _busy || !TemporaryInventory && _main.InventoryAutoSelling;

    private bool AutoSells(EquipmentStyle style) => TemporaryInventory
        ? _defensePlayer!.AutoSellStyles.Contains(style) : Preferences.AutoSells(style);

    private void BuildAutoSellButton(VBoxContainer box, EquipmentItem item)
    {
        var style = EquipmentStyle.Of(item);
        var button = Ui.Button(AutoSells(style) ? "Conservar nuevos de este estilo" : "No más de este estilo de ítem",
            () => SetAutoSell(style, !AutoSells(style)));
        button.Name = "EquipmentAutoSell_" + item.Id; button.CustomMinimumSize = new(0, 44);
        button.MouseFilter = MouseFilterEnum.Pass;
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.AddThemeFontSizeOverride("font_size", 14);
        button.TooltipText = style.Describe() + "\nVende automáticamente solo las piezas nuevas de esta ranura, calidad y familia.\n" +
            "Conserva las que ya tienes, los favoritos y el equipo. Puedes desactivarlo desde Opciones → Venta automática.";
        box.AddChild(button); _preferenceActions.Add(button);
    }

    private void SetAutoSell(EquipmentStyle style, bool enabled)
    {
        if (InventoryLocked || _draft == null) return;
        ResetQuickActions();
        string message = enabled ? "Venta automática activada para " + style.Describe() + ". Tus piezas actuales se conservan."
            : "Volverás a conservar los nuevos objetos de " + style.Describe() + ".";
        if (TemporaryInventory)
        {
            DefenseOperation(new() { Type = "defense_auto_sell", AutoSellStyle = style, Slot = enabled ? 1 : 0 },
                p => p.AutoSellStyles.Contains(style) == enabled, () => { RefreshPickerItems(); RenderAutoSellRules(); }, message);
            return;
        }
        Preferences.SetAutoSell(style, enabled, _draft);
        if (!DevicePreferences.SaveInventory()) message += " No se pudo guardar el ajuste en el dispositivo.";
        RefreshPickerItems(); SetPickerMessage(message); RenderAutoSellRules();
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
        var help = Ui.Label("Solo objetos nuevos de la misma ranura, calidad y familia. Los actuales, favoritos y equipados se conservan.\n" +
            (TemporaryInventory ? "Estos ajustes se guardan con esta partida." : "Estos ajustes se guardan para tu cuenta en este dispositivo."), 15, Ui.Muted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; _autoSellList.AddChild(help);
        var styles = TemporaryInventory ? _defensePlayer!.AutoSellStyles : Preferences.AutoSellRules.Select(r => r.Style).ToArray();
        foreach (var style in styles.OrderBy(s => s.Slot).ThenBy(s => s.Tier).ThenBy(s => s.Family))
        {
            var row = new HBoxContainer(); _autoSellList.AddChild(row);
            var label = Ui.Label(style.Describe(), 15, TierColor(style.Tier));
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
