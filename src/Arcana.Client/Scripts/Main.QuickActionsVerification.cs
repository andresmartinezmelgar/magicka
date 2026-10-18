using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _quickItemActions;
    private Task? _itemGesture;

    private bool ItemGestureRunning()
    {
        if (_itemGesture == null) return false;
        if (!_itemGesture.IsCompleted) return true;
        _itemGesture.GetAwaiter().GetResult();
        return false;
    }
    private void ClickItemWithPointer(EquipmentPanel panel, string buttonName, int clicks)
        => _itemGesture = SendItemPointerClicks(panel, buttonName, clicks);

    private async Task SendItemPointerClicks(EquipmentPanel panel, string buttonName, int clicks)
    {
        await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        var button = (Button)panel.FindChild(buttonName, true, false);
        var point = button.GetGlobalRect().GetCenter();
        if (button.Disabled || !button.IsVisibleInTree() || button.Size.X < 40 || button.Size.Y < 40 || !GetViewport().GetVisibleRect().HasPoint(point))
            throw new InvalidOperationException("No se puede pulsar el botón de la pieza: " + buttonName);
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point });
        for (int i = 0; i < clicks; i++)
        {
            if (Ui.IsMobile) Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = point, Pressed = true });
            else Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point,
                ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = i > 0 });
            await ToSignal(GetTree().CreateTimer(0.025), SceneTreeTimer.SignalName.Timeout);
            if (Ui.IsMobile) Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Position = point, Pressed = false });
            else Input.ParseInputEvent(new InputEventMouseButton { Position = point, GlobalPosition = point,
                ButtonIndex = MouseButton.Left, Pressed = false });
            await ToSignal(GetTree().CreateTimer(0.045), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print($"ITEM_POINTER_OK: {(Ui.IsMobile ? "tacto" : "ratón")} · {clicks} · {buttonName}");
    }
}
