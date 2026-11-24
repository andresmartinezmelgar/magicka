using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ItemShopPanel
{
    private static ElementTextLabel Wrapped(Control parent, string value, int size, Color? color = null)
    {
        var label = Ui.Label(value, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }

    private void ResizeOffers()
    {
        if (_scroll == null || _offers == null) return;
        float width = _scroll.Size.X - _scroll.GetVScrollBar().Size.X;
        _offers.Columns = width >= 1100 ? 3 : width >= 600 ? 2 : 1;
        foreach (var card in _offers.GetChildren())
            if (card.FindChild("ShopDetailsScroll_*", true, false) is Control details)
                details.CustomMinimumSize = new(0, OfferDetailsHeight);
        UpdateStoreHeaderVisibility();
        ResizeCommercialAreas(width);
    }

    private float OfferDetailsHeight => Ui.IsMobile
        ? Math.Clamp(GetViewportRect().Size.Y * .3f, 180, 240)
        : Math.Clamp(GetViewportRect().Size.Y * .3f, 224, 280);

    private void ClearOffers()
    {
        _shop = null; _connected = false; _buyButtons.Clear();
        _availableProducts = []; _storeApi = false;
        _comparison?.Hide();
        foreach (var child in _offers.GetChildren()) { _offers.RemoveChild(child); child.QueueFree(); }
    }

    private void ShowUnavailable(string status)
    {
        ClearOffers(); _wallet.Text = "Oro de cuenta: —";
        for (int i = 1; i <= 6; i++)
        {
            var card = new PanelContainer { Name = "ShopUnavailable_" + i, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", Ui.Panel(ArcanaMenuChrome.SurfaceRaised, 8, ArcanaMenuChrome.Border)); _offers.AddChild(card);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 7); card.AddChild(box);
            Wrapped(box, "Oferta " + i, 19, Ui.Muted);
            Wrapped(box, "Sin objetos disponibles", 16, Ui.Muted);
            var unavailable = Ui.Button("No disponible", () => { }); unavailable.Disabled = true;
            unavailable.CustomMinimumSize = new(0, 50); box.AddChild(unavailable);
        }
        SetBusy(false, status); ResizeOffers();
    }

    private void Render(ShopResult result, string message = "Ofertas del día. Renovación a las 00:00, hora peninsular.")
    {
        if (!_main.Accounts.SignedIn || result.Profile.Uid != _main.Accounts.Profile?.Uid) return;
        result = result with { Profile = _main.Accounts.Profile! };
        ClearOffers();
        _owner = result.Profile.Uid; _shop = result.Shop; _connected = true; _buyButtons.Clear();
        _ads = result.RewardedAds; _adsEnabled = result.AdsEnabled;
        ReceiveCommercialProducts(result);
        _adPolicyRetryAt = Time.GetTicksMsec() + 60000;
        _serverTime = result.ServerTime ?? DateTimeOffset.UtcNow; _receivedAt = Time.GetTicksMsec();
        _wallet.Text = $"Oro de cuenta: {result.Profile.Gold:N0} · Inventario: {result.Profile.Items.Length}/{EquipmentCatalogue.InventoryLimit}";
        foreach (var offer in result.Shop.Offers)
        {
            var card = new PanelContainer { Name = "ShopOffer_" + offer.Item.Id, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var style = Ui.Panel(ArcanaMenuChrome.SurfaceRaised, 8, EquipmentPanel.TierColor(offer.Item.Tier));
            style.ContentMarginLeft = style.ContentMarginRight = 10; style.ContentMarginTop = style.ContentMarginBottom = 10;
            card.AddThemeStyleboxOverride("panel", style); _offers.AddChild(card);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 7); card.AddChild(box);
            var itemHeader = new HBoxContainer(); itemHeader.AddThemeConstantOverride("separation", 12); box.AddChild(itemHeader);
            itemHeader.AddChild(EquipmentIcons.View(EquipmentIcons.Slot(offer.Item.Slot), 58, EquipmentPanel.TierColor(offer.Item.Tier)));
            var itemName = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; itemHeader.AddChild(itemName);
            Wrapped(itemName, EquipmentCatalogue.TierName(offer.Item.Tier).ToUpperInvariant(), 14, EquipmentPanel.TierColor(offer.Item.Tier));
            Wrapped(itemName, EquipmentCatalogue.SlotName(offer.Item.Slot), 21, Ui.Text);
            Wrapped(itemName, EquipmentCatalogue.FamilyName(offer.Item.Family), 15, EquipmentIcons.FamilyColor(offer.Item.Family));
            // Los atributos largos se desplazan sin alargar la ficha ni sus botones.
            var detailsScroll = new ContentScrollContainer { Name = "ShopDetailsScroll_" + offer.Item.Id,
                CustomMinimumSize = new(0, OfferDetailsHeight),
                SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.Fill };
            box.AddChild(detailsScroll); Ui.VisibleVerticalScroll(detailsScroll);
            var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            details.AddThemeConstantOverride("separation", 7); detailsScroll.AddChild(details);
            var stats = Ui.Table(2, "ShopStats_" + offer.Item.Id); details.AddChild(stats);
            EquipmentPanel.RenderItemStats(stats, offer.Item);
            EquipmentPanel.AddItemEffects(details, EquipmentPanel.ItemEffects(offer.Item, offer.Item.UpgradeLevel,
                EquipmentQuery.Preview(result.Profile, offer.Item).After.Modifiers));
            var purchaseRow = new HBoxContainer(); purchaseRow.AddThemeConstantOverride("separation", 6); box.AddChild(purchaseRow);
            var compare = Ui.Button("Comparar", () =>
            {
                _comparisonText.Text = EquipmentCatalogue.ItemName(offer.Item) + "\n\nEQUIPO SI LA EQUIPAS\n" + EquipmentQuery.Preview(result.Profile, offer.Item).Describe();
                var size = GetViewport().GetVisibleRect().Size;
                _comparison.PopupCentered(new((int)Math.Min(620, size.X - 32), (int)Math.Min(410, size.Y - 32)));
            });
            compare.Name = "ShopComparison_" + offer.Item.Id; compare.CustomMinimumSize = new(0, 40);
            compare.TooltipText = "Comparar el equipo completo, conjuntos y límites antes de comprar.";
            compare.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            compare.AddThemeFontSizeOverride("font_size", 15); purchaseRow.AddChild(compare);
            bool full = result.Profile.Items.Length >= EquipmentCatalogue.InventoryLimit;
            bool affordable = result.Profile.Gold >= offer.Price;
            string caption = offer.Sold ? "COMPRADO" : full ? "Inventario lleno" : !affordable ? $"Faltan {offer.Price - result.Profile.Gold:N0} oro" : $"Comprar · {offer.Price:N0} oro";
            var buy = Ui.Button(caption, () => _ = Buy(offer.Item.Id)); buy.Name = "ShopBuy_" + offer.Item.Id;
            buy.CustomMinimumSize = new(0, 50); buy.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            buy.AddThemeFontSizeOverride("font_size", 16); buy.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            buy.Disabled = offer.Sold || !affordable || full;
            buy.TooltipText = offer.Sold ? "Esta oferta ya está comprada." : full ? "El inventario está lleno." :
                !affordable ? $"Faltan {offer.Price - result.Profile.Gold} de oro." : "Comprar y guardar esta pieza en tu cuenta.";
            buy.SetMeta("available", !buy.Disabled); purchaseRow.AddChild(buy); _buyButtons.Add(offer.Item.Id, buy);
            Wrapped(box, buy.TooltipText, 15, buy.Disabled && !offer.Sold ? Ui.Gold : Ui.Muted).Name = "ShopReason_" + offer.Item.Id;
        }
        _status.Text = message; ResizeOffers(); SetBusy(false); _main.SaveAccountCache();
    }
}
