using Godot;
using Arcana.Core;

namespace Arcana.Client;

public static class Ui
{
    public static readonly Color Gold = new("dfbf7d"), Text = new("e9edf5"), Muted = new("9babc1"), Blue = new("77caff"), Red = new("ff8479");
    public static Color ElementColor(Element e) => e switch
    {
        Element.Water => new("5faee9"), Element.Life => new("78dfab"), Element.Shield => new("e7c66d"),
        Element.Cold => new("c4f2ff"), Element.Lightning => new("b991ff"), Element.Arcane => new("ed7198"),
        Element.Earth => new("b48e64"), _ => new("ff9754")
    };
    public static string ElementName(Element e) => new[] { "Agua", "Vida", "Escudo", "Frío", "Rayo", "Arcano", "Tierra", "Fuego" }[(int)e];
    public static string ElementKey(Element e) => new[] { "Q", "W", "E", "R", "A", "S", "D", "F" }[(int)e];
    public static Color SpellColor(Element[] elements)
    {
        if (elements.Length == 0) return new Color("d8eaf8");
        var c = new Color(0, 0, 0, 1);
        foreach (var e in elements) { var a = ElementColor(e); c.R += a.R; c.G += a.G; c.B += a.B; }
        return new(c.R / elements.Length, c.G / elements.Length, c.B / elements.Length, 1);
    }
    public static string ModeName(GameMode mode) => mode switch { GameMode.Duel => "Duelo", GameMode.Arena => "Todos contra todos", _ => "Guerra de puntos" };
    public static StyleBoxFlat Panel(Color? color = null, int radius = 12, Color? border = null)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color ?? new Color(0.035f, 0.059f, 0.095f, 0.96f),
            BorderColor = border ?? new Color("2b3a52"), BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1, CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 16, ContentMarginBottom = 16
        };
        return style;
    }
    public static Theme Theme()
    {
        var theme = new Theme { DefaultFontSize = 17 };
        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Gold);
        theme.SetColor("font_disabled_color", "Button", Muted);
        theme.SetStylebox("normal", "Button", Panel(new("19283d"), 8));
        theme.SetStylebox("hover", "Button", Panel(new("263951"), 8, Gold));
        theme.SetStylebox("pressed", "Button", Panel(new("324864"), 8, Gold));
        theme.SetStylebox("disabled", "Button", Panel(new("121c2c"), 8));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetStylebox("normal", "LineEdit", Panel(new("0c1626"), 7));
        theme.SetStylebox("focus", "LineEdit", Panel(new("0c1626"), 7, Gold));
        theme.SetColor("font_color", "LineEdit", Text);
        theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        theme.SetStylebox("panel", "PanelContainer", Panel());
        theme.SetStylebox("background", "ProgressBar", Panel(new("1d2b41"), 4));
        theme.SetStylebox("fill", "ProgressBar", Panel(Blue, 4, Blue));
        return theme;
    }
    public static Label Label(string text, int size = 17, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color != null) label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }
    public static Button Button(string text, Action pressed, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, 45), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (primary)
        {
            button.AddThemeStyleboxOverride("normal", Panel(new("a6874e"), 8, Gold));
            button.AddThemeStyleboxOverride("hover", Panel(new("c3a469"), 8, Gold));
            button.AddThemeColorOverride("font_color", new Color("101727"));
            button.AddThemeColorOverride("font_hover_color", new Color("101727"));
        }
        button.Pressed += pressed;
        return button;
    }
    public static void Full(Control control) => control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
}
