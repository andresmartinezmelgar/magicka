using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentPanel
{
    private string _saleOwner = "";
    private Label _saleHeading = null!, _saleItem = null!, _saleState = null!, _saleGold = null!, _saleWarning = null!;
    private GridContainer _saleStats = null!;
    private PanelContainer _saleCard = null!;
    private ColorRect _saleDimmer = null!;

    private void BuildSaleModal()
    {
        _saleDimmer = new ColorRect { Name = "SaleBackdrop", Color = new(0.005f, 0.01f, 0.025f, 0.75f), Visible = false };
        AddChild(_saleDimmer); Ui.Full(_saleDimmer);
        var theme = Ui.Theme();
        var surface = Ui.Panel(ArcanaMenuChrome.Surface, 14, ArcanaMenuChrome.Border);
        surface.ContentMarginLeft = surface.ContentMarginRight = 16;
        surface.ContentMarginTop = surface.ContentMarginBottom = 12;
        theme.SetStylebox("panel", "AcceptDialog", surface);
        theme.SetConstant("buttons_separation", "AcceptDialog", 12);
        _confirm = new ConfirmationDialog { Name = "SaleConfirmation", Title = "Vender objeto", Theme = theme,
            Borderless = true, Unresizable = true, Exclusive = true, Transient = true, CancelButtonText = "Conservar objeto" };
        AddChild(_confirm); _confirm.GetLabel().Hide();
        _confirm.Confirmed += () => _ = Sell();
        _confirm.Canceled += CancelSale;
        _confirm.VisibilityChanged += () => _saleDimmer.Visible = _confirm.Visible;

        var scroll = new ContentScrollContainer { Name = "SaleContentScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _confirm.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6); scroll.AddChild(body);
        _saleHeading = Ui.Label("VENDER OBJETO", 19, Ui.Gold); body.AddChild(_saleHeading);
        _saleCard = new PanelContainer(); body.AddChild(_saleCard);
        var itemBox = new VBoxContainer(); itemBox.AddThemeConstantOverride("separation", 3); _saleCard.AddChild(itemBox);
        _saleItem = Ui.Label("", 20); _saleItem.AutowrapMode = TextServer.AutowrapMode.WordSmart; itemBox.AddChild(_saleItem);
        _saleState = Ui.Label("", 16, Ui.Muted); itemBox.AddChild(_saleState);
        _saleStats = Ui.Table(2, "SaleStats"); body.AddChild(_saleStats);

        var reward = new PanelContainer();
        var rewardStyle = Ui.Panel(new Color("28291e"), 7, new Color("75633d"));
        rewardStyle.ContentMarginTop = rewardStyle.ContentMarginBottom = 4;
        rewardStyle.ContentMarginLeft = rewardStyle.ContentMarginRight = 12;
        reward.AddThemeStyleboxOverride("panel", rewardStyle); body.AddChild(reward);
        var goldRow = new HBoxContainer(); reward.AddChild(goldRow);
        var caption = Ui.Label(_defensePlayer == null ? "RECIBES · ORO DE CUENTA" : "RECIBES · ORO DE PARTIDA", 14, Ui.Muted);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill; caption.VerticalAlignment = VerticalAlignment.Center; goldRow.AddChild(caption);
        _saleGold = Ui.Label("", 20, Ui.Gold); _saleGold.Name = "SaleGold"; goldRow.AddChild(_saleGold);
        _saleWarning = Ui.Label("", 15, Ui.Muted); _saleWarning.Name = "SaleWarning";
        _saleWarning.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_saleWarning);

        foreach (var button in new[] { _confirm.GetOkButton(), _confirm.GetCancelButton() })
        {
            button.CustomMinimumSize = new(0, 46); button.MouseDefaultCursorShape = CursorShape.PointingHand;
            button.AddThemeFontSizeOverride("font_size", 17);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            {
                var style = (StyleBoxFlat)button.GetThemeStylebox(state).Duplicate();
                style.ContentMarginTop = style.ContentMarginBottom = 8;
                style.ContentMarginLeft = style.ContentMarginRight = 18;
                button.AddThemeStyleboxOverride(state, style);
            }
            var focus = Ui.Panel(new Color(0, 0, 0, 0), 8, Ui.Gold);
            focus.BorderWidthTop = focus.BorderWidthBottom = focus.BorderWidthLeft = focus.BorderWidthRight = 2;
            button.AddThemeStyleboxOverride("focus", focus);
        }
        var sell = _confirm.GetOkButton();
        foreach (var (state, color) in new[] { ("normal", "c7a568"), ("hover", "e5c587"), ("pressed", "ad8a50") })
        {
            var style = (StyleBoxFlat)sell.GetThemeStylebox(state).Duplicate(); style.BgColor = new(color); style.BorderColor = Ui.Gold;
            sell.AddThemeStyleboxOverride(state, style);
        }
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" }) sell.AddThemeColorOverride(state, new Color("111a26"));
    }

    private void ConfirmSale(string id)
    {
        var item = _draft?.Items.FirstOrDefault(i => i.Id == id && i.Slot == _selectedSlot);
        if (_busy || item == null || !CanSellInventory) return;
        _selling = item.Id; _saleOwner = _draft!.Uid;
        bool equipped = _draft.Loadout.Get(item.Slot) == item.Id;
        Color rarity = TierColor(item.Tier);
        var card = Ui.Panel(ArcanaMenuChrome.SurfaceRaised, 8, rarity);
        card.BorderWidthLeft = 4; card.ContentMarginLeft = card.ContentMarginRight = 12;
        card.ContentMarginTop = card.ContentMarginBottom = 5; _saleCard.AddThemeStyleboxOverride("panel", card);
        _saleItem.Text = EquipmentCatalogue.ItemName(item); _saleItem.AddThemeColorOverride("font_color", rarity);
        _saleState.Text = equipped ? "EQUIPADA · Se quitará de tu equipo" : "SIN EQUIPAR";
        _saleState.AddThemeColorOverride("font_color", equipped ? Ui.Gold : Ui.Muted);
        Clear(_saleStats);
        void StatCell(string text, bool number, Color? color = null, bool header = false)
        {
            var label = Ui.TableCell(_saleStats, text, number, color, header);
            var style = (StyleBoxFlat)label.GetThemeStylebox("normal").Duplicate();
            style.ContentMarginTop = style.ContentMarginBottom = 0; style.BorderColor = new Color("304761");
            label.AddThemeStyleboxOverride("normal", style);
            if (number) { label.SizeFlagsHorizontal = SizeFlags.Fill; label.CustomMinimumSize = new(108, 0); }
        }
        StatCell("Valor", true, header: true); StatCell("Atributo", false, header: true);
        foreach (var bonus in item.Bonuses)
        {
            StatCell(EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue);
            StatCell(StatName(bonus.Stat), false);
        }
        _saleGold.Text = $"+{item.SaleGold} oro";
        _saleWarning.Text = equipped ? "Venta definitiva. La pieza se desequipará y se eliminará."
            : "Venta definitiva. La pieza se eliminará del inventario.";
        _confirm.OkButtonText = $"Vender · +{item.SaleGold} oro";
        MoveChild(_saleDimmer, GetChildCount() - 1);
        UpdateSaleDensity(); _confirm.PopupCentered(SaleModalSize());
        _confirm.GetCancelButton().GrabFocus();
    }

    private Vector2I SaleModalSize()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        int rows = Math.Max(0, _saleStats.GetChildCount() / 2 - 1);
        int height = (viewport.Y < 500 ? 330 : 390) - Math.Max(0, 4 - rows) * 23;
        return new((int)Math.Min(620, viewport.X - 32), (int)Math.Min(height, viewport.Y - 32));
    }
    private void ResizeSaleModal()
    {
        UpdateSaleDensity(); _confirm.Size = SaleModalSize(); _confirm.MoveToCenter();
    }
    private void UpdateSaleDensity()
    {
        bool compact = GetViewport().GetVisibleRect().Size.Y < 500;
        _saleHeading.Visible = _saleState.Visible = !compact;
    }
    private void CancelSale()
    {
        _confirm.Hide(); _selling = ""; _saleOwner = "";
    }
}
