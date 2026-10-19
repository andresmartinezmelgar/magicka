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
    private void ClickItemWithPointer(EquipmentPanel panel, string buttonName, int clicks, bool backAfter = false)
        => _itemGesture = SendItemPointerClicks(panel, buttonName, clicks, backAfter);

    private async Task SendItemPointerClicks(EquipmentPanel panel, string buttonName, int clicks, bool backAfter)
    {
        await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        var button = (Button)panel.FindChild(buttonName, true, false);
        // Llevar la pieza al área visible antes de simular el gesto real.
        // El selector puede ocupar una sola columna y compartir las barras del menú.
        ScrollContainer? container = null;
        for (Node? parent = button.GetParent(); parent != null; parent = parent.GetParent())
            if (parent is ScrollContainer scroll) { container = scroll; break; }
        container?.EnsureControlVisible(button);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var point = button.GetGlobalRect().GetCenter();
        if (container != null && !container.GetGlobalRect().Grow(1).Encloses(button.GetGlobalRect()))
            throw new InvalidOperationException("El botón sigue fuera del área desplazable: " + buttonName);
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
            if (backAfter && i == clicks - 1)
            {
                // Mismo orden que un usuario que pulsa Atrás justo tras el clic.
                // No esperar seis fotogramas: en un runner lento ya vencería la confirmación.
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = false });
            }
            await ToSignal(GetTree().CreateTimer(0.045), SceneTreeTimer.SignalName.Timeout);
        }
        GD.Print($"ITEM_POINTER_OK: {(Ui.IsMobile ? "tacto" : "ratón")} · {clicks} · {buttonName}");
    }
}
