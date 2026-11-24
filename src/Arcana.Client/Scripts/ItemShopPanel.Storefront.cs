using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ItemShopPanel
{
    private VBoxContainer _equipmentArea = null!, _goldArea = null!, _passesArea = null!;
    private GridContainer _goldGrid = null!, _passesGrid = null!;
    private HBoxContainer _paymentFooter = null!;
    private ElementTextLabel _passStatus = null!;
    private ElementTextLabel _heroTitle = null!;
    private StoreIllustration _heroArt = null!;
    private HBoxContainer _heroRow = null!;
    private readonly List<Button> _areaTabs = [];
    private readonly Dictionary<string, Button> _commercialButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ElementTextLabel> _commercialPrices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StorePrice> _playPrices = new(StringComparer.Ordinal);
    private StoreProduct[] _availableProducts = [];
    private int _areaIndex;
    private bool _pricesLoading, _storeApi;
    private string _priceKey = "";

    private void BuildStoreHeader(VBoxContainer layout)
    {
        var hero = new PanelContainer { Name = "ShopHero" };
        var style = Ui.Panel(new("112b46"), 12, new("426588"));
        style.ContentMarginTop = style.ContentMarginBottom = Ui.IsMobile ? 8 : 14;
        hero.AddThemeStyleboxOverride("panel", style); layout.AddChild(hero);
        var row = _heroRow = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); hero.AddChild(row);
        _heroArt = new StoreIllustration { ThemeKind = "gold", Level = 2, CustomMinimumSize = new(Ui.IsMobile ? 62 : 84, Ui.IsMobile ? 62 : 84) };
        row.AddChild(_heroArt);
        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; copy.AddThemeConstantOverride("separation", 3); row.AddChild(copy);
        _heroTitle = Wrapped(copy, "EL BAZAR ARCANO", Ui.IsMobile ? 24 : 31, Ui.Gold);
        if (!Ui.IsMobile) Wrapped(copy, "Equipa tu magia. Elige tu próxima ventaja.", 17, Ui.Muted);
        _wallet = Wrapped(copy, "Oro de cuenta: —", Ui.IsMobile ? 16 : 18, Ui.Text); _wallet.Name = "ShopWallet";
        _reload = Ui.Button("Consultar", () => { _priceKey = ""; _ = Load(refresh: true); }); _reload.Name = "ShopConnect";
        _reload.CustomMinimumSize = new(0, 44); _reload.AddThemeFontSizeOverride("font_size", 14); row.AddChild(_reload);
        _reload.SizeFlagsVertical = SizeFlags.ShrinkCenter;
    }

    private void BuildStoreTabs(VBoxContainer layout)
    {
        var tabs = new HBoxContainer { Name = "ShopAreaTabs" }; tabs.AddThemeConstantOverride("separation", 8); layout.AddChild(tabs);
        string[] names = ["Equipamiento", "Oro", "Sin anuncios"];
        for (int i = 0; i < names.Length; i++)
        {
            int area = i;
            var button = Ui.Button(names[i], () => ShowStoreArea(area)); button.Name = "ShopArea" + i;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, Ui.IsMobile ? 46 : 48);
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 16 : 18);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _areaTabs.Add(button); tabs.AddChild(button);
        }
    }

    private void BuildCommercialAreas(VBoxContainer body, VBoxContainer layout)
    {
        _goldArea = new VBoxContainer { Name = "ShopGoldArea", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _passesArea = new VBoxContainer { Name = "ShopPassesArea", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var area in new[] { _goldArea, _passesArea }) { area.AddThemeConstantOverride("separation", Ui.IsMobile ? 8 : 12); body.AddChild(area); }
        Wrapped(_goldArea, Ui.IsMobile ? "ELIGE TU RESERVA DE ORO" : "MÁS ORO. MÁS POSIBILIDADES.", Ui.IsMobile ? 18 : 24, Ui.Gold);
        Wrapped(_goldArea, "Elige tu reserva y guárdala en tu cuenta. Úsala en equipo y mejoras cuando quieras.", 16, Ui.Muted).Visible = !Ui.IsMobile;
        _goldGrid = new GridContainer { Name = "ShopGoldPacks", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _goldGrid.AddThemeConstantOverride("h_separation", 14); _goldGrid.AddThemeConstantOverride("v_separation", 14); _goldArea.AddChild(_goldGrid);
        Wrapped(_passesArea, Ui.IsMobile ? "Pago único · Sin renovación automática" : "TU MAGIA, SIN INTERRUPCIONES", Ui.IsMobile ? 16 : 24, new("bda4ff"));
        Wrapped(_passesArea, "Un mes, seis meses o para siempre. Pago único en cada compra. Sin renovación automática.", 16, Ui.Muted).Visible = !Ui.IsMobile;
        _passStatus = Wrapped(_passesArea, "", 16, Ui.Gold); _passStatus.Name = "ShopPassStatus";
        _passesGrid = new GridContainer { Name = "ShopNoAdsPlans", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _passesGrid.AddThemeConstantOverride("h_separation", 14); _passesGrid.AddThemeConstantOverride("v_separation", 14); _passesArea.AddChild(_passesGrid);
        var products = StoreCatalogue.Products();
        for (int i = 0; i < products.Length; i++) BuildCommercialCard(products[i], i);
        var manage = Ui.Button("Gestionar el pase en Google Play", () => OS.ShellOpen(
            "https://play.google.com/store/account/subscriptions?sku=" + StoreCatalogue.PassProduct + "&package=com.arcana.arena"));
        manage.Name = "ShopManagePass"; manage.CustomMinimumSize = new(0, 44); _passesArea.AddChild(manage);
        _paymentFooter = new HBoxContainer(); _paymentFooter.AddThemeConstantOverride("separation", 10); body.AddChild(_paymentFooter);
        var secure = Wrapped(_paymentFooter, "Pago seguro con Google Play. El importe final se confirma antes de pagar.", 13, Ui.Muted);
        secure.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _restore = Ui.Button(Ui.IsMobile ? "Restaurar" : "Restaurar compras", () => _ = RestoreCommercialPurchases()); _restore.Name = "ShopRestorePurchases";
        _restore.CustomMinimumSize = new(0, 44); _restore.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _restore.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 13 : 15); _heroRow.AddChild(_restore);
        ShowStoreArea(0);
    }

    private void BuildCommercialCard(StoreProduct product, int index)
    {
        bool gold = product.Gold > 0, forever = !gold && product.Months == 0;
        Color accent = gold ? Ui.Gold : forever ? new("f0ce8b") : new("b99aff");
        var card = new PanelContainer { Name = "ShopProduct_" + product.Key, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = Ui.Panel(gold ? new("15283c") : new("211f3b"), 14, accent.Darkened(.28f));
        style.BorderWidthBottom = 3; style.ContentMarginLeft = style.ContentMarginRight = 16;
        if (Ui.IsMobile)
        {
            style.ContentMarginTop = style.ContentMarginBottom = 10;
            style.ContentMarginLeft = style.ContentMarginRight = 12;
        }
        card.AddThemeStyleboxOverride("panel", style); (gold ? _goldGrid : _passesGrid).AddChild(card);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", Ui.IsMobile ? 6 : 8); card.AddChild(box);
        var tag = Wrapped(box, gold ? new[] { "ESENCIAL", "RESERVA", "GRAN COFRE", "TESORO" }[index] : forever ? "SIN FECHA DE CADUCIDAD" : "PASE DE PREPAGO", 12, accent);
        tag.HorizontalAlignment = HorizontalAlignment.Center;
        tag.Visible = !Ui.IsMobile;
        Container identity = Ui.IsMobile ? new HBoxContainer() : box;
        if (Ui.IsMobile) { identity.AddThemeConstantOverride("separation", 8); box.AddChild(identity); }
        identity.AddChild(new StoreIllustration { ThemeKind = gold ? "gold" : "noAds", Level = gold ? index : product.Months,
            Forever = forever, CustomMinimumSize = Ui.IsMobile ? new(56, 56) : new(100, 100),
            SizeFlagsHorizontal = Ui.IsMobile ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill });
        var headings = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; identity.AddChild(headings);
        var amount = Wrapped(headings, gold ? product.Gold.ToString("N0") + " ORO" : forever ? "PARA SIEMPRE" : product.Months == 1 ? "1 MES" : "6 MESES", Ui.IsMobile ? 21 : 27, accent);
        amount.HorizontalAlignment = HorizontalAlignment.Center;
        var title = Wrapped(headings, product.Title, Ui.IsMobile ? 14 : 17); title.HorizontalAlignment = HorizontalAlignment.Center;
        title.Visible = !Ui.IsMobile || gold;
        var description = Wrapped(box, gold ? "Oro permanente en tu cuenta" : forever ? "Una compra. Sin anuncios." : "Sin anuncios durante todo el pase", 14, Ui.Muted);
        description.HorizontalAlignment = HorizontalAlignment.Center;
        description.Visible = !Ui.IsMobile;
        var price = Wrapped(box, "Precio de Google Play", Ui.IsMobile ? 13 : 15, Ui.Muted); price.HorizontalAlignment = HorizontalAlignment.Center;
        _commercialPrices.Add(product.Key, price);
        var spacer = new Control { CustomMinimumSize = new(0, Ui.IsMobile ? 0 : 4), SizeFlagsVertical = SizeFlags.ExpandFill }; box.AddChild(spacer);
        var buy = Ui.Button("Próximamente", () => _ = BuyCommercialProduct(product.Key), true);
        buy.Name = "ShopProductBuy_" + product.Key; buy.CustomMinimumSize = new(0, Ui.IsMobile ? 48 : 50);
        buy.AutowrapMode = TextServer.AutowrapMode.WordSmart; buy.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 16 : 17);
        var buttonStyle = Ui.Panel(gold ? new("66502d") : new("4e3e79"), 9, accent.Darkened(.1f));
        buttonStyle.ContentMarginLeft = buttonStyle.ContentMarginRight = 8;
        buy.AddThemeStyleboxOverride("normal", buttonStyle);
        buy.AddThemeStyleboxOverride("hover", Ui.Panel(gold ? new("82643a") : new("655092"), 9, accent));
        buy.Disabled = true; box.AddChild(buy); _commercialButtons.Add(product.Key, buy);
    }

    private void ShowStoreArea(int area)
    {
        _areaIndex = Math.Clamp(area, 0, 2);
        _equipmentArea.Visible = area == 0; _goldArea.Visible = area == 1; _passesArea.Visible = area == 2;
        _paymentFooter.Visible = area != 0;
        _restore.Visible = area != 0;
        UpdateStoreHeaderVisibility();
        RefreshStoreStatusVisibility();
        for (int i = 0; i < _areaTabs.Count; i++)
        {
            var style = Ui.Panel(i == area ? new("2c405b") : new("111e30"), 8, i == area ? Ui.Gold : ArcanaMenuChrome.Border);
            style.ContentMarginLeft = style.ContentMarginRight = 6; style.ContentMarginTop = style.ContentMarginBottom = 8;
            _areaTabs[i].AddThemeStyleboxOverride("normal", style);
        }
        if (_scroll != null) _scroll.ScrollVertical = 0;
        if (area != 0) _ = LoadCommercialPrices();
    }

    private void ResizeCommercialAreas(float width)
    {
        if (_goldGrid == null) return;
        _goldGrid.Columns = width >= 1180 ? 4 : width >= 480 ? 2 : 1;
        _passesGrid.Columns = width >= 540 ? 3 : width >= 480 ? 2 : 1;
    }

    private void UpdateStoreHeaderVisibility()
    {
        // El saldo y Consultar siguen visibles; la decoración cede altura al catálogo.
        bool decorated = !Ui.IsMobile || _areaIndex == 0 && GetViewportRect().Size.Y >= 600;
        _heroArt.Visible = _heroTitle.Visible = decorated;
    }

    private void ReceiveCommercialProducts(ShopResult result)
    {
        _storeApi = result.BillingProducts.Length > 0;
        _availableProducts = result.BillingProducts.Length > 0 ? result.BillingProducts : result.NoAdsProductId is { Length: > 0 } legacy
            ? StoreCatalogue.Products(legacy).Where(p => p.Key == "no_ads_forever").ToArray() : [];
        RefreshCommercialButtons();
        if (_areaIndex != 0) _ = LoadCommercialPrices();
    }

    private void RefreshCommercialButtons()
    {
        if (_restore == null) return;
        var access = _main.Accounts.Profile?.Access ?? new();
        bool exempt = access.WithoutAds(ShopNow);
        _passStatus.Text = access.IsAdmin ? "Sin anuncios incluido por tu cuenta de administrador." :
            access.AdsRemoved || access.PlayAdsValidUntil > ShopNow.ToUnixTimeSeconds() ? "Tu cuenta ya tiene acceso sin anuncios para siempre." :
            access.PlayPassValidUntil > ShopNow.ToUnixTimeSeconds() ? "Pase activo hasta el " + DateTimeOffset.FromUnixTimeSeconds(access.PlayPassExpiresAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm") + "." :
            !_adsEnabled ? "Publicidad pausada para todos." : "";
        _passStatus.Visible = _passStatus.Text.Length > 0;
        _restore.Disabled = _busy || _pricesLoading || !_connected || _billing?.Available != true || _availableProducts.Length == 0;
        foreach (var pair in _commercialButtons)
        {
            var product = _availableProducts.FirstOrDefault(p => p.Key == pair.Key);
            bool hasPrice = _playPrices.TryGetValue(pair.Key, out var price);
            bool noAds = !pair.Key.StartsWith("gold_", StringComparison.Ordinal);
            pair.Value.Disabled = _busy || _pricesLoading || !_connected || _billing?.Available != true || product == null || !hasPrice || noAds && (exempt || !_adsEnabled);
            pair.Value.Text = noAds && exempt ? "Ya incluido" : noAds && !_adsEnabled ? "Anuncios pausados" : _pricesLoading ? "Consultando precio…" :
                _billing?.Available != true ? Ui.IsMobile ? "Solo Android" : "Disponible en Android" : !hasPrice || product == null ? "Próximamente" : "Comprar · " + price!.Price;
            _commercialPrices[pair.Key].Text = hasPrice ? price!.Price + (noAds ? " · pago único" : "") : "Precio de Google Play";
        }
        RefreshStoreStatusVisibility();
    }

    private void RefreshStoreStatusVisibility()
    {
        if (_status != null) _status.Visible = (!Ui.IsMobile && _areaIndex == 0) || _busy || !_connected ||
            !_status.Text.StartsWith("Ofertas del día.", StringComparison.Ordinal);
    }

    private async Task LoadCommercialPrices()
    {
        if (_pricesLoading || !_connected || _billing?.Available != true || _availableProducts.Length == 0 || _lifetime.IsCancellationRequested) return;
        var account = _main.Accounts; long session = account.SessionRevision;
        string key = account.Profile?.Uid + ":" + session + ":" + string.Join(";", _availableProducts.Select(p => p.ProductId + "/" + p.BasePlanId));
        if (_priceKey == key) return;
        _priceKey = key; _pricesLoading = true; _playPrices.Clear(); RefreshCommercialButtons();
        StorePrice[] prices = [];
        try { prices = await _billing.Prices(_availableProducts, _lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or InvalidOperationException or System.IO.InvalidDataException) { }
        _main.Post(() =>
        {
            if (_lifetime.IsCancellationRequested) return;
            _pricesLoading = false;
            if (!ReferenceEquals(account, _main.Accounts) || session != account.SessionRevision) { _priceKey = ""; return; }
            foreach (var price in prices)
            {
                var product = _availableProducts.FirstOrDefault(p => p.Key == price.Key);
                if (product != null && (product.Months == 0 || price.Period == "P" + product.Months + "M")) _playPrices[price.Key] = price;
            }
            RefreshCommercialButtons();
        });
    }

    private async Task BuyCommercialProduct(string key)
    {
        if (_busy || _pricesLoading || !_connected || !_commercialButtons.TryGetValue(key, out var button) || button.Disabled || _billing == null) return;
        var product = _availableProducts.FirstOrDefault(p => p.Key == key); if (product == null) return;
        var account = _main.Accounts; long session = account.SessionRevision; int request = ++_requestVersion;
        SetBusy(true, "Abriendo el pago seguro de Google Play…");
        try
        {
            var receipt = await _billing.Request(product, account.Profile!.Uid, false, _lifetime.Token);
            if (!ReferenceEquals(account, _main.Accounts) || session != account.SessionRevision) return;
            SetBusy(true, "Confirmando la compra y guardándola en tu cuenta…");
            var result = _storeApi ? await account.VerifyStorePurchase(product.ProductId, receipt, _lifetime.Token)
                : await account.VerifyNoAds(receipt, _lifetime.Token);
            _main.RememberShop(account, session, result);
            Publish(request, () => Render(result, product.Gold > 0 ? "Compra confirmada. El oro ya está en tu cuenta." : "Compra confirmada. Disfruta del juego sin anuncios."));
        }
        catch (Exception error) { Publish(request, () => SetBusy(false, PvpServer.DisplayText(error.Message) + " Puedes consultar o restaurar la compra.")); }
    }

    private async Task RestoreCommercialPurchases()
    {
        if (_busy || _restore.Disabled || _billing == null || !_main.Accounts.SignedIn) return;
        var account = _main.Accounts; long session = account.SessionRevision; int request = ++_requestVersion;
        SetBusy(true, "Restaurando las compras de esta cuenta…");
        int found = 0;
        try
        {
            foreach (var product in _availableProducts.DistinctBy(p => p.ProductId))
            {
                string receipt;
                try { receipt = await _billing.Request(product, account.Profile!.Uid, true, _lifetime.Token); }
                catch (InvalidOperationException error) when (error.Message.StartsWith("Google Play no encuentra", StringComparison.Ordinal)) { continue; }
                if (!ReferenceEquals(account, _main.Accounts) || session != account.SessionRevision) return;
                if (_storeApi) await account.VerifyStorePurchase(product.ProductId, receipt, _lifetime.Token);
                else await account.VerifyNoAds(receipt, _lifetime.Token);
                found++;
            }
            var result = await account.Shop(_lifetime.Token);
            _main.RememberShop(account, session, result);
            Publish(request, () => Render(result, found > 0 ? "Compras restauradas. El oro ya entregado no se duplica." : "Cuenta sincronizada. Google Play no tiene compras pendientes de restaurar."));
        }
        catch (Exception error) { Publish(request, () => SetBusy(false, PvpServer.DisplayText(error.Message))); }
    }
}
