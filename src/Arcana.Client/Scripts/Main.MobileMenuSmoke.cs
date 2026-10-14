using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Prueba de geometría real del menú móvil. No toca servidores ni servicios externos.</summary>
public partial class Main
{
    private bool _mobileMenuSmoke;
    private bool _desktopHeaderSmoke;
    private int _desktopHeaderSmokeFrame;
    private int _mobileMenuSmokeFrame;
    private bool _visualReview;
    private long _lastVisualDraw = -1;
    private string? _visualReviewDirectory;

    private void CaptureVisualReview(string stage)
    {
        if (!_visualReview || DisplayServer.GetName() == "headless") return;
        var screenshot = GetViewport().GetTexture().GetImage();
        if (screenshot.IsEmpty()) throw new InvalidOperationException("Captura gráfica vacía: " + stage);
        if (screenshot.GetWidth() < 640 || screenshot.GetHeight() < 450)
            throw new InvalidOperationException("Captura gráfica incompleta: " + stage);
        // Rechaza fotogramas antiguos del combate usados por error como capturas del menú.
        // En los menús predomina el azul oscuro; el mapa del juego es mayoritariamente verde.
        if (stage.StartsWith("03-", StringComparison.Ordinal) ||
            stage.StartsWith("04-", StringComparison.Ordinal) ||
            stage.StartsWith("05-", StringComparison.Ordinal))
        {
            int green = 0, samples = 0;
            for (int row = 2; row < 18; row++)
                for (int col = 2; col < 18; col++)
                {
                    var pixel = screenshot.GetPixel(col * screenshot.GetWidth() / 20,
                        row * screenshot.GetHeight() / 20);
                    if (pixel.G > pixel.R * 1.3f && pixel.G > pixel.B * 1.3f && pixel.G > 0.12f) green++;
                    samples++;
                }
            if (green > samples / 5)
                throw new InvalidOperationException($"VISUAL_STALE_FRAME: {stage} parece mostrar el combate en vez del menú ({green}/{samples} píxeles verdes).");
        }
        if (!string.IsNullOrWhiteSpace(_visualReviewDirectory))
        {
            DirAccess.MakeDirRecursiveAbsolute(_visualReviewDirectory);
            string file = System.IO.Path.Combine(_visualReviewDirectory, stage + ".png");
            if (screenshot.SavePng(file) != Error.Ok) throw new IOException("No se pudo guardar " + file);
            GD.Print($"VISUAL_CAPTURE_OK:{stage}:{screenshot.GetWidth()}x{screenshot.GetHeight()}:{file}");
        }
        screenshot.Resize(800, 450, Image.Interpolation.Lanczos);
        GD.Print("VISUAL_REVIEW_BASE64:" + stage + ":" + Convert.ToBase64String(screenshot.SaveJpgToBuffer(0.68f)));
    }

    private void AssertHudBounds(Hud hud, string stage)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        foreach (var name in new[] { "MatchBanner", "MatchRewardNotice", "PlayerStatusPanel", "SpellControlsPanel", "MiniMapPanel", "MatchObjective" })
        {
            if (hud.FindChild(name, true, false) is not Control control || !control.IsVisibleInTree()) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
                throw new InvalidOperationException($"HUD {stage}: {name} fuera de pantalla: {rect}, viewport={screen}");
        }
        var status = (Control)hud.FindChild("PlayerStatusPanel", true, false);
        var minimap = (Control)hud.FindChild("MiniMapPanel", true, false);
        if (status.IsVisibleInTree() && minimap.IsVisibleInTree() &&
            status.GetGlobalRect().Intersects(minimap.GetGlobalRect()))
            throw new InvalidOperationException($"HUD {stage}: el minimapa tapa la vida del jugador: {minimap.GetGlobalRect()} / {status.GetGlobalRect()}");
    }

    // En CI no basta con simular los controles móviles: hay que comprobar
    // la geometría Godot del escritorio real en 1600×900 y 1280×720.
    private void PrepareDesktopHeaderSmokeWindow()
    {
        var args = OS.GetCmdlineUserArgs();
        if (!(args.Contains("--desktop-header-smoke") || args.Contains("--visual-desktop")) || Ui.IsMobile) return;
        string? size = args.FirstOrDefault(arg => arg.StartsWith("--smoke-viewport=", StringComparison.Ordinal));
        if (size == null) return;
        var parts = size["--smoke-viewport=".Length..].Split('x');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int width) ||
            !int.TryParse(parts[1], out int height) ||
            width is < 800 or > 2560 || height is < 600 or > 1440)
            throw new ArgumentException("Resolución del smoke de escritorio inválida.");
        GetWindow().ContentScaleSize = new Vector2I(width, height);
        GetWindow().ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        GetWindow().Size = new Vector2I(width, height);
    }

    private static void AssertHeaderAndHome(Launcher launcher, Rect2 screen, string mode)
    {
        if (launcher.FindChild("OptionAHeader", true, false) is not Control header ||
            launcher.FindChild("OptionAAccountSummary", true, false) is not Label summary ||
            launcher.FindChild("OptionAHomeCards", true, false) is not GridContainer home)
            throw new InvalidOperationException($"{mode}: faltan cabecera, resumen o tarjetas.");
        Rect2 head = header.GetGlobalRect();
        Rect2 account = summary.GetGlobalRect();
        Rect2 cards = home.GetGlobalRect();
        var buttons = home.GetChildren().OfType<Button>().ToArray();
        if (head.Size.Y is < 55 or > 80 || head.Position.Y < -1 || !screen.Encloses(head))
            throw new InvalidOperationException($"{mode}: la cabecera invade la pantalla: {head}");
        if (!summary.IsVisibleInTree() || summary.AutowrapMode != TextServer.AutowrapMode.Off ||
            account.Size.X < 140 || account.Size.Y > 56 || account.Position.Y < head.Position.Y ||
            account.End.Y > head.End.Y || account.End.X > screen.End.X)
            throw new InvalidOperationException($"{mode}: perfil inválido: rect={account}, header={head}, visible={summary.IsVisibleInTree()}, wrap={summary.AutowrapMode}, screen={screen}");
        if (home.Columns != 2 || buttons.Length != 4 ||
            cards.Position.Y < head.End.Y || cards.Position.X < 0 ||
            cards.End.X > screen.End.X || cards.Position.Y > screen.End.Y - 80)
            throw new InvalidOperationException($"{mode}: tarjetas 2×2 fuera del área central: {cards}, header={head}");
        foreach (var button in buttons)
        {
            var rect = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || rect.Size.X < 120 || rect.Size.Y < 90 ||
                rect.Position.Y < head.End.Y || rect.Position.X < 0 || rect.End.X > screen.End.X ||
                rect.Position.Y > screen.End.Y - 65)
                throw new InvalidOperationException($"{mode}: tarjeta del inicio fuera de pantalla: {button.Text}, {rect}");
        }
        GD.Print($"HEADER_AND_HOME_OK:{mode}: header={head}, summary={account}, cards={cards}");
    }

    private bool CheckDesktopHeaderSmoke()
    {
        if (!_desktopHeaderSmoke) return false;
        int frame = ++_desktopHeaderSmokeFrame;
        try
        {
            if (frame == 2)
            {
                _googleIdentityKnown = true;
                _googleIdentityName = "andresmartinez";
                _launcher.UpdateAccountStatus();
            }
            if (frame == 20)
            {
                AssertHeaderAndHome(_launcher, GetViewport().GetVisibleRect(), "DESKTOP");
                GD.Print("DESKTOP_HEADER_LAYOUT_OK");
                _desktopHeaderSmoke = false;
                GetTree().Quit(0);
            }
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("DESKTOP_HEADER_LAYOUT_FAILED: " + error);
            _desktopHeaderSmoke = false;
            GetTree().Quit(1);
            return true;
        }
    }
    private RoomLobby? _smokeLobby;
    private Hud? _smokeScoreHud;

    private void AssertMobileActions(string stage, IEnumerable<Button> buttons, int minimumCount)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        Button[] visible = buttons.ToArray();
        if (visible.Length < minimumCount)
            throw new InvalidOperationException($"{stage}: faltan botones ({visible.Length}/{minimumCount}).");
        foreach (var button in visible)
        {
            var bounds = button.GetGlobalRect();
            if (!button.IsVisibleInTree() || bounds.Size.Y < 60 || bounds.Size.X < 95 ||
                !screen.Encloses(bounds))
                throw new InvalidOperationException(
                    $"{stage}: botón fuera de pantalla o demasiado pequeño: {button.Text} rect={bounds} pantalla={screen}.");
            GD.Print($"MOBILE_BUTTON_OK:{stage}: {button.Text} {bounds}");
        }
    }

    private void AssertOptionAScreen(string stage, Control root)
    {
        Rect2 screen = GetViewport().GetVisibleRect();
        if (root.FindChild("OptionAHeader", true, false) is not Control ||
            root.FindChild("OptionAFooter", true, false) is not Control)
            throw new InvalidOperationException($"{stage}: faltan barras de navegación de la opción A.");
        foreach (var control in root.FindChildren("*", "Control", true, false).OfType<Control>())
        {
            if (!control.IsVisibleInTree()) continue;
            if (control is ScrollContainer sc && sc.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled)
                throw new InvalidOperationException($"{stage}: el panel {sc.Name} permite desbordamiento horizontal.");
            if (control is not BaseButton && control is not LineEdit &&
                control is not SpinBox && control is not ItemList) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
            {
                // El contenido puede desplazarse verticalmente, pero nunca
                // desbordarse horizontalmente. Las acciones fijas se prueban
                // por separado y siempre deben quedar dentro de pantalla.
                bool inVerticalScroll = false;
                for (Node? parent = control.GetParent(); parent != null; parent = parent.GetParent())
                    if (parent is ScrollContainer container && container.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
                    { inVerticalScroll = true; break; }
                if (inVerticalScroll && rect.Position.X >= screen.Position.X &&
                    rect.End.X <= screen.End.X) continue;
                throw new InvalidOperationException($"{stage}: control fuera de pantalla: {control.Name} {rect} / {screen}");
            }
        }
        GD.Print($"OPTION_A_SCREEN_OK:{stage}");
    }

    private bool CheckMobileMenuSmoke()
    {
        if (!_mobileMenuSmoke) return false;
        if (_visualReview)
        {
            // Xvfb+OpenGL por software puede dibujar menos fotogramas que _Process.
            // El escenario avanza exclusivamente tras un FramePostDraw completado.
            long drawn = Interlocked.Read(ref _drawnFrames);
            if (drawn <= _lastVisualDraw) return true;
            _lastVisualDraw = drawn;
        }
        int frame = ++_mobileMenuSmokeFrame;
        try
        {
            string? scene = frame switch
            {
                14 => "01-clasificacion",
                16 => "02-victoria-3-de-5",
                21 => "03-inicio",
                36 => "04-pvp",
                56 => "05-versus-ia",
                76 => "06-lista-salas",
                96 => "07-crear-sala",
                116 => "08-perfil",
                137 => "09-ayuda",
                156 => "10-equipacion",
                _ => null
            };
            if (scene != null) CaptureVisualReview(scene);
            if (frame == 10 && _visualReview) _launcher.Modulate = new Color(1, 1, 1, 0);
            if (frame == 9)
            {
                _smokeScoreHud = new Hud();
                _ui.AddChild(_smokeScoreHud);
                _smokeScoreHud.Build();
                var simulated = new GameWorld(new WorldOptions { Mode = GameMode.Warfare, Bots = 0 });
                var player = simulated.AddPlayer("Mago de prueba", 0)!;
                _smokeScoreHud.ScoreOpen = true;
                _smokeScoreHud.Update(simulated.Snapshot(), player.Id, "Servidor de prueba", 0.016);
            }
            if (frame == 15)
            {
                // Nombre similar al que provocó el error real en Windows.
                _googleIdentityKnown = true;
                _googleIdentityName = "andresmartinez";
                _launcher.UpdateAccountStatus();
                var tableRect = _smokeScoreHud!.ScoreboardRectForSmoke;
                if (!GetViewport().GetVisibleRect().Encloses(tableRect))
                    throw new InvalidOperationException("Clasificación fuera de la pantalla: " + tableRect +
                        " pantalla=" + GetViewport().GetVisibleRect());
                GD.Print("MOBILE_SCOREBOARD_LAYOUT_OK");
                AssertHudBounds(_smokeScoreHud, "clasificacion");
                _smokeScoreHud.ScoreOpen = false;
                var simulated = new GameWorld(new WorldOptions { Mode = GameMode.Warfare, Bots = 0 });
                var player = simulated.AddPlayer("Mago de prueba", 0)!;
                var won = simulated.Snapshot() with
                {
                    BestOf = 5, SeriesWins = [3, 2], Winner = 0, MatchEnded = true,
                    RestartIn = 8, RewardStatus = "Recompensa pendiente de confirmar en Firebase."
                };
                _smokeScoreHud.Update(won, player.Id, "Servidor LAN", 0.016);
                Rect2 screen = GetViewport().GetVisibleRect();
                var banner = (Label)_smokeScoreHud.FindChild("MatchBanner", true, false);
                var reward = (Label)_smokeScoreHud.FindChild("MatchRewardNotice", true, false);
                if (!banner.Text.Contains("HAS GANADO LA SERIE", StringComparison.Ordinal) ||
                    !reward.Text.Contains("pendiente", StringComparison.OrdinalIgnoreCase) ||
                    !screen.Encloses(banner.GetGlobalRect()) || !screen.Encloses(reward.GetGlobalRect()))
                    throw new InvalidOperationException($"Resultado/recompensa fuera de pantalla: banner={banner.Text} rect={banner.GetGlobalRect()}, reward={reward.Text} rect={reward.GetGlobalRect()}, viewport={screen}.");
                GD.Print("MOBILE_SERIES_REWARD_LAYOUT_OK");
                AssertHudBounds(_smokeScoreHud, "victoria");
                // La etiqueta interna «Opción A» no debe mostrarse al jugador.
                var visibleLabels = _launcher.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Where(label => label.IsVisibleInTree()).Select(label => label.Text);
                if (visibleLabels.Any(text => text.Contains("O P C I Ó N", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("OPCIÓN A", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("INICIO: se muestra una etiqueta interna de diseño.");
            }
            if (frame == 17)
            {
                _smokeScoreHud?.QueueFree();
                _smokeScoreHud = null;
                _launcher.Modulate = new Color(1, 1, 1, 1);
            }
            if (frame == 20)
            {
                AssertHeaderAndHome(_launcher, GetViewport().GetVisibleRect(), "MOBILE");
                var fullPanel = _launcher.FindChild("OptionAFullWidthPanel", true, false) as Control;
                var choices = _launcher.FindChild("OptionAHomeCards", true, false) as Control;
                if (fullPanel == null || choices == null ||
                    fullPanel.Size.X < GetViewport().GetVisibleRect().Size.X * 0.75f ||
                    choices.Size.X < GetViewport().GetVisibleRect().Size.X * 0.65f)
                    throw new InvalidOperationException("INICIO: las cuatro tarjetas han quedado encogidas.");
                if (choices is not GridContainer home || home.Columns != 2)
                    throw new InvalidOperationException("INICIO: las cuatro tarjetas deben organizarse 2×2.");
                var cards = choices.GetChildren().OfType<Button>().ToArray();
                if (cards.Length != 4)
                    throw new InvalidOperationException("INICIO: faltan tarjetas principales.");
                foreach (Button card in cards)
                    if (card.Size.X < 170 || card.GetGlobalRect().Position.X < 0 ||
                        card.GetGlobalRect().End.X > GetViewport().GetVisibleRect().End.X)
                        throw new InvalidOperationException("INICIO: tarjeta demasiado estrecha o desbordada: " + card.Text);
                if (_launcher.FindChild("OptionAAccountSummary", true, false) is not Label accountHeader ||
                    !accountHeader.IsVisibleInTree() || !accountHeader.Text.Contains("V:") ||
                    !accountHeader.Text.Contains("D:"))
                    throw new InvalidOperationException("INICIO: faltan nombre, Google o resultados en la esquina superior derecha.");
                if (_launcher.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Any(label => label.IsVisibleInTree() && label.Text.Contains("victorias ·") &&
                        label.Name != "OptionAAccountSummary"))
                    throw new InvalidOperationException("INICIO: se muestra el resumen de cuenta dentro del menú.");
            }
            if (frame == 22)
            {
                _googleIdentityKnown = false;
                _googleIdentityName = "";
                _launcher.UpdateAccountStatus();
                // Verificación de la salida visible en sala de espera sin un servidor real.
                bool left = false;
                var wait = new RoomWaitingPanel();
                _ui.AddChild(wait);
                wait.Build(_ => { }, () => left = true);
                var exit = wait.FindChild("WaitingRoomExit", true, false) as Button;
                if (exit == null || !exit.IsVisibleInTree() || exit.Text != "Salir de la sala")
                    throw new InvalidOperationException("SALA DE ESPERA: no hay botón Salir visible.");
                exit.EmitSignal(Button.SignalName.Pressed);
                if (!left) throw new InvalidOperationException("SALA DE ESPERA: el botón Salir no responde.");
                wait.QueueFree();
            }
            if (frame == 25) _launcher.OpenMobileSmokePage(1);
            if (frame == 35)
            {
                var buttons = _launcher.MobilePrimaryButtons;
                AssertMobileActions("PVP", buttons, 2);
                AssertOptionAScreen("PVP", _launcher);
                if (_launcher.FindChild("OnlineFirebaseButton", true, false) is not Button online ||
                    !online.IsVisibleInTree() || online.Disabled)
                    throw new InvalidOperationException("PVP: falta el acceso al servidor online por Firebase.");
                if (!buttons.Any(button => button.Text == "Ver salas del servidor LAN" || button.Text == "Ver salas por IP"))
                    throw new InvalidOperationException("PVP: la acción principal apunta a una pestaña oculta o a Alojar.");
            }
            if (frame == 45) _launcher.OpenMobileSmokePage(2);
            if (frame == 55)
            {
                foreach (string nodeName in new[] { "OptionAFullWidthPanel", "OptionASettingsColumns",
                    "OptionAPrimaryActions", "LauncherSettingsScroll", "OptionAHomeCards", "OptionAIaSettingsColumns" })
                {
                    if (_launcher.FindChild(nodeName, true, false) is Control part)
                        GD.Print($"RESPONSIVE_DEBUG:{nodeName}: minimum={part.GetCombinedMinimumSize()} size={part.Size} rect={part.GetGlobalRect()}");
                }
                foreach (TabContainer tab in _launcher.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>())
                    GD.Print($"RESPONSIVE_DEBUG:TABS: minimum={tab.GetCombinedMinimumSize()} size={tab.Size} rect={tab.GetGlobalRect()}");
                AssertMobileActions("VERSUS_IA", _launcher.MobilePrimaryButtons, 2);
                AssertOptionAScreen("VERSUS_IA", _launcher);
            }
            if (frame == 65)
            {
                _smokeLobby = new RoomLobby();
                _ui.AddChild(_smokeLobby);
                _smokeLobby.Build(this, "127.0.0.1:27015", "", "Mago", -1, WizardType.Balanced);
            }
            if (frame == 75)
            {
                AssertMobileActions("SALAS_PVP", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 4);
                AssertOptionAScreen("SALAS_PVP", _smokeLobby);
                _smokeLobby!.SetBusy(true);
                _smokeLobby.ShowError("Contraseña de la sala incorrecta.");
                if (_smokeLobby.IsBusyForSmoke || _smokeLobby.StatusForSmoke != "Contraseña de la sala incorrecta.")
                    throw new InvalidOperationException("SALAS_PVP: un error de clave no permite reintentar o no aparece en pantalla.");
            }
            if (frame == 85) _smokeLobby!.OpenCreation();
            if (frame == 95)
            {
                AssertMobileActions("CREAR_SALA", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 3);
                AssertOptionAScreen("CREAR_SALA", _smokeLobby);
            }
            if (frame == 105)
            {
                _smokeLobby!.QueueFree();
                _googleIdentityKnown = true; // Simula Google autenticado sin tocar cuentas reales.
                _googleIdentityName = "Mago de pruebas";
                OpenAccountProfile();
                _accountProfilePanel!.UpdateGoogleStatus(
                    "Error al leer Firestore: Firestore (403 PERMISSION_DENIED) ha denegado la lectura de users/{tu cuenta}.");
            }
            if (frame == 115)
            {
                AssertMobileActions("PERFIL_403", _accountProfilePanel!.VisibleFooterButtons, 1);
                AssertOptionAScreen("PERFIL_403", _accountProfilePanel);
                string[] labels = _accountProfilePanel.FindChildren("*", "Label", true, false)
                    .OfType<Label>().Select(label => label.Text).ToArray();
                if (!labels.Any(t => t.Contains("Google conectado", StringComparison.Ordinal)) ||
                    !labels.Any(t => t.Contains("403", StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "El perfil oculta que Google está autenticado cuando Firestore rechaza sus permisos.");
            }
            if (frame == 125)
            {
                _accountProfilePanel!.QueueFree();
                _accountProfilePanel = null;
                ToggleHelp();
            }
            if (frame == 135)
                AssertOptionAScreen("AYUDA", _optionAHelp!);
            if (frame == 135 && _optionAHelp!.ContentWidthFraction < 0.78f)
                throw new InvalidOperationException("AYUDA: las tarjetas no ocupan el ancho disponible.");
            if (frame == 136) _optionAHelp!.SelectForSmoke(2);
            if (frame == 138) AssertOptionAScreen("AYUDA_MAGOS", _optionAHelp!);
            if (frame == 139) _optionAHelp!.SelectForSmoke(3);
            if (frame == 141) AssertOptionAScreen("AYUDA_ELEMENTOS", _optionAHelp!);
            if (frame == 142) _optionAHelp!.SelectForSmoke(4);
            if (frame == 144) AssertOptionAScreen("AYUDA_RELIQUIAS", _optionAHelp!);
            if (frame == 145)
            {
                CloseOptionAHelp();
                OpenEquipment("automatic");
            }
            if (frame == 155)
                AssertOptionAScreen("EQUIPACION", _equipmentPanel!);
            if (frame < 165) return false;
            _googleIdentityKnown = false;
            GD.Print("MOBILE_MENU_LAYOUT_OK: Opción A PvP, IA, salas, perfil, ayuda y equipación; todos sin scroll.");
            _mobileMenuSmoke = false;
            GetTree().Quit(0);
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("MOBILE_MENU_LAYOUT_FAILED: " + error);
            _mobileMenuSmoke = false;
            GetTree().Quit(1);
            return true;
        }
    }
}
