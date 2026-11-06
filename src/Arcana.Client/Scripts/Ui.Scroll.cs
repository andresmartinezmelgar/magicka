using Godot;

namespace Arcana.Client;

public static partial class Ui
{
    internal static bool UsesTouchScroll => IsMobile || DisplayServer.IsTouchscreenAvailable();
    public static int ScrollBarThickness => UsesTouchScroll ? 44 : 16;

    private static void ApplyScrollTheme(Theme theme)
    {
        bool touch = UsesTouchScroll;
        float halfWidth = ScrollBarThickness / 2f;
        float halfGrabber = touch ? halfWidth : 12;
        foreach (bool vertical in new[] { true, false })
        {
            string type = vertical ? "VScrollBar" : "HScrollBar";

            StyleBoxFlat Style(Color color, float halfLength)
            {
                var style = Panel(color, touch ? 10 : 6, new Color("345572"));
                style.ContentMarginLeft = style.ContentMarginRight = vertical ? halfWidth : halfLength;
                style.ContentMarginTop = style.ContentMarginBottom = vertical ? halfLength : halfWidth;
                return style;
            }

            theme.SetStylebox("scroll", type, Style(new Color("0a1728"), 2));
            theme.SetStylebox("scroll_focus", type, Style(new Color("182d43"), 2));
            theme.SetStylebox("grabber", type, Style(new Color("548fb7"), halfGrabber));
            theme.SetStylebox("grabber_highlight", type, Style(new Color("75bee8"), halfGrabber));
            theme.SetStylebox("grabber_pressed", type, Style(new Color("c0e7ff"), halfGrabber));
        }
    }
}
