using Arcana.Core;
using Godot;
using System.Net;
using System.Text.Json;

namespace Arcana.Client;

public partial class Main
{
    private bool _saleSmoke, _saleLogoutProbe;
    private int _salePhase;
    private ulong _saleStarted;
    private ulong _salePauseUntil;
    private string? _saleCaptureDirectory;
    private SaleFixtureHandler? _saleFixture;
    private readonly TaskCompletionSource<string> _saleServerReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private async void StartSaleSmoke(string[] args)
    {
        _saleSmoke = true; _saleStarted = Time.GetTicksMsec(); _saleLogoutProbe = args.Contains("--sale-logout-repro");
        _quickItemActions = args.Contains("--quick-item-actions");
        _saleCaptureDirectory = args.FirstOrDefault(a => a.StartsWith("--sale-captures=", StringComparison.Ordinal))?["--sale-captures=".Length..];
        if (!Ui.IsMobile)
        {
            var dimensions = (args.FirstOrDefault(a => a.StartsWith("--smoke-viewport=", StringComparison.Ordinal))?["--smoke-viewport=".Length..] ?? "1440x900").Split('x');
            GetWindow().ContentScaleSize = new(int.Parse(dimensions[0]), int.Parse(dimensions[1])); GetWindow().Size = GetWindow().ContentScaleSize;
        }
        try
        {
            _saleFixture = new SaleFixtureHandler { IncludeSpare = _quickItemActions }; Accounts.Dispose(); Accounts = new(new System.Net.Http.HttpClient(_saleFixture));
            await Accounts.SetFirebaseSession("demo-sale", "sale-fixture", "Mercader", "sale-token", DateTimeOffset.UtcNow.AddHours(1));
            _accountServerResolver = token => _saleServerReady.Task.WaitAsync(token);
            if (_saleLogoutProbe) _saleServerReady.SetResult("wss://sales.example/play");
            OpenEquipment("automatic");
        }
        catch (Exception error) { GD.PrintErr("SALE_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
    private bool CheckSaleSmoke()
    {
        if (!_saleSmoke) return false;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        try
        {
            if (Time.GetTicksMsec() - _saleStarted > 40000) throw new TimeoutException("No se completó la verificación de ventas.");
            if (_renderFrames % 6 != 0 || _saleFixture == null || _equipmentPanel == null) return false;
            var panel = _equipmentPanel; var fixture = _saleFixture;
            var dialog = panel.GetChildren().OfType<ConfirmationDialog>().Single();
            Button Trash(string id) => (Button)panel.FindChild("EquipmentTrash_" + id, true, false);
            if (_salePhase == 0)
            {
                if (panel.Saving) return false;
                panel.PressInspect(EquipmentSlot.Weapon);
                if (!_saleLogoutProbe)
                {
                    Check(Trash(fixture.Weapon.Id).Disabled, "Se permite vender antes de conectar el servidor.");
                    _saleServerReady.SetResult("wss://sales.example/play");
                }
                _salePhase++; return false;
            }
            if (_salePhase == 1)
            {
                if (Accounts.ServerAddress.Length == 0) return false;
                Check(!Trash(fixture.Weapon.Id).Disabled, "Conectar el servidor no habilita la venta en el selector que ya estaba abierto.");
                if (_quickItemActions) ClickItemWithPointer(panel, "EquipmentTrash_" + fixture.Weapon.Id, 1);
                else panel.PressTrash(fixture.Weapon.Id);
                _salePhase++; return false;
            }
            if (_salePhase == 2)
            {
                if (_quickItemActions && (ItemGestureRunning() || !dialog.Visible)) return false;
                Check(dialog.Visible, "No se muestra la confirmación de venta.");
                if (_saleLogoutProbe) { _ = Accounts.SignOut(); _salePhase = 9; return false; }
                var stats = (GridContainer)dialog.FindChild("SaleStats", true, false);
                Check(stats.GetChildren().OfType<Label>().Count() == (fixture.Weapon.Bonuses.Length + 1) * 2 &&
                    ((Label)dialog.FindChild("SaleGold", true, false)).Text.Contains("400") &&
                    ((Label)dialog.FindChild("SaleWarning", true, false)).Text.Contains("desequipará"), "La confirmación no muestra atributos, precio o aviso de pieza equipada.");
                // Los límites de ventanas y texto se verifican con render real en tres tamaños.
                if (DisplayServer.GetName() != "headless")
                {
                    var bounds = new Rect2(Vector2.Zero, dialog.Size);
                    foreach (var control in new Control[] { stats, (Control)dialog.FindChild("SaleWarning", true, false), dialog.GetOkButton(), dialog.GetCancelButton() })
                        Check(bounds.Grow(2).Encloses(control.GetGlobalRect()), "El contenido de la venta no cabe en la ventana: " + control.Name);
                    Check(GetViewport().GetVisibleRect().Grow(2).Encloses(new Rect2(dialog.Position, dialog.Size)),
                        $"La ventana de venta sale de la pantalla: {dialog.Position} / {dialog.Size} / {GetViewport().GetVisibleRect()}.");
                    var scroll = (ScrollContainer)dialog.FindChild("SaleContentScroll", true, false);
                    Check(scroll.ScrollVertical == 0 && scroll.GetGlobalRect().Grow(2).Encloses(((Control)dialog.FindChild("SaleWarning", true, false)).GetGlobalRect()),
                        "La venta requiere desplazar el contenido para leer el aviso.");
                }
                CaptureSale("01-mitico-equipado"); panel.HandleBack();
                Check(!dialog.Visible && panel.PickerVisible && fixture.Sales == 0, "Cancelar vendió la pieza o cerró el selector.");
                if (_quickItemActions)
                {
                    panel.PressChoice("");
                    Check(panel.PreviewProfile!.Loadout.Weapon == "" && !Trash(fixture.Weapon.Id).Disabled,
                        "Quitar una pieza impide venderla.");
                    panel.PressChoice(fixture.Spare.Id);
                    ClickItemWithPointer(panel, "EquipmentTrash_" + fixture.Weapon.Id, 2);
                }
                else panel.PressTrash(fixture.Weapon.Id);
                _salePhase++; return false;
            }
            if (_salePhase == 3)
            {
                if (_quickItemActions) { if (ItemGestureRunning()) return false; }
                else
                {
                    dialog.GetOkButton().EmitSignal(Button.SignalName.Pressed);
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                }
                _salePhase++; return false;
            }
            if (_salePhase == 4)
            {
                if (panel.Saving) return false;
                Check(fixture.Sales == 1 && fixture.SaleAttempts == 1 && Accounts.Profile!.Gold == 2900 && panel.PreviewProfile!.Items.All(i => i.Id != fixture.Weapon.Id) &&
                    panel.PreviewProfile.Loadout.Weapon == (_quickItemActions ? fixture.Spare.Id : "") && panel.PickerVisible && !dialog.Visible,
                    "Vender no actualiza el oro, elimina otra pieza, duplica la venta o conserva la ventana.");
                if (_quickItemActions)
                {
                    Check(Accounts.Profile!.Loadout.Weapon == "" && panel.PreviewProfile!.Items.Any(i => i.Id == fixture.Spare.Id),
                        "El doble clic elimina otra pieza o guarda la selección pendiente antes de salir.");
                    GD.Print("QUICK_SALE_OK: clic simple con modal, doble clic/toque vende una vez sin modal y conserva la selección pendiente.");
                }
                panel.PressInspect(EquipmentSlot.Boots); panel.PressTrash(fixture.Boots.Id); _salePhase++; return false;
            }
            if (_salePhase == 5)
            {
                Check(dialog.Visible && !((Label)dialog.FindChild("SaleWarning", true, false)).Text.Contains("desequipará"), "El aviso dice que se desequipará una pieza sin equipar.");
                CaptureSale("02-pieza-sin-equipar");
                if (_quickItemActions)
                {
                    panel.HandleBack(); ClickItemWithPointer(panel, "EquipmentTrash_" + fixture.Boots.Id, 1, backAfter: true);
                    _salePhase = 6; return false;
                }
                _ = Accounts.SignOut(); _salePhase = 9; return false;
            }
            if (_salePhase == 6)
            {
                if (ItemGestureRunning()) return false;
                _salePauseUntil = Time.GetTicksMsec() + 450; _salePhase = 7; return false;
            }
            if (_salePhase == 7)
            {
                if (Time.GetTicksMsec() < _salePauseUntil) return false;
                Check(!dialog.Visible && !panel.PickerVisible && fixture.SaleAttempts == 1, "Salir del selector deja un clic pendiente que abre una venta después.");
                GD.Print("QUICK_CANCEL_OK: salir del selector cancela el clic pendiente sin vender ni abrir una modal tardía.");
                panel.PressInspect(EquipmentSlot.Boots); panel.PressTrash(fixture.Boots.Id);
                _ = Accounts.SignOut(); _salePhase = 9; return false;
            }
            if (_salePhase == 9)
            {
                if (Accounts.Profile != null) return false;
                Check(!dialog.Visible && !panel.PickerVisible && fixture.Sales == (_saleLogoutProbe ? 0 : 1), "Cerrar sesión deja visible una venta de la cuenta anterior.");
                Check(_engineLog.ErrorCount == 0, "Godot registró errores durante la venta.");
                GD.Print("SALE_SMOKE_OK: conexión tardía, pieza y atributos, saldo, cancelar, venta única, desequipado y cierre de sesión.");
                _saleSmoke = false; GetTree().Quit(); return true;
            }
        }
        catch (Exception error) { CaptureSale("00-error"); GD.PrintErr("SALE_SMOKE_FAILED: " + error); GetTree().Quit(1); return true; }
        return false;
    }
    private void CaptureSale(string name)
    {
        if (_saleCaptureDirectory == null || DisplayServer.GetName() == "headless") return;
        System.IO.Directory.CreateDirectory(_saleCaptureDirectory);
        if (GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_saleCaptureDirectory, name + ".png")) != Error.Ok)
            throw new System.IO.IOException("No se pudo guardar la captura de venta.");
        GD.Print("SALE_CAPTURE_OK: " + name);
    }
    private sealed class SaleFixtureHandler : HttpMessageHandler
    {
        public readonly EquipmentItem Weapon = EquipmentCatalogue.Generate(new(81), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Fire);
        public readonly EquipmentItem Boots = EquipmentCatalogue.Generate(new(82), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
        public readonly EquipmentItem Spare = EquipmentCatalogue.Generate(new(83), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon);
        public bool IncludeSpare;
        public int Sales, SaleAttempts;
        private long _gold = 2500;
        private bool _sold;
        private EquipmentLoadout Loadout => new(Weapon: _sold ? "" : Weapon.Id);
        private EquipmentItem[] Items => (_sold ? new[] { Boots } : new[] { Weapon, Boots }).Concat(IncludeSpare ? [Spare] : Array.Empty<EquipmentItem>()).ToArray();
        private AccountProfile Profile => new("sale-fixture", "Mercader", _gold, 0, 0, Loadout, Items);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/account/sell")
            {
                SaleAttempts++;
                var sale = Protocol.Deserialize<InventoryRequest>(await request.Content!.ReadAsStringAsync(token))!;
                if (sale.ItemId != Weapon.Id || _sold) return Json(new ApiError("La pieza ya no está disponible."), HttpStatusCode.Conflict);
                _sold = true; _gold += Weapon.SaleGold; Sales++; return Json(new InventoryResult(Profile, "Venta confirmada."));
            }
            if (path.EndsWith("/items", StringComparison.Ordinal)) return Json(new { documents = Items.Select(Document).ToArray() });
            if (path.EndsWith("/loadouts/active", StringComparison.Ordinal)) return Json(Document(Loadout));
            return Json(Document(new { uid = Profile.Uid, name = Profile.Name, gold = _gold, wins = 0, losses = 0, itemCount = Items.Length, schemaVersion = 1 }));
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
        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value, Protocol.Json)) };
    }
}
