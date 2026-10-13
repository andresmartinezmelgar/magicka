using Godot;
using Arcana.Core;

namespace Arcana.Client;

public static class Ui
{
    public static bool IsMobile { get; } = OS.HasFeature("mobile") || OS.GetCmdlineUserArgs().Any(a => a is "--mobile" or "--mobile-smoke" or "--mobile-fixture" or "--account-entry-smoke");
    public static readonly Color Gold = new("f1cd83"), Text = new("f4f7ff"), Muted = new("b0bfd4"), Blue = new("37b9ff"), Red = new("ff526c");
    public static readonly Color Health = new(IsMobile ? "30ed69" : "2ce273"), Shield = new(IsMobile ? "ffda00" : "ffcf00"), Energy = new(IsMobile ? "b16aff" : "aa65ff");
    public static OptionButton TeamChoice(int team = -1)
    {
        // -1 es el ID automático del propio OptionButton, no un ID de equipo.
        var choice = new OptionButton { CustomMinimumSize = new(0, IsMobile ? 66 : 45), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        choice.AddItem("Automático", 2); choice.AddItem("Azul", 0); choice.AddItem("Rojo", 1);
        choice.Select(choice.GetItemIndex(team is 0 or 1 ? team : 2));
        return choice;
    }
    public static int SelectedTeam(OptionButton choice) => choice.GetSelectedId() is 0 or 1 ? choice.GetSelectedId() : -1;
    public static Color ElementColor(Element e) => ElementColor(e, IsMobile);
    private static readonly Color[] MobileElements = [new("2985ff"), new("30ed69"), new("ffda00"), new("1ddecb"),
        new("b16aff"), new("ff3b9d"), new("d6a252"), new("ff5d26")];
    private static readonly Color[] DesktopElements = [new("2791ff"), new("2ce273"), new("ffcf00"), new("10d7cd"),
        new("aa65ff"), new("ff369f"), new("cb903f"), new("ff611c")];
    internal static Color ElementColor(Element e, bool mobile) =>
        (mobile ? MobileElements : DesktopElements)[(uint)e < 8 ? (int)e : 7];
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
    public static Color WizardColor(WizardType type) => WizardTypes.Affinity(type) is { } element ? ElementColor(element) : Gold;
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
        var theme = new Theme { DefaultFontSize = IsMobile ? 21 : 17 };
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
        label.AddThemeFontSizeOverride("font_size", IsMobile ? Math.Max(size, 16) : size);
        label.AddThemeColorOverride("font_outline_color", new Color("070c16")); label.AddThemeConstantOverride("outline_size", 2);
        if (color != null) label.AddThemeColorOverride("font_color", color.Value);
        return label;
    }
    public static Button Button(string text, Action pressed, bool primary = false)
    {
        var button = new Button { Text = text, CustomMinimumSize = new(0, IsMobile ? 68 : 45),
            MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        if (IsMobile) button.AddThemeFontSizeOverride("font_size", 21);
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
    /// <summary>Tamaño táctil coherente para inputs, selectores y botones en Android.</summary>
    public static void TouchTarget(Control control, float mobileHeight = 66)
    {
        if (!IsMobile) return;
        control.CustomMinimumSize = new Vector2(control.CustomMinimumSize.X,
            Math.Max(control.CustomMinimumSize.Y, mobileHeight));
    }

    public static void Full(Control control) => control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
}
