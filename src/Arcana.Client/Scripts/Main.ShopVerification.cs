using Arcana.Core;
using Godot;
using System.Net;
using System.Text.Json;

namespace Arcana.Client;

public partial class Main
{
    private bool _shopSmoke;
    private int _shopSmokePhase;
    private ulong _shopSmokeStarted;
    private string _shopSmokeId = "", _shopBoughtId = "";
    private int _shopBoughtPrice;
    private long _shopGoldAfterPurchase;
    private string? _shopCaptureDirectory;
    private ShopFixtureHandler? _shopFixture;

    private async void StartShopSmoke(string[] arguments)
    {
        _shopSmoke = true; _shopSmokeStarted = Time.GetTicksMsec();
        _shopCaptureDirectory = arguments.FirstOrDefault(a => a.StartsWith("--shop-captures=", StringComparison.Ordinal))?["--shop-captures=".Length..];
        if (!Ui.IsMobile)
        {
            string size = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport=", StringComparison.Ordinal))?["--smoke-viewport=".Length..] ?? "1440x900";
            var parts = size.Split('x'); GetWindow().ContentScaleSize = new(int.Parse(parts[0]), int.Parse(parts[1])); GetWindow().Size = GetWindow().ContentScaleSize;
        }
        try
        {
            _shopFixture = new ShopFixtureHandler(); Accounts.Dispose(); Accounts = new AccountClient(new System.Net.Http.HttpClient(_shopFixture));
            await Accounts.SetFirebaseSession("demo-shop", "shop-fixture", "Comprador", "shop-fixture-token", DateTimeOffset.UtcNow.AddHours(1));
            _accountServerResolver = token => _shopFixture.Connected
                ? Task.FromResult("wss://shop.example/play")
                : Task.FromException<string>(new System.IO.IOException("Servidor de prueba desconectado."));
        }
        catch (Exception error) { GD.PrintErr("SHOP_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
    private bool CheckShopSmoke()
    {
        if (!_shopSmoke) return false;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        try
        {
            if (Time.GetTicksMsec() - _shopSmokeStarted > 45000) throw new TimeoutException("La interfaz de tienda no completó sus operaciones.");
            if (_renderFrames % 6 != 0) return false;
            var shop = _itemShopPanel;
            Button Button(string name) => (Button)shop!.FindChild(name, true, false);
            void CheckUnavailable()
            {
                Check(shop != null && shop.Catalogue == null &&
                    ((GridContainer)shop.FindChild("ShopOffers", true, false)).GetChildCount() == 6 &&
                    shop.FindChild("ShopBuy_*", true, false) == null && Button("ShopRefreshAd").Disabled,
                    "La tienda desconectada conserva ofertas, permite comprar o no muestra su interfaz.");
            }
            if (_shopSmokePhase == 0)
            {
                if (!Accounts.SignedIn || _renderFrames < 20) return false;
                var entry = (Button)_launcher.FindChild("OptionANav7", true, false);
                Check(entry.IsVisibleInTree() && GetViewport().GetVisibleRect().Grow(1).Encloses(entry.GetGlobalRect()),
                    "El acceso a la tienda no es visible en la pantalla principal.");
                CaptureShop("01-inicio"); entry.EmitSignal(Godot.Button.SignalName.Pressed); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 1)
            {
                if (shop == null || shop.Busy) return false;
                CheckUnavailable(); Check(shop.Status.Contains("Sin conexión"), "Falta el aviso de tienda desconectada.");
                CaptureShop("02-sin-conexion"); _shopFixture!.Connected = true;
                Button("ShopConnect").EmitSignal(Godot.Button.SignalName.Pressed); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 2)
            {
                if (shop?.Catalogue == null || shop.Busy) return false;
                Check(shop.Catalogue.Offers.Length == 6 && shop.Columns == (GetViewport().GetVisibleRect().Size.X >= 1200 ? 3 : 2), "No se muestran seis ofertas en columnas adaptadas.");
                foreach (string name in new[] { "ShopWallet", "ShopConnect", "ShopRefreshAd", "ShopInventory", "ShopHome", "ShopOffersScroll" })
                {
                    var control = (Control)shop.FindChild(name, true, false);
                    Check(GetViewport().GetVisibleRect().Grow(1).Encloses(control.GetGlobalRect()), name + " sale de la pantalla.");
                }
                _shopSmokeId = shop.Catalogue.Id; var offer = shop.Catalogue.Offers[0]; _shopBoughtId = offer.Item.Id; _shopBoughtPrice = offer.Price;
                Check(((Control)shop.FindChild("ShopOffersScroll", true, false)).GetGlobalRect().Grow(1).Encloses(Button("ShopBuy_" + _shopBoughtId).GetGlobalRect()),
                    "El primer objeto no permite ver el botón de compra junto con sus atributos.");
                Check(Button("ShopRefreshAd").Disabled, "La renovación por anuncios está activa.");
                CaptureShop("03-ofertas"); Button("ShopBuy_" + _shopBoughtId).EmitSignal(Godot.Button.SignalName.Pressed);
                Button("ShopBuy_" + _shopBoughtId).EmitSignal(Godot.Button.SignalName.Pressed); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 3)
            {
                if (shop!.Busy) return false;
                Check(_shopFixture!.Purchases == 1 && Accounts.Profile!.Items.Single().Id == _shopBoughtId && Accounts.Profile.Gold == 10000 - _shopBoughtPrice &&
                    shop.Catalogue!.Offers[0].Sold && Button("ShopBuy_" + _shopBoughtId).Disabled, "La compra no actualiza el saldo o permite un cobro duplicado.");
                _shopGoldAfterPurchase = Accounts.Profile!.Gold;
                Button("ShopRefreshAd").EmitSignal(Godot.Button.SignalName.Pressed);
                Check(_shopFixture.AdRequests == 0 && shop.Catalogue!.Id == _shopSmokeId, "El botón de anuncios contactó con el servidor o renovó el catálogo.");
                CaptureShop("04-compra"); Button("ShopInventory").EmitSignal(Godot.Button.SignalName.Pressed); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 4)
            {
                if (_equipmentPanel?.Saving != false) return false;
                Check(_equipmentPanel.VisibleItems == 1 && _equipmentPanel.PreviewProfile!.Items.Single().Id == _shopBoughtId, "La pieza comprada no aparece en el inventario.");
                Check(_equipmentPanel.FindChild("EquipmentOpenShop", true, false) == null &&
                    !_equipmentPanel.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text is "Servidor" or "Guardar configuración") &&
                    _equipmentPanel.FindChildren("*", "LineEdit", true, false).Count == 0, "El inventario conserva la tienda o la configuración manual del servidor.");
                NavigateOptionAMenu(0); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 5)
            {
                if (GodotObject.IsInstanceValid(_equipmentPanel) && !_equipmentPanel!.IsQueuedForDeletion()) return false;
                ((Button)_launcher.FindChild("OptionANav7", true, false)).EmitSignal(Godot.Button.SignalName.Pressed);
                _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 6)
            {
                if (shop?.Catalogue == null || shop.Busy) return false;
                Check(shop.Catalogue.Id == _shopSmokeId && shop.Catalogue.Offers[0].Sold, "Cerrar y abrir volvió a renovar el catálogo o restauró la oferta comprada.");
                _shopFixture!.Connected = false;
                Button("ShopBuy_" + shop.Catalogue.Offers.First(o => !o.Sold).Item.Id).EmitSignal(Godot.Button.SignalName.Pressed);
                _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 7)
            {
                if (shop!.Busy) return false;
                CheckUnavailable(); Check(shop.Status.Contains("Sin conexión") && Accounts.Profile!.Gold == _shopGoldAfterPurchase && _shopFixture!.Purchases == 1,
                    "Una compra sin conexión gastó oro o conservó las ofertas.");
                CaptureShop("05-desconexion"); _shopFixture!.Connected = true; _shopFixture.Gold = 0;
                Button("ShopConnect").EmitSignal(Godot.Button.SignalName.Pressed); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 8)
            {
                if (shop!.Busy) return false;
                Check(Accounts.Profile!.Gold == 0 && shop.Catalogue!.Offers.All(o => Button("ShopBuy_" + o.Item.Id).Disabled), "Se permite comprar sin oro.");
                Check(Button("ShopRefreshAd").Disabled && _shopFixture!.AdRequests == 0, "Los anuncios están activos tras reconectar.");
                CaptureShop("06-sin-oro"); _ = Accounts.SignOut(); _shopSmokePhase++; return false;
            }
            if (_shopSmokePhase == 9)
            {
                if (Accounts.SignedIn || shop!.Catalogue != null) return false;
                CheckUnavailable(); Check(shop.Status.Contains("Inicia sesión") && shop.Wallet == "Oro de cuenta: —", "Cerrar sesión conserva datos de compra de la cuenta anterior.");
                Check(_engineLog.ErrorCount == 0, "Godot registró errores en la tienda.");
                GD.Print("SHOP_SMOKE_OK: acceso desde Inicio, interfaz sin conexión, compra única, saldo, inventario sin configuración, catálogo estable, desconexión, reconexión, cierre de sesión y anuncios desactivados.");
                _shopSmoke = false; GetTree().Quit(); return true;
            }
        }
        catch (Exception error) { CaptureShop("00-error"); GD.PrintErr("SHOP_SMOKE_FAILED: " + error); GetTree().Quit(1); return true; }
        return false;
    }
    private void CaptureShop(string name)
    {
        if (_shopCaptureDirectory == null || DisplayServer.GetName() == "headless") return;
        System.IO.Directory.CreateDirectory(_shopCaptureDirectory);
        var path = System.IO.Path.Combine(_shopCaptureDirectory, name + ".png");
        if (GetViewport().GetTexture().GetImage().SavePng(path) != Error.Ok) throw new System.IO.IOException("No se pudo guardar " + path);
        GD.Print("SHOP_CAPTURE_OK: " + name);
    }
    private sealed class ShopFixtureHandler : HttpMessageHandler
    {
        public long Gold = 10000;
        public int Purchases, AdRequests;
        public bool Connected;
        private ItemShop _shop = ShopRules.Generate(new(53));
        private readonly List<EquipmentItem> _items = [];
        private AccountProfile Profile => new("shop-fixture", "Comprador", Gold, 0, 0, new(), _items.ToArray());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/account/shop", StringComparison.Ordinal) && !Connected)
                throw new System.Net.Http.HttpRequestException("Servidor de prueba desconectado.");
            if (path == "/account/shop/buy")
            {
                var buy = Protocol.Deserialize<ShopPurchaseRequest>(await request.Content!.ReadAsStringAsync(token))!;
                var offer = _shop.Offers.Single(o => o.Item.Id == buy.ItemId);
                if (!offer.Sold) { Gold -= offer.Price; _items.Add(offer.Item); Purchases++; _shop = _shop with { Offers = _shop.Offers.Select(o => o.Item.Id == buy.ItemId ? o with { Sold = true } : o).ToArray() }; }
            }
            if (path.StartsWith("/account/shop/ad/", StringComparison.Ordinal))
            { AdRequests++; throw new InvalidOperationException("Los anuncios no deben invocarse desde la tienda."); }
            if (path.StartsWith("/account/shop", StringComparison.Ordinal)) return Json(new ShopResult(Profile, _shop, "Tienda conectada."));
            if (path.EndsWith("/items", StringComparison.Ordinal)) return Json(new { documents = _items.Select(Document).ToArray() });
            if (path.EndsWith("/loadouts/active", StringComparison.Ordinal)) return Json(Document(new EquipmentLoadout()));
            return Json(Document(new { uid = Profile.Uid, name = Profile.Name, gold = Gold, wins = 0, losses = 0, itemCount = _items.Count, schemaVersion = 1 }));
        }
        private static object Document(object value) => new { fields = JsonSerializer.SerializeToElement(value, Protocol.Json).EnumerateObject().ToDictionary(p => p.Name, p => FirestoreValue(p.Value)) };
        private static object FirestoreValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => new { stringValue = value.GetString() },
            JsonValueKind.Number when value.TryGetInt64(out long integer) => new { integerValue = integer.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            JsonValueKind.Number => new { doubleValue = value.GetDouble() },
            JsonValueKind.Array => new { arrayValue = new { values = value.EnumerateArray().Select(FirestoreValue).ToArray() } },
            JsonValueKind.Object => new { mapValue = new { fields = value.EnumerateObject().ToDictionary(p => p.Name, p => FirestoreValue(p.Value)) } },
            _ => new { nullValue = (object?)null }
        };
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Protocol.Json)) };
    }
}
