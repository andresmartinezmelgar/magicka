using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>La interfaz siempre está disponible; solo el servidor puede proporcionar ofertas y aceptar compras.</summary>
public partial class ItemShopPanel : Control
{
    private Main _main = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private ItemShop? _shop;
    private ElementTextLabel _wallet = null!, _status = null!;
    private Button _reload = null!, _refresh = null!;
    private GridContainer _offers = null!;
    private ContentScrollContainer _scroll = null!;
    private readonly Dictionary<string, Button> _buyButtons = new(StringComparer.Ordinal);
    private bool _busy, _connected, _waitingForAutoSales, _adsRemoved;
    private bool _adsEnabled = true, _checkingAdPolicy;
    private ulong _adPolicyRetryAt;
    internal void ResumeAfterAutoSales() { if (_waitingForAutoSales && !_lifetime.IsCancellationRequested) _ = Load(); }
    private string? _owner;
    private AccountClient _account = null!;
    private long _session;
    private int _requestVersion;
    private DateTimeOffset _serverTime;
    private ulong _receivedAt, _renewalRetryAt;
    private AcceptDialog _comparison = null!;
    private ElementTextLabel _comparisonText = null!;
    private RewardedAdsConfiguration? _ads;
    private Button _privacy = null!, _restore = null!;
    private AndroidPlayBilling? _billing;
    private DateTimeOffset ShopNow => _serverTime == default ? DateTimeOffset.UtcNow : _serverTime.AddMilliseconds(Time.GetTicksMsec() - _receivedAt);
    private bool WithoutAd => !_adsEnabled || _main.Accounts.SignedIn && _main.Accounts.Profile?.Access.WithoutAds(ShopNow) == true;
    internal ItemShop? Catalogue => _shop;
    internal bool Busy => _busy;
    internal int Columns => _offers.Columns;
    internal string Status => _status.Text;
    internal string Wallet => _wallet.Text;

    public void Build(Main main)
    {
        _main = main; _owner = main.Accounts.Profile?.Uid; Name = "ItemShopPanel"; Theme = Ui.Theme(); Ui.Full(this);
        _account = main.Accounts; _session = _account.SessionRevision;
        var background = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(background); Ui.Full(background);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 9 : 15);
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", Ui.Panel(ArcanaMenuChrome.Surface, 14, ArcanaMenuChrome.Border)); margin.AddChild(panel);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); panel.AddChild(layout);
        _billing = new AndroidPlayBilling();
        BuildStoreHeader(layout);
        BuildStoreTabs(layout);
        _scroll = new ContentScrollContainer { Name = "ShopOffersScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(_scroll); Ui.VisibleVerticalScroll(_scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8); _scroll.AddChild(body);
        _status = Wrapped(body, "Conectando con la tienda…", 15, Ui.Muted); _status.Name = "ShopStatus";
        _equipmentArea = new VBoxContainer { Name = "ShopEquipmentArea", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _equipmentArea.AddThemeConstantOverride("separation", 10); body.AddChild(_equipmentArea);
        var explanation = Wrapped(_equipmentArea, "EQUIPAMIENTO DEL DÍA · Seis piezas para tu próxima partida", 18, Ui.Gold);
        explanation.Visible = !Ui.IsMobile;
        var toolbar = new HBoxContainer(); toolbar.AddThemeConstantOverride("separation", 8); _equipmentArea.AddChild(toolbar);
        _refresh = Ui.Button("Ver anuncio y renovar ofertas · 1/día", () => _ = RenewAfterAd()); _refresh.Name = "ShopRefreshAd";
        _refresh.Disabled = true;
        _refresh.TooltipText = "Un anuncio completado permite sustituir las seis ofertas una vez al día. Las compras anteriores se conservan. El día se reinicia a las 00:00, hora peninsular.";
        _privacy = Ui.Button("Privacidad de anuncios", () => _main.RewardedAds.PrivacyOptions()); _privacy.Name = "ShopAdsPrivacy";
        _privacy.Visible = false; _equipmentArea.AddChild(_privacy);
        foreach (var button in new[] { _refresh })
        {
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, Ui.IsMobile ? 54 : 46);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16); toolbar.AddChild(button);
        }
        _offers = new GridContainer { Name = "ShopOffers", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("h_separation", 12); _offers.AddThemeConstantOverride("v_separation", 12); _equipmentArea.AddChild(_offers);
        BuildCommercialAreas(body, layout);
        _scroll.Resized += ResizeOffers;
        _comparison = new AcceptDialog { Name = "ShopComparison", Title = "Comparación del equipo", OkButtonText = "Volver a la oferta",
            Theme = Ui.Theme(), Exclusive = true, Transient = true }; AddChild(_comparison);
        Ui.ConfigureDialog(_comparison);
        var comparisonScroll = new ContentScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _comparison.AddChild(comparisonScroll); Ui.VisibleVerticalScroll(comparisonScroll);
        var comparisonBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; comparisonScroll.AddChild(comparisonBody);
        comparisonBody.AddThemeConstantOverride("separation", 12); comparisonBody.AddChild(Ui.Label("COMPARACIÓN DEL EQUIPO", 24, Ui.Gold));
        _comparisonText = Wrapped(comparisonBody, "", 17); _comparisonText.Name = "ShopComparisonText";
        ArcanaMenuChrome.Attach(this, margin, main, "Tienda", 7, main.NavigateOptionAMenu);
        if (!main.InventoryAutoSelling && main.PreparedShop is { } prepared) Render(prepared);
        else
        {
            ShowUnavailable("Conectando con la tienda…");
            _ = Load();
        }
    }
    public override void _Process(double delta)
    {
        if (_lifetime.IsCancellationRequested) return;
        if (_adsRemoved != WithoutAd) SetBusy(_busy);
        _privacy.Visible = _main.RewardedAds.PrivacyRequired;
        if (!ReferenceEquals(_account, _main.Accounts) || _session != _main.Accounts.SessionRevision ||
            _owner != _main.Accounts.Profile?.Uid || _connected && !_main.Accounts.SignedIn)
        {
            _account = _main.Accounts; _session = _account.SessionRevision;
            _owner = _main.Accounts.Profile?.Uid; ++_requestVersion;
            ShowUnavailable(_main.Accounts.SignedIn
                ? "Cargando las ofertas de la cuenta…"
                : "Inicia sesión con Google desde Perfil para consultar las ofertas y comprar objetos.");
            if (_main.Accounts.SignedIn) _ = Load();
            return;
        }
        if (_busy || !_connected || !_main.Accounts.SignedIn || _shop?.RefreshAt == null) return;
        ulong now = Time.GetTicksMsec();
        if (!_checkingAdPolicy && now >= _adPolicyRetryAt) _ = RefreshAdPolicy();
        // La decisión se basa en la hora del servidor y tiempo transcurrido,
        // sin confiar en que el reloj del teléfono esté bien ajustado.
        if (now >= _renewalRetryAt && _serverTime.AddMilliseconds(now - _receivedAt) >= _shop.RefreshAt.Value)
        {
            _renewalRetryAt = now + 60000;
            _ = Load(refresh: true);
        }
    }
}
