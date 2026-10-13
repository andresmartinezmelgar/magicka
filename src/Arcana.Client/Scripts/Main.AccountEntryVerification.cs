using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _accountEntrySmoke;
    private int _accountEntryFrame;
    private Vector2 _accountEntryPoint;

    private bool CheckAccountEntrySmoke()
    {
        if (!_accountEntrySmoke) return false;
        int frame = ++_accountEntryFrame;
        if (frame == 30)
        {
            var button = _launcher.FindChildren("*", "Button", true, false).OfType<Button>()
                .Single(b => b.Text == "Entrar o registrarse con Google");
            _accountEntryPoint = button.GetGlobalRect().GetCenter();
            GD.Print("ACCOUNT_ENTRY_BUTTON: " + button.GetGlobalRect() + " viewport=" + GetViewport().GetVisibleRect());
            GetViewport().PushInput(new InputEventMouseMotion { Position = _accountEntryPoint, GlobalPosition = _accountEntryPoint });
            Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = _accountEntryPoint });
        }
        if (frame == 31)
            Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = _accountEntryPoint });
        if (frame < 40) return false;
        bool opened = GodotObject.IsInstanceValid(_equipmentPanel) && _equipmentPanel!.IsVisibleInTree();
        if (opened)
        {
            OpenEquipment("127.0.0.1:27015", googleLogin: true);
            opened = !_equipmentPanel!.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Single().Visible;
        }
        if (opened) GD.Print("ACCOUNT_ENTRY_SMOKE_OK: toque Google abre acceso y localhost móvil utiliza descubrimiento automático.");
        else GD.PushError("ACCOUNT_ENTRY_SMOKE_FAILED: Google no abre la pantalla; control bajo el cursor=" + GetViewport().GuiGetHoveredControl()?.GetPath());
        _accountEntrySmoke = false; GetTree().Quit(opened ? 0 : 1); return true;
    }
}
