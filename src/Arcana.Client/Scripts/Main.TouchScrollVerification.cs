using Godot;

namespace Arcana.Client;

public partial class Main
{
    private void AssertTouchScroll(Control root, string name, string stage, bool overCard = false, bool overButton = false)
    {
        var scroll = root.FindChild(name, true, false) as ScrollContainer
            ?? throw new InvalidOperationException(stage + ": falta " + name);
        if (!scroll.IsVisibleInTree()) throw new InvalidOperationException(stage + ": scroll oculto.");
        var bar = scroll.GetVScrollBar();
        if (bar.MaxValue - bar.Page < 12) return; // El contenido completo ya cabe.
        var area = scroll.GetGlobalRect().Intersection(GetViewport().GetVisibleRect()).Grow(-12);
        if (overCard || overButton)
        {
            area = scroll.FindChildren("*", overButton ? "Button" : "PanelContainer", true, false).OfType<Control>()
                .Where(card => card.IsVisibleInTree())
                .Select(card => card.GetGlobalRect().Intersection(area))
                .FirstOrDefault(rect => rect.Size.X >= 60 && rect.Size.Y >= 60);
        }
        if (area.Size.X < 40 || area.Size.Y < 40)
            throw new InvalidOperationException(stage + ": no hay contenido visible para deslizar.");
        int original = scroll.ScrollVertical;
        bool emulateTouch = Input.EmulateTouchFromMouse, emulateMouse = Input.EmulateMouseFromTouch;
        try
        {
            // En CI no hay pantalla táctil física. La emulación habilita el mismo scroll nativo.
            Input.EmulateTouchFromMouse = Input.EmulateMouseFromTouch = true;
            var interactive = scroll.FindChildren("*", "Control", true, false).OfType<Control>()
                .Where(control => control.IsVisibleInTree() &&
                    (control is BaseButton or LineEdit or TextEdit or ItemList or Godot.Range or TabBar or ScrollContainer) &&
                    !(overButton && control is Button))
                .ToArray();
            Vector2 ContentPoint(bool upwards)
            {
                // Un gesto sobre un selector sería una interacción diferente a arrastrar el contenido.
                // Se busca una zona real de texto, tarjeta o separación, sin pulsar controles activos.
                float first = upwards ? area.End.Y - 8 : area.Position.Y + 8;
                for (float y = first; upwards ? y >= area.Position.Y + 36 : y <= area.End.Y - 36; y += upwards ? -4 : 4)
                    foreach (float fraction in new[] { 0.5f, 0.25f, 0.75f })
                    {
                        var point = new Vector2(area.Position.X + area.Size.X * fraction, y);
                        if (!interactive.Any(control => control.GetGlobalRect().HasPoint(point))) return point;
                    }
                throw new InvalidOperationException(stage + ": no hay una zona pasiva visible para el gesto.");
            }
            void Swipe(Vector2 start, float delta)
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
            var start = ContentPoint(upwards: true);
            float distance = Math.Min(90, start.Y - area.Position.Y - 4);
            Swipe(start, -distance);
            int afterUp = scroll.ScrollVertical;
            if (afterUp <= original + 5)
                throw new InvalidOperationException(stage + ": arrastrar el contenido hacia arriba no desplaza la pantalla.");
            var reverseStart = ContentPoint(upwards: false);
            Swipe(reverseStart, Math.Min(distance / 2, area.End.Y - reverseStart.Y - 4));
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
