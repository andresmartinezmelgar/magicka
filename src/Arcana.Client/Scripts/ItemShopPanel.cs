using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>La interfaz siempre está disponible; solo el servidor puede proporcionar ofertas y aceptar compras.</summary>
public partial class ItemShopPanel : Control
{
    private Main _main = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private ItemShop? _shop;
    private Label _wallet = null!, _status = null!;
    private Button _reload = null!, _refresh = null!;
    private GridContainer _offers = null!;
    private ContentScrollContainer _scroll = null!;
    private readonly Dictionary<string, Button> _buyButtons = new(StringComparer.Ordinal);
    private bool _busy, _connected;
    private string? _owner;
    private int _requestVersion;
    private AcceptDialog _comparison = null!;
    private Label _comparisonText = null!;
    internal ItemShop? Catalogue => _shop;
    internal bool Busy => _busy;
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
        _scroll = new ContentScrollContainer { Name = "ShopOffersScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(_scroll); Ui.VisibleVerticalScroll(_scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8); _scroll.AddChild(body);
        if (Ui.IsMobile) Wrapped(body, "TIENDA · OBJETOS PERMANENTES", 20, Ui.Gold);
        _wallet = Wrapped(body, "Oro de cuenta: —", 21, Ui.Gold); _wallet.Name = "ShopWallet";
        var explanation = Wrapped(body, "Objetos permanentes. Cada oferta se compra una vez; las piezas llegan al inventario sin cambiar tu equipo.", 15, Ui.Muted);
        explanation.Visible = !Ui.IsMobile;
        var toolbar = new HBoxContainer(); toolbar.AddThemeConstantOverride("separation", 8); body.AddChild(toolbar);
        _reload = Ui.Button("Consultar", () => _ = Load()); _reload.Name = "ShopConnect";
        _refresh = Ui.Button(Ui.IsMobile ? "Sin anuncios" : "Renovar · pronto", () => { }); _refresh.Name = "ShopRefreshAd";
        _refresh.Disabled = true;
        _refresh.TooltipText = "La renovación por anuncios todavía no está disponible.";
        foreach (var button in new[] { _reload, _refresh })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, Ui.IsMobile ? 54 : 46);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); toolbar.AddChild(button);
        }
        _status = Wrapped(body, "Conectando con la tienda…", 16, Ui.Muted); _status.Name = "ShopStatus";
        _offers = new GridContainer { Name = "ShopOffers", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("h_separation", 10); _offers.AddThemeConstantOverride("v_separation", 10); body.AddChild(_offers);
        _scroll.Resized += ResizeOffers;
        _comparison = new AcceptDialog { Name = "ShopComparison", Title = "Comparación del equipo", OkButtonText = "Volver a la oferta",
            Theme = Ui.Theme(), Exclusive = true, Transient = true }; AddChild(_comparison);
        Ui.ConfigureDialog(_comparison);
        var comparisonScroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _comparison.AddChild(comparisonScroll); Ui.VisibleVerticalScroll(comparisonScroll);
        var comparisonBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; comparisonScroll.AddChild(comparisonBody);
        comparisonBody.AddThemeConstantOverride("separation", 12); comparisonBody.AddChild(Ui.Label("COMPARACIÓN DEL EQUIPO", 24, Ui.Gold));
        _comparisonText = Wrapped(comparisonBody, "", 17); _comparisonText.Name = "ShopComparisonText";
        ArcanaMenuChrome.Attach(this, margin, main, "Tienda de objetos", 7, main.NavigateOptionAMenu);
        ShowUnavailable("Conectando con la tienda…");
        _ = Load();
    }
    private static Label Wrapped(Control parent, string value, int size, Color? color = null)
    {
        var label = Ui.Label(value, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label); return label;
    }
    private void ResizeOffers()
    {
        if (_scroll == null || _offers == null) return;
        float width = _scroll.Size.X - _scroll.GetVScrollBar().Size.X;
        _offers.Columns = width >= 1100 ? 3 : width >= 600 ? 2 : 1;
    }
    private void ClearOffers()
    {
        _shop = null; _connected = false; _buyButtons.Clear();
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
    private void Render(ShopResult result, string message = "Tienda conectada. Las ofertas se conservan al cerrar esta pantalla.")
    {
        if (!_main.Accounts.SignedIn || result.Profile.Uid != _main.Accounts.Profile?.Uid) return;
        ClearOffers();
        _owner = result.Profile.Uid; _shop = result.Shop; _connected = true; _buyButtons.Clear();
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
            if (offer.Item.Affix != null) Wrapped(box, "Especial: " + ItemAffixes.Describe(offer.Item.Affix), 16, Ui.Gold);
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
    private void SetBusy(bool value, string? status = null)
    {
        _busy = value; _reload.Disabled = value; _refresh.Disabled = true;
        foreach (var button in _buyButtons.Values) button.Disabled = value || !_connected || !button.GetMeta("available").AsBool();
        if (status != null) _status.Text = status;
    }
    private void Publish(int request, Action action) => _main.Post(() =>
    { if (!_lifetime.IsCancellationRequested && request == _requestVersion) action(); });
    private void Failed(Exception error)
    {
        Diagnostics.Warn("shop.unavailable", "No se pudo consultar o completar la compra de la tienda.", error);
        ShowUnavailable(!_main.Accounts.SignedIn
            ? "Inicia sesión con Google desde Perfil para consultar las ofertas y comprar objetos."
            : error is System.IO.IOException or System.Net.Http.HttpRequestException or OperationCanceledException
            ? "Sin conexión con la tienda. No hay objetos disponibles para comprar. Pulsa «Consultar» para reintentar."
            : error.Message + " Pulsa «Consultar» para actualizar las ofertas.");
    }
    private async Task Load()
    {
        if (_busy) return;
        int request = ++_requestVersion;
        ShowUnavailable("Conectando con el servidor de la tienda…");
        SetBusy(true, "Conectando con el servidor de la tienda…");
        try
        {
            await _main.EnsureShopServer(_lifetime.Token).ConfigureAwait(false);
            var result = await _main.Accounts.Shop(_lifetime.Token).ConfigureAwait(false);
            Publish(request, () => Render(result));
        }
        catch (Exception error) { Publish(request, () => Failed(error)); }
    }
    private async Task Buy(string itemId)
    {
        if (_busy || !_connected || _shop == null || !_main.Accounts.SignedIn || !_buyButtons.TryGetValue(itemId, out var button) || button.Disabled) return;
        int request = ++_requestVersion;
        var purchase = new ShopPurchaseRequest(_shop.Id, itemId);
        SetBusy(true, "Comprando y guardando el objeto…");
        try { var result = await _main.Accounts.BuyShopItem(purchase, _lifetime.Token).ConfigureAwait(false); Publish(request, () => Render(result, "Objeto comprado y guardado en tu inventario.")); }
        catch (Exception error) { Publish(request, () => Failed(error)); }
    }
    internal void HandleBack() { if (_comparison.Visible) _comparison.Hide(); else _main.NavigateOptionAMenu(0); }
    internal void Close()
    {
        _lifetime.Cancel(); QueueFree();
    }
    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested || _owner == _main.Accounts.Profile?.Uid && (!_connected || _main.Accounts.SignedIn)) return;
        _owner = _main.Accounts.Profile?.Uid; ++_requestVersion;
        ShowUnavailable(_main.Accounts.SignedIn
            ? "La cuenta ha cambiado. Pulsa «Consultar» para cargar sus ofertas."
            : "Inicia sesión con Google desde Perfil para consultar las ofertas y comprar objetos.");
    }
    public override void _ExitTree() => _lifetime.Cancel();
}
