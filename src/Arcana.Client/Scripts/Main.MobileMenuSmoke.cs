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
                if (!buttons.Any(button => button.Text == "Ver salas del servidor LAN" || button.Text == "Ver salas por IP"))
                    throw new InvalidOperationException("PVP: la acción principal apunta a una pestaña oculta o a Alojar.");
            }
            if (frame == 45) _launcher.OpenMobileSmokePage(2);
            if (frame == 55) AssertMobileActions("VERSUS_IA", _launcher.MobilePrimaryButtons, 2);
            if (frame == 65)
            {
                _smokeLobby = new RoomLobby();
                _ui.AddChild(_smokeLobby);
                _smokeLobby.Build(this, "127.0.0.1:27015", "", "Mago", -1, WizardType.Balanced);
            }
            if (frame == 75)
                AssertMobileActions("SALAS_PVP", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 4);
            if (frame == 85) _smokeLobby!.OpenCreation();
            if (frame == 95)
                AssertMobileActions("CREAR_SALA", _smokeLobby!.MobileRoomButtons.Append(_smokeLobby.MobileBackButton!), 3);
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
                string[] labels = _accountProfilePanel.FindChildren("*", "Label", true, false)
                    .OfType<Label>().Select(label => label.Text).ToArray();
                if (!labels.Any(t => t.Contains("Google conectado", StringComparison.Ordinal)) ||
                    !labels.Any(t => t.Contains("403", StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        "El perfil oculta que Google está autenticado cuando Firestore rechaza sus permisos.");
            }
            if (frame < 125) return false;
            _googleIdentityKnown = false;
            GD.Print("MOBILE_MENU_LAYOUT_OK: PvP, versus IA, salas y perfil 403 visibles sin recortes.");
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
