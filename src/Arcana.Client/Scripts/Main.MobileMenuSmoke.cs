using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Prueba de geometría real del menú móvil. No toca servidores ni servicios externos.</summary>
public partial class Main
{
    private bool _mobileMenuSmoke;
    private int _mobileMenuSmokeFrame;
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
        int frame = ++_mobileMenuSmokeFrame;
        try
        {
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
                var tableRect = _smokeScoreHud!.ScoreboardRectForSmoke;
                if (!GetViewport().GetVisibleRect().Encloses(tableRect))
                    throw new InvalidOperationException("Clasificación fuera de la pantalla: " + tableRect +
                        " pantalla=" + GetViewport().GetVisibleRect());
                GD.Print("MOBILE_SCOREBOARD_LAYOUT_OK");
                _smokeScoreHud.QueueFree();
                _smokeScoreHud = null;
                // La etiqueta interna «Opción A» no debe mostrarse al jugador.
                var visibleLabels = _launcher.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Where(label => label.IsVisibleInTree()).Select(label => label.Text);
                if (visibleLabels.Any(text => text.Contains("O P C I Ó N", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("OPCIÓN A", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("INICIO: se muestra una etiqueta interna de diseño.");
            }
            if (frame == 20)
            {
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
