using Arcana.Core;
using Godot;
using System.Net;

namespace Arcana.Client;

public partial class Main
{
    private bool _usabilitySmoke;
    private int _usabilityStage;
    private ulong _usabilityStarted, _usabilityWait;
    private UsabilityFixture? _usabilityFixture;
    private string? _usabilityCaptures;
    private Vector2 _controlBefore;
    private async void StartUsabilitySmoke(string[] args)
    {
        _usabilitySmoke = true; _usabilityStarted = Time.GetTicksMsec();
        _usabilityCaptures = args.FirstOrDefault(a => a.StartsWith("--usability-captures="))?["--usability-captures=".Length..];
        if (!Ui.IsMobile)
        {
            var size = (args.FirstOrDefault(a => a.StartsWith("--smoke-viewport="))?["--smoke-viewport=".Length..] ?? "1440x900").Split('x');
            GetWindow().ContentScaleSize = new(int.Parse(size[0]), int.Parse(size[1])); GetWindow().Size = GetWindow().ContentScaleSize;
        }
        try
        {
            _usabilityFixture = new(); Accounts.Dispose(); Accounts = new(new System.Net.Http.HttpClient(_usabilityFixture));
            var challenge = await Accounts.BeginLogin("wss://usability.example/play", CancellationToken.None);
            await Accounts.PollLogin(challenge, CancellationToken.None);
            OpenEquipment("wss://usability.example/play");
        }
        catch (Exception error) { GD.PrintErr("USABILITY_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
    private void CaptureUsability(string name)
    {
        if (_usabilityCaptures == null || DisplayServer.GetName() == "headless") return;
        Directory.CreateDirectory(_usabilityCaptures);
        GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_usabilityCaptures, name + ".png"));
    }
    private bool CheckUsabilitySmoke()
    {
        if (!_usabilitySmoke) return false;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        try
        {
            if (Time.GetTicksMsec() - _usabilityStarted > 35000) throw new TimeoutException("Prueba de usabilidad incompleta.");
            if (_renderFrames % 8 != 0 || _usabilityFixture == null || _equipmentPanel == null || _equipmentPanel.Saving) return false;
            var panel = _equipmentPanel; var fixture = _usabilityFixture;
            Button Button(string name) => (Button)panel.FindChild(name, true, false);
            var teams = (OptionButton)panel.FindChild("EquipmentSavedTeam", true, false);
            var dialog = panel.GetChildren().OfType<ConfirmationDialog>().Single();
            switch (_usabilityStage)
            {
                case 0:
                    var name = (LineEdit)panel.FindChild("EquipmentTeamName", true, false);
                    name.Text = "Fuego"; Button("EquipmentSaveTeam").EmitSignal(BaseButton.SignalName.Pressed);
                    panel.PressInspect(EquipmentSlot.Weapon); panel.PressChoice(fixture.Extras[1].Id);
                    name.Text = "Frío"; Button("EquipmentSaveTeam").EmitSignal(BaseButton.SignalName.Pressed);
                    teams.Select(0); teams.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                    Button("EquipmentApplyTeam").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(panel.PreviewProfile!.Loadout == fixture.Profile.Loadout && teams.ItemCount == 2, "Guardar y aplicar equipos no restaura las cuatro posiciones.");
                    foreach (string id in new[] { fixture.Profile.Loadout.Weapon, fixture.Extras[0].Id })
                    {
                        Button("EquipmentFavorite_" + id).EmitSignal(BaseButton.SignalName.Pressed);
                        Check(Button("EquipmentTrash_" + id).Disabled, "Marcar favorito permite vender.");
                        panel.PressTrash(id); panel.PressTrash(id);
                    }
                    Check(!dialog.Visible && fixture.Attempts == 0, "Una venta rápida evita la protección del favorito.");
                    var saved = DevicePreferences.Read<Dictionary<string, InventoryPreferences>>("inventory-preferences")!;
                    Check(saved[fixture.Profile.Uid].Favorites.Count == 2 && saved[fixture.Profile.Uid].Loadouts.Count == 2, "Favoritos y equipos no se guardan en el disco.");
                    var sort = (OptionButton)panel.FindChild("EquipmentSort", true, false);
                    sort.Select((int)EquipmentStat.ColdPower + 1); sort.EmitSignal(OptionButton.SignalName.ItemSelected, sort.Selected);
                    Check(panel.PickerItems.SequenceEqual(EquipmentQuery.Sort(fixture.Profile.Items.Where(i => i.Slot == EquipmentSlot.Weapon), EquipmentStat.ColdPower).Select(i => i.Id)), "La interfaz no ordena por el atributo elegido.");
                    Check(((Label)panel.FindChild("EquipmentComparison_" + fixture.Extras[1].Id, true, false)).Text.Contains("4/4 → 3/4"), "Falta el cambio de conjunto en la comparación.");
                    Button("EquipmentBulkToggle").EmitSignal(BaseButton.SignalName.Pressed);
                    Button("EquipmentBulkAll").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(Button("EquipmentBulkSell").Text.Contains("Vender 2"), "Marcar visibles incluye favoritas o equipadas.");
                    _usabilityStage++; return false;
                case 1:
                    CaptureUsability("01-favoritos-y-comparacion");
                    Button("EquipmentBulkSell").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(dialog.Visible && dialog.GetOkButton().Text.Contains("Vender 2"), "La venta múltiple no confirma la cantidad.");
                    _usabilityStage++; return false;
                case 2:
                    CaptureUsability("02-venta-multiple");
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); _usabilityStage++; return false;
                case 3:
                    Check(fixture.Sold == 1 && fixture.Attempts == 2 && panel.VisibleItems == 6 && panel.PreviewProfile!.Gold == fixture.Profile.Gold,
                        "Un fallo parcial pierde objetos, oro o duplica ventas.");
                    teams.Select(1); teams.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                    Check(Button("EquipmentApplyTeam").Disabled, "Se permite aplicar un equipo con una pieza vendida.");
                    fixture.FailSecond = false;
                    Button("EquipmentBulkSell").EmitSignal(BaseButton.SignalName.Pressed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                    _usabilityStage++; return false;
                case 4:
                    Check(fixture.Sold == 2 && fixture.Attempts == 3 && panel.VisibleItems == 5 && panel.PreviewProfile!.Items.Any(i => i.Id == fixture.Extras[0].Id),
                        "Reintentar no conserva la favorita o repite una venta confirmada.");
                    Button("EquipmentFavorite_" + fixture.Extras[0].Id).EmitSignal(BaseButton.SignalName.Pressed);
                    Button("EquipmentBulkAll").EmitSignal(BaseButton.SignalName.Pressed);
                    fixture.LoseNextResponse = true;
                    Button("EquipmentBulkSell").EmitSignal(BaseButton.SignalName.Pressed); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                    _usabilityStage = 8; return false;
                case 5:
                    var tools = (Control)_controlsSettings!.FindChild("TouchEditorToolbar", true, false);
                    Check(GetViewport().GetVisibleRect().Encloses(tools.GetGlobalRect()), "Las herramientas del editor salen de la pantalla: " + tools.GetGlobalRect());
                    var control = (Control)_controlsSettings!.FindChild("TouchSelf", true, false);
                    _controlBefore = control.GetGlobalRect().GetCenter();
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = _controlBefore, Pressed = true });
                    Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = _controlBefore + new Vector2(140, 40), Relative = new(140, 40) });
                    Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = _controlBefore + new Vector2(140, 40), Pressed = false });
                    var editing = (MobileControls)_controlsSettings.FindChild("MobileControls", true, false);
                    foreach (var center in editing.StickCentersForVerification)
                    {
                        Input.ParseInputEvent(new InputEventScreenTouch { Index = 1, Position = center, Pressed = true });
                        Input.ParseInputEvent(new InputEventScreenDrag { Index = 1, Position = center + new Vector2(0, -30), Relative = new(0, -30) });
                        Input.ParseInputEvent(new InputEventScreenTouch { Index = 1, Position = center + new Vector2(0, -30), Pressed = false });
                    }
                    _usabilityStage++; return false;
                case 6:
                    var moved = (Control)_controlsSettings!.FindChild("TouchSelf", true, false);
                    Check(moved.GetGlobalRect().GetCenter().DistanceTo(_controlBefore) > 40, "El gesto no mueve el control del editor.");
                    Check(_controlsSettings.Configuration.Positions.ContainsKey("TouchSelf"), "No se conserva la posición normalizada.");
                    Check(GetViewport().GetVisibleRect().Encloses(moved.GetGlobalRect()), "El editor deja un botón fuera de pantalla.");
                    var preview = (MobileControls)_controlsSettings.FindChild("MobileControls", true, false);
                    VerifyTouchClearance(preview);
                    VerifyTouchLayoutVariants(preview);
                    Check(preview.Settings.Positions.ContainsKey("Move") && preview.Settings.Positions.ContainsKey("Aim") && preview.Movement == Vector2.Zero,
                        "El editor no mueve ambos joysticks o genera movimiento de combate: " + string.Join(",", preview.Settings.Positions.Keys) + " / " + preview.Movement + " / " + ((Control)_controlsSettings.FindChild("TouchEditorToolbar", true, false)).GetGlobalRect());
                    CaptureUsability("03-editor-tactil");
                    ((Button)_controlsSettings.FindChild("TouchEditorDone", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    _controlsSettings.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Guardar").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(!ControlsOpen, "Guardar no cierra los ajustes.");
                    var config = DevicePreferences.Read<TouchSettings>("touch-controls")!;
                    Check(config.LeftHanded && config.Positions.ContainsKey("TouchSelf") && Math.Abs(config.Size - 1.15f) < .01 && Math.Abs(config.DeadZone - .2f) < .01,
                        "Los ajustes táctiles no se guardan en disco.");
                    OpenTouchSettings(); _usabilityStage++; return false;
                case 7:
                    ((HSlider)_controlsSettings!.FindChild("TouchSize", true, false)).Value = .8;
                    _controlsSettings.HandleBack();
                    Check(DevicePreferences.Read<TouchSettings>("touch-controls")!.Size > 1, "Cancelar guarda cambios.");
                    VerifyCustomTouchRouting();
                    _usabilityWait = Time.GetTicksMsec() + 400; _usabilityStage = 99; return false;
                case 8:
                    Check(fixture.Sold == 3 && fixture.Attempts == 4 && panel.VisibleItems == 4 && panel.PreviewProfile!.Gold == fixture.Profile.Gold && !Accounts.InventoryNeedsRefresh,
                        "Perder una respuesta de venta no recupera el inventario real o vuelve a enviar la escritura.");
                    var boots = fixture.Profile.Items.Single(i => i.Slot == EquipmentSlot.Boots);
                    panel.PressInspect(EquipmentSlot.Boots);
                    fixture.LoseNextResponse = true; fixture.ReadsOffline = true;
                    panel.PressTrash(boots.Id); dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                    _usabilityStage = 9; return false;
                case 9:
                    Check(Accounts.InventoryNeedsRefresh && fixture.Sold == 4 && fixture.Attempts == 5 && panel.VisibleItems == 4 &&
                        ((Label)panel.FindChild("EquipmentPickerStatus", true, false)).Text.Contains("sin confirmar"), "La pérdida de respuesta sin lectura fiable se muestra como una venta descartada.");
                    var pendingId = panel.PreviewProfile!.Items.Single(i => i.Slot == EquipmentSlot.Boots).Id;
                    Check(Button("EquipmentTrash_" + pendingId).Disabled, "Se puede reenviar una venta sin confirmar.");
                    panel.PressTrash(pendingId);
                    Check(fixture.Attempts == 5 && !dialog.Visible, "Un botón desactivado reenvía la operación pendiente.");
                    fixture.ReadsOffline = false; panel.HandleBack();
                    panel.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Actualizar").EmitSignal(BaseButton.SignalName.Pressed);
                    _usabilityStage = 10; return false;
                case 10:
                    Check(!Accounts.InventoryNeedsRefresh && panel.VisibleItems == 3 && panel.PreviewProfile!.Gold == fixture.Profile.Gold && fixture.Attempts == 5,
                        "Actualizar no resuelve la venta pendiente o repite su cobro.");
                    GD.Print("SALE_RESPONSE_RECOVERY_OK: respuesta perdida, sincronización automática, bloqueo sin lectura y recuperación manual sin reenvíos.");
                    OpenTouchSettings();
                    ((HSlider)_controlsSettings!.FindChild("TouchSize", true, false)).Value = 1.15;
                    ((HSlider)_controlsSettings.FindChild("TouchDeadZone", true, false)).Value = .2;
                    ((HSlider)_controlsSettings.FindChild("TouchSensitivity", true, false)).Value = 1.2;
                    ((CheckBox)_controlsSettings.FindChild("TouchLeftHanded", true, false)).ButtonPressed = true;
                    ((Button)_controlsSettings.FindChild("TouchEditPositions", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
                    _usabilityStage = 5; return false;
                default:
                    if (Time.GetTicksMsec() < _usabilityWait) return false;
                    Check(_engineLog.ErrorCount == 0, "Godot registró errores durante los flujos de usabilidad.");
                    GD.Print("USABILITY_SMOKE_OK: comparación con conjuntos, ordenación, favoritos, persistencia, equipos, venta múltiple con fallo y reintento, editor táctil y cancelación.");
                    _usabilitySmoke = false; GetTree().Quit(); return true;
            }
        }
        catch (Exception error) { CaptureUsability("00-error"); GD.PrintErr("USABILITY_SMOKE_FAILED: " + error); _usabilitySmoke = false; GetTree().Quit(1); return true; }
    }
    private sealed class UsabilityFixture : System.Net.Http.HttpMessageHandler
    {
        private readonly string _challenge = Guid.NewGuid().ToString("N");
        public readonly EquipmentItem[] Extras = Enumerable.Range(0, 3).Select(i => EquipmentCatalogue.Generate(new(120 + i),
            tier: ItemTier.Diamond, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Cold)).ToArray();
        public AccountProfile Profile;
        public int Attempts, Sold;
        public bool FailSecond = true, LoseNextResponse, ReadsOffline;
        public UsabilityFixture()
        {
            var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(111 + (int)s),
                tier: ItemTier.Mythic, slot: s, family: EquipmentFamily.Fire)).ToArray();
            Profile = new("usability-fixture", "Mago", 10000, 0, 0, new(items[0].Id, items[1].Id, items[2].Id, items[3].Id), [.. items, .. Extras]);
        }
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken token)
        {
            object value;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/account/login/start": value = new LoginChallenge(_challenge, new('p', 64), "https://usability.example/account/login/" + _challenge + "?key=" + new string('k', 64), DateTimeOffset.UtcNow.AddMinutes(2)); break;
                case "/account/login/poll": value = new LoginPoll(true, new('s', 64), DateTimeOffset.UtcNow.AddHours(1), Profile); break;
                case "/account/profile":
                    if (ReadsOffline) throw new System.Net.Http.HttpRequestException("Lectura sin conexión simulada.");
                    value = Profile; break;
                case "/account/sell":
                    Attempts++;
                    if (FailSecond && Attempts == 2) return Json(new ApiError("Fallo de conexión simulado."), HttpStatusCode.ServiceUnavailable);
                    var sale = Protocol.Deserialize<InventoryRequest>(await request.Content!.ReadAsStringAsync(token))!;
                    Profile = AccountInventory.Sell(Profile, sale.ItemId); Sold++;
                    if (LoseNextResponse) { LoseNextResponse = false; throw new System.Net.Http.HttpRequestException("Respuesta perdida tras vender."); }
                    value = new InventoryResult(Profile, "Vendido."); break;
                default: throw new InvalidOperationException("Operación inesperada: " + request.RequestUri.AbsolutePath);
            }
            return Json(value);
        }
        private static System.Net.Http.HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new System.Net.Http.StringContent(Protocol.Serialize(value)) };
    }
}
