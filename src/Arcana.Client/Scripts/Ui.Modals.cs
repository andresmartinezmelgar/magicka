using Godot;

namespace Arcana.Client;

public static partial class Ui
{
    public static readonly Color ModalDim = new(ArcanaMenuChrome.Backdrop, 0.90f);
    public static StyleBoxFlat ModalPanel()
    {
        var style = Panel(ArcanaMenuChrome.Surface, 12, ArcanaMenuChrome.Border);
        style.ContentMarginLeft = style.ContentMarginRight = 16;
        style.ContentMarginTop = style.ContentMarginBottom = 12;
        return style;
    }
    private static void ApplyModalTheme(Theme theme)
    {
        theme.SetStylebox("panel", "AcceptDialog", ModalPanel());
        theme.SetConstant("buttons_separation", "AcceptDialog", 12);
        theme.SetConstant("buttons_min_height", "AcceptDialog", 46);
        theme.SetStylebox("panel", "PopupMenu", ModalPanel());
        theme.SetStylebox("hover", "PopupMenu", Panel(ArcanaMenuChrome.SurfaceRaised, 6, Blue));
        theme.SetColor("font_color", "PopupMenu", Text);
        theme.SetColor("font_hover_color", "PopupMenu", Gold);
        theme.SetColor("font_disabled_color", "PopupMenu", Muted);
        theme.SetStylebox("panel", "TooltipPanel", Panel(ArcanaMenuChrome.SurfaceRaised, 8, ArcanaMenuChrome.Border));
    }

    /// <summary>Marco, botones y sombreado comunes; el contenido y acciones siguen en su propietario.</summary>
    public static void ConfigureDialog(AcceptDialog dialog, bool backdrop = true)
    {
        dialog.Theme = Theme(); dialog.Borderless = true; dialog.Unresizable = true;
        dialog.Exclusive = dialog.Transient = true; dialog.GetLabel().Hide();
        foreach (var button in dialog is ConfirmationDialog confirmation
            ? new[] { dialog.GetOkButton(), confirmation.GetCancelButton() } : new[] { dialog.GetOkButton() })
        {
            button.CustomMinimumSize = new(0, 46); button.AddThemeFontSizeOverride("font_size", IsMobile ? 18 : 17);
            button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            var focus = Panel(new Color(0, 0, 0, 0), 8, Gold);
            focus.BorderWidthBottom = focus.BorderWidthTop = focus.BorderWidthLeft = focus.BorderWidthRight = 2;
            button.AddThemeStyleboxOverride("focus", focus);
        }
        var accept = dialog.GetOkButton();
        foreach (var (state, color) in new[] { ("normal", "a6874e"), ("hover", "c3a469"), ("pressed", "96743e") })
            accept.AddThemeStyleboxOverride(state, Panel(new(color), 8, Gold));
        foreach (string color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            accept.AddThemeColorOverride(color, new Color("101727"));
        if (!backdrop) return;
        var parent = dialog.GetParent();
        var dim = new ColorRect { Name = dialog.Name + "Backdrop", Color = ModalDim, Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop };
        parent.AddChild(dim); Full(dim);
        dialog.VisibilityChanged += () =>
        {
            if (!GodotObject.IsInstanceValid(dim)) return;
            dim.Visible = dialog.Visible;
            if (dialog.Visible) parent.MoveChild(dim, -1);
        };
        dialog.TreeExiting += () => { if (GodotObject.IsInstanceValid(dim)) dim.QueueFree(); };
    }

    internal static void VerifyDialog(AcceptDialog dialog)
    {
        if (dialog.GetThemeStylebox("panel", "AcceptDialog") is not StyleBoxFlat style ||
            style.BgColor != ArcanaMenuChrome.Surface || style.BorderColor != ArcanaMenuChrome.Border ||
            !dialog.Borderless || !dialog.Exclusive || dialog.GetOkButton().CustomMinimumSize.Y < 44)
            throw new InvalidDataException("La modal no utiliza el marco visual común: " + dialog.Name);
    }
}
