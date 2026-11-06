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
        var content = scroll.GetGlobalRect();
        content.Size -= new Vector2(bar.Visible ? bar.Size.X : 0, 0);
        var area = content.Intersection(GetViewport().GetVisibleRect()).Grow(-12);
        if (overCard || overButton)
        {
            area = scroll.FindChildren("*", overButton ? "Button" : "PanelContainer", true, false).OfType<Control>()
                .Where(card => card.IsVisibleInTree())
                .Select(card => card.GetGlobalRect().Intersection(area))
                .FirstOrDefault(rect => rect.Size.X >= 60 && rect.Size.Y >= 60);
        }
        if (area.Size.X < 40 || area.Size.Y < 40)
            throw new InvalidOperationException(stage + ": no hay contenido visible para deslizar. Scroll=" + scroll.GetGlobalRect() + "; zona=" + area + "; padre=" + ((Control)scroll.GetParent()).GetGlobalRect());
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
            var start = ContentPoint(upwards: true);
            float distance = Math.Min(90, start.Y - area.Position.Y - 4);
            DragTouch(start, new(0, -distance));
            int afterUp = scroll.ScrollVertical;
            if (afterUp <= original + 5)
                throw new InvalidOperationException(stage + ": arrastrar el contenido hacia arriba no desplaza la pantalla.");
            var reverseStart = ContentPoint(upwards: false);
            DragTouch(reverseStart, new(0, Math.Min(distance / 2, area.End.Y - reverseStart.Y - 4)));
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

    private void DragTouch(Vector2 start, Vector2 delta)
    {
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = start, GlobalPosition = start }, true);
        var transform = viewport.GetFinalTransform();
        var from = transform * start;
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = true, Position = from });
        Input.FlushBufferedEvents();
        if (delta != Vector2.Zero)
        {
            for (int step = 1; step <= 3; step++)
            {
                var point = transform * (start + delta * step / 3);
                Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = point, Relative = point - from });
                Input.FlushBufferedEvents();
                from = point;
            }
        }
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = false, Position = from });
        Input.FlushBufferedEvents();
    }

    private void AssertScrollBarDrag(ScrollBar bar, bool vertical)
    {
        var grabber = bar.GetThemeStylebox("grabber").GetMinimumSize();
        if (!bar.IsVisibleInTree() || (vertical ? bar.Size.X : bar.Size.Y) < 44 ||
            (vertical ? grabber.Y : grabber.X) < 44)
            throw new InvalidOperationException("La barra y el indicador deben tener una zona táctil de 44 px.");

        var position = bar.GetGlobalRect().Position;
        var grip = vertical ? new Vector2(bar.Size.X / 2, 20) : new Vector2(20, bar.Size.Y / 2);
        bar.Value = bar.MinValue;
        DragTouch(position + grip, vertical ? new(0, 32) : new(32, 0));
        if (bar.Value <= bar.MinValue)
            throw new InvalidOperationException("La barra no se mueve al arrastrar su indicador.");
    }

    private ScrollChoiceList? _scrollSmokeList;
    private ContentScrollContainer? _scrollSmokeChart;
    private int _scrollSmokeActivations;

    private void BuildScrollFixture()
    {
        _scrollSmokeList = new ScrollChoiceList { Position = new(40, 40), Size = new(380, 140),
            Theme = Ui.Theme(), ActivateOnTap = true };
        for (int i = 0; i < 12; i++) _scrollSmokeList.AddItem("Opción " + i);
        _scrollSmokeList.ItemActivated += _ => _scrollSmokeActivations++;
        _ui.AddChild(_scrollSmokeList);
        _scrollSmokeChart = new ContentScrollContainer { Position = new(40, 210), Size = new(380, 140),
            Theme = Ui.Theme(), VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scrollSmokeChart.AddChild(new Panel { CustomMinimumSize = new(900, 90) });
        _ui.AddChild(_scrollSmokeChart);
    }

    private void AssertScrollFixture()
    {
        var list = _scrollSmokeList!;
        var chart = _scrollSmokeChart!;
        bool emulateTouch = Input.EmulateTouchFromMouse, emulateMouse = Input.EmulateMouseFromTouch;
        try
        {
            Input.EmulateTouchFromMouse = Input.EmulateMouseFromTouch = true;
            DragTouch(list.Position + new Vector2(80, 32), Vector2.Zero);
            if (list.SelectedIndex != 0 || _scrollSmokeActivations != 1)
                throw new InvalidOperationException("El toque no selecciona y activa la opción.");
            DragTouch(list.Position + new Vector2(80, 105), new(0, -70));
            if (list.ScrollVertical <= 0 || list.SelectedIndex != 0 || _scrollSmokeActivations != 1)
                throw new InvalidOperationException("Arrastrar una fila debe mover la lista sin activar una opción.");
            DragTouch(chart.Position + new Vector2(160, 42), new(-90, 0));
            if (chart.ScrollHorizontal <= 0)
                throw new InvalidOperationException("No se puede arrastrar el contenido horizontal.");
            AssertScrollBarDrag(list.GetVScrollBar(), vertical: true);
            AssertScrollBarDrag(chart.GetHScrollBar(), vertical: false);
            GD.Print("TOUCH_SCROLL_CONTROLS_OK");
        }
        finally
        {
            Input.EmulateTouchFromMouse = emulateTouch;
            Input.EmulateMouseFromTouch = emulateMouse;
            list.QueueFree();
            chart.QueueFree();
        }
    }
}
