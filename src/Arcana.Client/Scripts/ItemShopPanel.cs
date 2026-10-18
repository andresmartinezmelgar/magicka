using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Ofertas persistentes verificadas por el servidor; el anuncio es una simulación explícita.</summary>
public partial class ItemShopPanel : Control
{
    private Main _main = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _adCancellation;
    private ItemShop? _shop;
    private Label _wallet = null!, _status = null!, _adMessage = null!;
    private Button _reload = null!, _refresh = null!, _cancelAd = null!;
    private GridContainer _offers = null!;
    private Control _adOverlay = null!;
    private readonly Dictionary<string, Button> _buyButtons = new(StringComparer.Ordinal);
    private bool _busy, _connected;
    private string? _owner;
    internal ItemShop? Catalogue => _shop;
    internal bool Busy => _busy;
    internal bool ShowingAd => _adOverlay.Visible;
    internal int Columns => _offers.Columns;
    internal string Status => _status.Text;
    internal string Wallet => _wallet.Text;

    public void Build(Main main)
    {
        _main = main; _owner = main.Accounts.Profile?.Uid; Name = "ItemShopPanel"; Theme = Ui.Theme(); Ui.Full(this);
        var background = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(background); Ui.Full(background);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 9 : 15);
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.Panel(ArcanaMenuChrome.Surface, 10, ArcanaMenuChrome.Border)); margin.AddChild(panel);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 8); panel.AddChild(body);
        if (Ui.IsMobile) Wrapped(body, "TIENDA · OBJETOS PERMANENTES", 20, Ui.Gold);
        _wallet = Wrapped(body, "Oro de cuenta: —", 21, Ui.Gold); _wallet.Name = "ShopWallet";
        var explanation = Wrapped(body, "Objetos permanentes. Cada oferta se compra una vez; las piezas llegan al inventario sin cambiar tu equipo.", 15, Ui.Muted);
        explanation.Visible = !Ui.IsMobile;
        var toolbar = new HBoxContainer(); toolbar.AddThemeConstantOverride("separation", 8); body.AddChild(toolbar);
        _reload = Ui.Button(Ui.IsMobile ? "Consultar" : "Conectar / consultar", () => _ = Load()); _reload.Name = "ShopConnect";
        _refresh = Ui.Button(Ui.IsMobile ? "Renovar (demo)" : "Renovar · anuncio simulado", () => _ = WatchAd()); _refresh.Name = "ShopRefreshAd";
        _refresh.TooltipText = "Ver un anuncio simulado de tres segundos para renovar las seis ofertas sin gastar oro.";
        var inventory = Ui.Button("Inventario", () => main.CloseItemShop(true)); inventory.Name = "ShopInventory";
        foreach (var button in new[] { _reload, _refresh, inventory })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, Ui.IsMobile ? 54 : 46);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); toolbar.AddChild(button);
        }
        _status = Wrapped(body, "Conectando con la tienda…", 16, Ui.Muted); _status.Name = "ShopStatus";
        var scroll = new ContentScrollContainer { Name = "ShopOffersScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        body.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        _offers = new GridContainer { Name = "ShopOffers", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("h_separation", 10); _offers.AddThemeConstantOverride("v_separation", 10); scroll.AddChild(_offers);
        scroll.Resized += ResizeOffers;
        if (!Ui.IsMobile) ArcanaMenuChrome.Attach(this, margin, main, "Tienda de objetos", 4, main.NavigateOptionAMenu);
        BuildAdOverlay();
        _ = Load();
    }
    private static Label Wrapped(Control parent, string value, int size, Color? color = null)
    {
        var label = Ui.Label(value, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }
    private void ResizeOffers()
    {
        if (_offers?.GetParent() is not Control scroll) return;
        _offers.Columns = scroll.Size.X >= 1100 ? 3 : scroll.Size.X >= 600 ? 2 : 1;
    }
    private void Render(ShopResult result)
    {
        _owner = result.Profile.Uid; _shop = result.Shop; _connected = true; _buyButtons.Clear();
        foreach (var child in _offers.GetChildren()) { _offers.RemoveChild(child); child.QueueFree(); }
        _wallet.Text = $"Oro de cuenta: {result.Profile.Gold:N0} · Inventario: {result.Profile.Items.Length}/{EquipmentCatalogue.InventoryLimit}";
        foreach (var offer in result.Shop.Offers)
        {
            var card = new PanelContainer { Name = "ShopOffer_" + offer.Item.Id, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var style = Ui.Panel(ArcanaMenuChrome.SurfaceRaised, 8, EquipmentPanel.TierColor(offer.Item.Tier));
            style.ContentMarginLeft = style.ContentMarginRight = 10; style.ContentMarginTop = style.ContentMarginBottom = 10;
            card.AddThemeStyleboxOverride("panel", style); _offers.AddChild(card);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 7); card.AddChild(box);
            Wrapped(box, EquipmentCatalogue.SlotName(offer.Item.Slot) + " · " + EquipmentCatalogue.TierName(offer.Item.Tier), 19, EquipmentPanel.TierColor(offer.Item.Tier));
            Wrapped(box, EquipmentCatalogue.FamilyName(offer.Item.Family), 15, Ui.Muted);
            var stats = Ui.Table(2, "ShopStats_" + offer.Item.Id); box.AddChild(stats);
            Ui.TableCell(stats, "Valor", true, header: true); Ui.TableCell(stats, "Atributo", header: true);
            foreach (var bonus in offer.Item.Bonuses)
            {
                Ui.TableCell(stats, EquipmentCatalogue.Percent(bonus.Current), true, bonus.Current < 0 ? Ui.Red : Ui.Blue);
                Ui.TableCell(stats, EquipmentCatalogue.StatName(bonus.Stat)).TooltipText = EquipmentCatalogue.Percent(bonus.Minimum) + " / " + EquipmentCatalogue.Percent(bonus.Maximum);
            }
            bool full = result.Profile.Items.Length >= EquipmentCatalogue.InventoryLimit;
            bool affordable = result.Profile.Gold >= offer.Price;
            string caption = offer.Sold ? "COMPRADO" : $"Comprar · {offer.Price:N0} oro";
            var buy = Ui.Button(caption, () => _ = Buy(offer.Item.Id)); buy.Name = "ShopBuy_" + offer.Item.Id;
            buy.CustomMinimumSize = new(0, 50); buy.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            buy.Disabled = offer.Sold || !affordable || full;
            buy.TooltipText = offer.Sold ? "Esta oferta ya está comprada." : full ? "El inventario está lleno." :
                !affordable ? $"Faltan {offer.Price - result.Profile.Gold} de oro." : "Comprar y guardar esta pieza en tu cuenta.";
            buy.SetMeta("available", !buy.Disabled); box.AddChild(buy); _buyButtons.Add(offer.Item.Id, buy);
        }
        _status.Text = result.Message; ResizeOffers(); SetBusy(false); _main.SaveAccountCache();
    }
    private void SetBusy(bool value, string? status = null)
    {
        _busy = value; _reload.Disabled = value; _refresh.Disabled = value || !_connected || _shop == null || !_main.Accounts.SignedIn;
        foreach (var button in _buyButtons.Values) button.Disabled = value || !_connected || !button.GetMeta("available").AsBool();
        if (status != null) _status.Text = status;
    }
    private void Publish(Action action) => _main.Post(() => { if (!_lifetime.IsCancellationRequested) action(); });
    private void Failed(Exception error)
    {
        _connected = false; SetBusy(false, error.Message + " Pulsa «Consultar» para conectar de nuevo.");
    }
    private async Task Load()
    {
        if (_busy) return;
        SetBusy(true, "Conectando con el servidor de la tienda…");
        try
        {
            await _main.EnsureShopServer(_lifetime.Token).ConfigureAwait(false);
            var result = await _main.Accounts.Shop(_lifetime.Token).ConfigureAwait(false);
            Publish(() => Render(result));
        }
        catch (Exception error) { Publish(() => Failed(error)); }
    }
    private async Task Buy(string itemId)
    {
        if (_busy || _shop == null || !_buyButtons.TryGetValue(itemId, out var button) || button.Disabled) return;
        var request = new ShopPurchaseRequest(_shop.Id, itemId);
        SetBusy(true, "Comprando y guardando el objeto…");
        try { var result = await _main.Accounts.BuyShopItem(request, _lifetime.Token).ConfigureAwait(false); Publish(() => Render(result)); }
        catch (Exception error) { Publish(() => Failed(error)); }
    }
    private void BuildAdOverlay()
    {
        _adOverlay = new Control { Name = "ShopSimulatedAd", Visible = false }; AddChild(_adOverlay); Ui.Full(_adOverlay);
        var shade = new ColorRect { Color = new(0.01f, 0.02f, 0.04f, 0.97f) }; _adOverlay.AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer(); _adOverlay.AddChild(center); Ui.Full(center);
        var panel = new PanelContainer { CustomMinimumSize = new(360, 220) }; center.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(ArcanaMenuChrome.SurfaceRaised, 10, Ui.Gold));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 15); panel.AddChild(box);
        Wrapped(box, "ANUNCIO SIMULADO", 24, Ui.Gold);
        _adMessage = Wrapped(box, "", 20);
        Wrapped(box, "Recompensa: seis ofertas nuevas.\nRenovación gratuita; no consume oro.", 17, Ui.Muted);
        _cancelAd = Ui.Button("Cancelar anuncio", CancelAd); _cancelAd.Name = "ShopCancelAd"; box.AddChild(_cancelAd);
    }
    private async Task WatchAd()
    {
        if (_busy || _shop == null) return;
        string shopId = _shop.Id;
        _adCancellation?.Dispose(); _adCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var token = _adCancellation.Token;
        SetBusy(true, "Preparando el anuncio simulado…");
        try
        {
            var ticket = await _main.Accounts.StartShopAd(shopId, token).ConfigureAwait(false);
            Publish(() => { _adOverlay.Show(); _cancelAd.Disabled = false; });
            for (int remaining = ShopRules.SimulatedAdSeconds; remaining > 0; remaining--)
            {
                int seconds = remaining;
                Publish(() => _adMessage.Text = $"Finaliza en {seconds} s");
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            Publish(() => { _cancelAd.Disabled = true; _adMessage.Text = "Renovando las ofertas…"; });
            var result = await _main.Accounts.CompleteShopAd(new(shopId, ticket.Id), token).ConfigureAwait(false);
            Publish(() => { _adOverlay.Hide(); Render(result); });
        }
        catch (OperationCanceledException)
        { Publish(() => { _adOverlay.Hide(); SetBusy(false, "Anuncio cancelado. Puedes reintentarlo."); }); }
        catch (Exception error)
        { Publish(() => { _adOverlay.Hide(); Failed(error); }); }
    }
    private void CancelAd() => _adCancellation?.Cancel();
    internal void HandleBack()
    {
        if (_adOverlay.Visible && !_cancelAd.Disabled) CancelAd();
        else if (!_adOverlay.Visible) _main.CloseItemShop(true);
    }
    internal void Close()
    {
        _lifetime.Cancel(); _adCancellation?.Cancel(); QueueFree();
    }
    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested || _owner == _main.Accounts.Profile?.Uid) return;
        _owner = _main.Accounts.Profile?.Uid; _adCancellation?.Cancel(); _adOverlay.Hide();
        _shop = null; _connected = false; _buyButtons.Clear();
        foreach (var child in _offers.GetChildren()) { _offers.RemoveChild(child); child.QueueFree(); }
        _wallet.Text = "Oro de cuenta: —";
        SetBusy(false, "La cuenta ha cambiado. Pulsa «Consultar» para cargar sus ofertas.");
    }
    public override void _ExitTree() { _lifetime.Cancel(); _adCancellation?.Cancel(); }
}
