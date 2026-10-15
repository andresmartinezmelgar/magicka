using Godot;

namespace Arcana.Client;

public partial class Main
{
    private void AssertTouchScroll(Control root, string name, string stage, bool overCard = false)
    {
        var scroll = root.FindChild(name, true, false) as ScrollContainer
            ?? throw new InvalidOperationException(stage + ": falta " + name);
        if (!scroll.IsVisibleInTree()) throw new InvalidOperationException(stage + ": scroll oculto.");
        var bar = scroll.GetVScrollBar();
        if (bar.MaxValue - bar.Page < 12) return; // El contenido completo ya cabe.
        var area = scroll.GetGlobalRect().Intersection(GetViewport().GetVisibleRect()).Grow(-12);
        if (overCard)
        {
            area = scroll.FindChildren("*", "PanelContainer", true, false).OfType<Control>()
                .Where(card => card.IsVisibleInTree())
                .Select(card => card.GetGlobalRect().Intersection(area))
                .FirstOrDefault(rect => rect.Size.X >= 60 && rect.Size.Y >= 60);
        }
        if (area.Size.X < 40 || area.Size.Y < 40)
            throw new InvalidOperationException(stage + ": no hay contenido visible para deslizar.");
        float distance = Math.Min(90, area.Size.Y * 0.6f);
        var start = area.GetCenter() + new Vector2(0, distance / 2);
        int original = scroll.ScrollVertical;
        bool emulateTouch = Input.EmulateTouchFromMouse, emulateMouse = Input.EmulateMouseFromTouch;
        try
        {
            // En CI no hay pantalla táctil física. La emulación habilita el mismo scroll nativo.
            Input.EmulateTouchFromMouse = Input.EmulateMouseFromTouch = true;
            void Swipe(float delta)
            {
                GetViewport().PushInput(new InputEventMouseMotion { Position = start, GlobalPosition = start });
                Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = start });
                Input.FlushBufferedEvents();
                var previous = start;
                for (int step = 1; step <= 3; step++)
                {
                    var point = start + new Vector2(0, delta * step / 3);
                    Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = point, Relative = point - previous });
                    Input.FlushBufferedEvents();
                    previous = point;
                }
                Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = previous });
                Input.FlushBufferedEvents();
            }
            Swipe(-distance);
            int afterUp = scroll.ScrollVertical;
            if (afterUp <= original + 5)
                throw new InvalidOperationException(stage + ": arrastrar el contenido hacia arriba no desplaza la pantalla.");
            Swipe(distance / 2);
            if (scroll.ScrollVertical >= afterUp - 5)
                throw new InvalidOperationException(stage + ": arrastrar el contenido hacia abajo no devuelve el scroll.");
            GD.Print($"TOUCH_SCROLL_OK:{stage}: {original} -> {afterUp} -> {scroll.ScrollVertical}");
        }
        finally
        {
            Input.EmulateTouchFromMouse = emulateTouch;
            Input.EmulateMouseFromTouch = emulateMouse;
            scroll.ScrollVertical = original;
        }
    }
}
