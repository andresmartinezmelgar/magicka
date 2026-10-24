using Godot;

namespace Arcana.Client;

public static partial class Ui
{
    /// <summary>Acción de salida compacta para cabeceras que quedan fuera del contenido desplazable.</summary>
    public static Button ExitButton(string text, Action exit, string name)
    {
        var button = Button(text, exit); button.Name = name;
        button.CustomMinimumSize = new(0, 44); button.AddThemeFontSizeOverride("font_size", 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            var style = Panel(new Color(state switch { "hover" => "263951", "pressed" => "324864",
                "disabled" => "121c2c", _ => "19283d" }), 8, state is "hover" or "pressed" ? Gold : null);
            style.ContentMarginLeft = style.ContentMarginRight = 10;
            style.ContentMarginTop = style.ContentMarginBottom = 5;
            button.AddThemeStyleboxOverride(state, style);
        }
        return button;
    }
}
