using Godot;

namespace Arcana.Client;

public static partial class Ui
{
    internal static void ToggleBorder(Button button, bool active)
    {
        button.ToggleMode = true; button.ButtonPressed = active;
        var color = active ? Health : Red;
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
        {
            var style = button.GetThemeStylebox(state) is StyleBoxFlat flat ? (StyleBoxFlat)flat.Duplicate() : Panel();
            style.BorderColor = color;
            if (state == "focus") style.BgColor = Colors.Transparent;
            style.BorderWidthLeft = style.BorderWidthRight = style.BorderWidthTop = style.BorderWidthBottom = 2;
            button.AddThemeStyleboxOverride(state, style);
        }
    }
}
