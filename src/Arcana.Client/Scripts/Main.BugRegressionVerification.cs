using Arcana.Core;
using Godot;
using System.Net;
using System.Text.Json;

namespace Arcana.Client;

public partial class Main
{
    private async Task RunBugRegressionSmoke(string[] args)
    {
        async Task Frames(int count = 5) { for (int n = 0; n < count; n++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        async Task Click(Button button)
        {
            var point = button.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = true });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Position = point, GlobalPosition = point, Pressed = false });
            await Frames();
        }
        void CheckText(Control root)
        {
            float right = GetViewport().GetVisibleRect().End.X;
            foreach (var label in root.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>())
            {
                if (!label.IsVisibleInTree() || label.Text.Length < 16 || label.AutowrapMode == TextServer.AutowrapMode.Off) continue;
                Check(label.Size.X >= 120 && label.GlobalPosition.X >= 0 && label.GetGlobalRect().End.X <= right + 1,
                    $"Texto PvP sin ancho legible: {label.Text}: {label.GetGlobalRect()}");
            }
        }
        string? captureDirectory = args.FirstOrDefault(a => a.StartsWith("--regression-captures="))?["--regression-captures=".Length..];
        void Capture(string stage)
        {
            if (captureDirectory == null || DisplayServer.GetName() == "headless") return;
            Directory.CreateDirectory(captureDirectory);
            var image = GetViewport().GetTexture().GetImage();
            Check(!image.IsEmpty() && image.SavePng(Path.Combine(captureDirectory, stage + ".png")) == Error.Ok, "No se pudo capturar " + stage);
        }
        try
        {
            await Frames();
            foreach (int page in new[] { 0, 2 })
            {
                _launcher.OpenMobileSmokePage(page); OpenAccountProfile(); await Frames();
                if (Ui.IsMobile) _Notification((int)NotificationWMGoBackRequest);
                else _UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
                await Frames();
                Check(_accountProfilePanel == null && _launcher.PageForVerification == page, "Atrás de Perfil modifica el menú de origen o no cierra Perfil.");
            }
            GD.Print("PROFILE_BACK_REGRESSION_OK");
            string oauthDirectory = Path.Combine(OS.GetUserDataDir(), "invalid-oauth-regression"); Directory.CreateDirectory(oauthDirectory);
            string oauthFile = Path.Combine(oauthDirectory, "google-desktop-oauth.json"); File.WriteAllText(oauthFile, "{\"installed\":{\"client_id\":123}}");
            using (var desktop = new FirebaseDesktopBridge(oauthDirectory))
                Check(!desktop.TryImportConfiguration(oauthFile, out string error) && error.Length > 0, "Se importó una configuración OAuth inválida.");
            GD.Print("OAUTH_STARTUP_REGRESSION_OK");
            foreach (var size in new[] { new Vector2I(640, 450), new(720, 540), new(1024, 640), new(1440, 900) })
            {
                GetWindow().ContentScaleSize = size; GetWindow().Size = size; await Frames();
                NavigateOptionAMenu(1); await Frames(); CheckText(_launcher);
                var lobby = new RoomLobby(); _ui.AddChild(lobby);
                lobby.Build(this, "127.0.0.1:1", "", "Nombre de mago para probar", 0, WizardType.Balanced);
                lobby.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Crear sala").EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(); CheckText(lobby);
                var lobbyScroll = (ScrollContainer)lobby.FindChild("OptionALobbyContent", true, false);
                Check(lobbyScroll.Size.Y >= 100, "Las acciones de PvP no dejan espacio al formulario.");
                Capture($"{size.X}-pvp-crear");
                var fields = (GridContainer)lobby.FindChild("RoomCreationFields", true, false);
                Check(fields.GetChildren().OfType<VBoxContainer>().All(f => f.Size.X >= 160), "Los campos de creación se comprimen.");
                lobby.QueueFree(); await Frames();
                var world = new GameWorld(new() { Mode = GameMode.Warfare });
                var blue = world.AddPlayer("Nombre largo del equipo azul", 0)!; world.AddPlayer("Nombre largo del equipo rojo", 1);
                var waiting = new RoomWaitingPanel(); _ui.AddChild(waiting); waiting.Build(_ => { }, () => { });
                waiting.Update(world.Snapshot() with { WaitingForPlayers = true, BestOf = 9, LobbyHostId = blue.Id }, blue.Id, true);
                await Frames(); CheckText(waiting); Capture($"{size.X}-pvp-espera");
                Check(GetViewport().GetVisibleRect().Encloses(((Control)waiting.FindChild("WaitingRoomExit", true, false)).GetGlobalRect()), "Salir de la sala queda fuera de pantalla.");
                waiting.QueueFree(); await Frames();
                var item = EquipmentCatalogue.Generate(new(813), slot: EquipmentSlot.Chest);
                Accounts.RestoreOfflineProfile(new("regression-ui", "Mago", 1000, 0, 0, new(), [item]));
                NavigateOptionAMenu(4); await Frames(); var panel = _equipmentPanel!; panel.PressInspect(EquipmentSlot.Chest); await Frames();
                var scroll = (ScrollContainer)panel.FindChild("EquipmentPickerScroll", true, false);
                Check(scroll.Size.Y >= 90, "El selector no deja espacio para desplazarse.");
                Capture($"{size.X}-inventario-selector");
                var home = (Button)panel.FindChild("OptionANav0", true, false); int pressed = 0; home.Pressed += () => pressed++;
                await Click(home);
                Check(pressed == 1 && _equipmentPanel == null && _launcher.PageForVerification == 0, "El selector tapa la navegación inferior.");
            }
            GD.Print("PVP_FLEX_REGRESSION_OK: 640x450, 720x540, 1024x640, 1440x900; etiquetas y campos legibles, espera y navegación con puntero.");
            if (Ui.IsMobile)
            {
                _playing = true; _waitingPanel = new RoomWaitingPanel(); _ui.AddChild(_waitingPanel); _waitingPanel.Build(_ => { }, () => { });
                BuildTouchControls(); UpdateTouchVisibility(); Check(!_touch!.Visible, "Los controles táctiles reciben gestos detrás de la sala de espera.");
                _waitingPanel.Hide(); UpdateTouchVisibility(); Check(_touch.Visible, "Los controles no vuelven al comenzar la partida.");
                _playing = false; _touch.QueueFree(); _touch = null; _waitingPanel.QueueFree(); _waitingPanel = null;
                GD.Print("PVP_WAITING_INPUT_REGRESSION_OK");
            }
            var controls = new MobileControls(); _ui.AddChild(controls); await Frames();
            var touchWorld = new GameWorld(new() { Mode = GameMode.Arena }); var caster = touchWorld.AddPlayer("Táctil")!;
            touchWorld.ApplyMessage(caster.Id, new() { Type = "self", Sequence = caster.LastSequence + 1 });
            controls.ActionRequested += message => { message.Sequence = caster.LastSequence + 1; touchWorld.ApplyMessage(caster.Id, message); };
            controls.Update(touchWorld.Snapshot().Players.Single());
            void Tap(string name, int finger)
            {
                var point = ((Control)controls.FindChild(name, true, false)).GetGlobalRect().GetCenter();
                controls.Touch(finger, point, true); controls.Touch(finger, point, false);
            }
            Tap("TouchElementShield", 2); var aim = controls.StickCentersForVerification[1];
            controls.Touch(3, aim, true); controls.Touch(3, aim, false); Tap("TouchElementEarth", 4);
            for (int n = 0; n < 70; n++) { touchWorld.Step(); controls.Update(touchWorld.Snapshot().Players.Single()); }
            controls.Touch(5, aim, true);
            Check(controls.BarrierAimPending && caster.Charging == null, "El gesto predice Tierra y olvida Escudo tras un lanzamiento rechazado.");
            controls.Touch(5, aim, false);
            Check(touchWorld.Snapshot().Walls.Length == 1, "El gesto confirmado no lanza la barrera esperada.");
            controls.QueueFree(); await Frames(); GD.Print("TOUCH_REJECTED_CAST_REGRESSION_OK");
            Accounts.Dispose(); using var http = new System.Net.Http.HttpClient(new RegressionProfileHandler()); Accounts = new(http);
            var old = new FirebaseAndroidBridge.Session("regression-user", "Mago", "fake-regression-token", DateTimeOffset.UtcNow.AddHours(1));
            await Accounts.SetFirebaseSession("demo-regression", old.Uid, old.Name, old.IdToken, old.ExpiresAt);
            OnGoogleSession(old); await StopGoogleSession();
            _acceptGoogleSessions = true; // Un nuevo intento no rehabilita eventos ya encolados.
            await Frames(20);
            Check(!Accounts.SignedIn && Accounts.Profile == null, "Una señal encolada de la sesión anterior sobrevive a un nuevo intento.");
            await StopGoogleSession(); OnGoogleSession(old); await Frames(20);
            Check(!Accounts.SignedIn && Accounts.Profile == null && !GoogleIdentityKnown, "Una sesión tardía vuelve a conectar tras salir.");
            GD.Print("SESSION_LOGOUT_REGRESSION_OK");
            GD.Print("REGRESSION_SMOKE_OK: sesiones, OAuth, Perfil, PvP flexible y navegación del selector."); GetTree().Quit();
        }
        catch (Exception error) { GD.PrintErr("REGRESSION_SMOKE_FAILED: " + error); GetTree().Quit(1); }
    }
    private sealed class RegressionProfileHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/loadouts/active", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            object body = path.EndsWith("/items", StringComparison.Ordinal) ? new { documents = Array.Empty<object>() }
                : new { fields = new Dictionary<string, object> { ["uid"] = new { stringValue = "regression-user" }, ["name"] = new { stringValue = "Mago" },
                    ["gold"] = new { integerValue = "0" }, ["wins"] = new { integerValue = "0" }, ["losses"] = new { integerValue = "0" },
                    ["itemCount"] = new { integerValue = "0" }, ["schemaVersion"] = new { integerValue = "1" } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body, Protocol.Json)) });
        }
    }
}
