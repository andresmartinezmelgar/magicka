using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>Prueba de geometría real del menú móvil. No toca servidores ni servicios externos.</summary>
public partial class Main
{
    private bool _mobileMenuSmoke;
    private int _mobileMenuSmokeFrame;
    private RoomLobby? _smokeLobby;

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
            if (control is ScrollContainer sc &&
                (sc.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled ||
                 sc.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled))
                throw new InvalidOperationException($"{stage}: scroll no permitido en {sc.Name}.");
            if (control is not BaseButton && control is not LineEdit &&
                control is not SpinBox && control is not ItemList) continue;
            var rect = control.GetGlobalRect();
            if (!screen.Encloses(rect))
                throw new InvalidOperationException($"{stage}: control fuera de pantalla: {control.Name} {rect} / {screen}");
        }
        GD.Print($"OPTION_A_SCREEN_OK:{stage}");
    }

    private bool CheckMobileMenuSmoke()
    {
        if (!_mobileMenuSmoke) return false;
        int frame = ++_mobileMenuSmokeFrame;
        try
        {
            if (frame == 25) _launcher.OpenMobileSmokePage(1);
            if (frame == 35)
            {
                var buttons = _launcher.MobilePrimaryButtons;
                AssertMobileActions("PVP", buttons, 2);
                AssertOptionAScreen("PVP", _launcher);
                if (!buttons.Any(button => button.Text == "Ver salas del servidor LAN" || button.Text == "Ver salas por IP"))
                    throw new InvalidOperationException("PVP: la acción principal apunta a una pestaña oculta o a Alojar.");
            }
            if (frame == 45) _launcher.OpenMobileSmokePage(2);
            if (frame == 55)
            {
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
                AssertMobileActions("PERFIL_403", _accountProfilePanel!.VisibleFooterButtons, 3);
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
