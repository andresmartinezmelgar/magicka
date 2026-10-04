using Godot;
using Arcana.Core;

namespace Arcana.Client;

public static class Ui
{
    public static readonly Color Gold = new("f1cd83"), Text = new("f4f7ff"), Muted = new("b0bfd4"), Blue = new("37b9ff"), Red = new("ff526c");
    public static readonly Color Health = new("55e879"), Shield = new("ffe04b"), Energy = new("b995ff");
    public static Color ElementColor(Element e) => e switch
    {
        Element.Water => new("3796ff"), Element.Life => Health, Element.Shield => Shield,
        Element.Cold => new("d9fbff"), Element.Lightning => new("b58bff"), Element.Arcane => new("ff4fb5"),
        Element.Earth => new("c79c68"), _ => new("ff642f")
    };
    private static readonly string[] ElementNames = ["Agua", "Vida", "Escudo", "Frío", "Rayo", "Arcano", "Tierra", "Fuego"];
    private static readonly string[] ElementKeys = ["Q", "W", "E", "R", "A", "S", "D", "F"];
    public static string ElementName(Element e) => ElementNames[(int)e];
    public static string ElementKey(Element e) => ElementKeys[(int)e];
    public static PanelContainer ElementTile(Element e, bool compact = false)
    {
        Color color = ElementColor(e);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new(compact ? 69 : 96, compact ? 68 : 86), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = Panel(new("0b1526"), 6, color);
        style.BorderWidthBottom = 3; style.ContentMarginLeft = style.ContentMarginRight = 5;
        style.ContentMarginTop = style.ContentMarginBottom = 7;
        panel.AddThemeStyleboxOverride("panel", style);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 3); panel.AddChild(box);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; row.AddThemeConstantOverride("separation", 6); box.AddChild(row);
        row.AddChild(new ElementIcon { Value = e, CustomMinimumSize = new(compact ? 20 : 25, compact ? 20 : 25) });
        row.AddChild(Label(ElementKey(e), compact ? 17 : 20));
        var name = Label(ElementName(e), compact ? 13 : 15, color); name.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(name);
        return panel;
    }
    public static float ShieldMaximum(WizardSnapshot player) => player.ShieldMax > 0 ? player.ShieldMax
        : player.Ward.Contains(Element.Earth) ? 650 : player.Ward.Length > 0 ? 180 : 420;
    public static Color SpellColor(Element[] elements)
    {
        if (elements.Length == 0) return new Color("d8eaf8");
        // La media RGB desaturaba las combinaciones. Conserva el color de su forma principal.
        if (elements.Contains(Element.Earth)) return ElementColor(Element.Earth);
        if (elements.Contains(Element.Shield))
        {
            foreach (var e in elements) if (e != Element.Shield) return ElementColor(e);
            return Shield;
        }
        if (elements.Contains(Element.Water) && elements.Contains(Element.Cold)) return ElementColor(Element.Cold);
        if (elements.Contains(Element.Arcane)) return ElementColor(Element.Arcane);
        if (elements.Contains(Element.Life)) return Health;
        if (elements.Contains(Element.Lightning)) return ElementColor(Element.Lightning);
        if (elements.Contains(Element.Water) && elements.Contains(Element.Fire)) return new("ffe3c8");
        if (elements.Contains(Element.Fire)) return ElementColor(Element.Fire);
        if (elements.Contains(Element.Cold)) return ElementColor(Element.Cold);
        return ElementColor(Element.Water);
    }
    public static string ModeName(GameMode mode) => mode switch { GameMode.Duel => "Duelo", GameMode.Arena => "Todos contra todos", _ => "Guerra de puntos" };
    public static StyleBoxFlat Panel(Color? color = null, int radius = 12, Color? border = null)
    {
        var style = new StyleBoxFlat
        {
            BgColor = color ?? new Color(0.025f, 0.043f, 0.072f, 0.98f),
            BorderColor = border ?? new Color("405372"), BorderWidthBottom = 1, BorderWidthTop = 1,
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
        label.AddThemeColorOverride("font_outline_color", new Color("070c16")); label.AddThemeConstantOverride("outline_size", 2);
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
