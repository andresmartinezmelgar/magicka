using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Launcher
{
    private void BuildTutorialPrompt(HBoxContainer heroRow)
    {
        const int diameter = 64;
        _tutorialPrompt = new HBoxContainer { Name = "TutorialPrompt", MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _tutorialPrompt.AddThemeConstantOverride("separation", 8);
        heroRow.AddChild(_tutorialPrompt);
        var badge = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        var badgeStyle = Ui.Panel(new Color("38293f"), 10, new Color("9f7645"));
        badgeStyle.ContentMarginLeft = badgeStyle.ContentMarginRight = 8;
        badgeStyle.ContentMarginTop = badgeStyle.ContentMarginBottom = 5;
        badge.AddThemeStyleboxOverride("panel", badgeStyle);
        _tutorialPrompt.AddChild(badge);
        var recommendation = Ui.Label("Recomendado", 15, Ui.Gold);
        recommendation.Name = "TutorialRecommendation";
        recommendation.MouseFilter = MouseFilterEnum.Ignore;
        badge.AddChild(recommendation);

        var bubble = new Control { CustomMinimumSize = new(diameter + 4, diameter + 9), MouseFilter = MouseFilterEnum.Ignore };
        _tutorialPrompt.AddChild(bubble);
        var tutorial = Ui.Button("Tutorial", _main.OpenTutorial);
        tutorial.Name = "TutorialMode";
        tutorial.TooltipText = "Iniciar o continuar el tutorial interactivo";
        tutorial.CustomMinimumSize = new(diameter, diameter);
        tutorial.AddThemeFontSizeOverride("font_size", 14);
        var ink = new Color("1c2033");
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            tutorial.AddThemeColorOverride(state, ink);

        StyleBoxFlat Sphere(Color fill)
        {
            var style = Ui.Panel(fill, diameter / 2, new Color("ffedac"));
            style.ContentMarginLeft = style.ContentMarginRight = 4;
            style.ShadowColor = new Color("0a1324cc");
            style.ShadowSize = 4;
            style.ShadowOffset = new(0, 3);
            return style;
        }
        tutorial.AddThemeStyleboxOverride("normal", Sphere(new Color("eab75c")));
        tutorial.AddThemeStyleboxOverride("hover", Sphere(new Color("ffda87")));
        tutorial.AddThemeStyleboxOverride("pressed", Sphere(new Color("d89d48")));
        var focus = Ui.Panel(Colors.Transparent, diameter / 2, Ui.Text);
        focus.DrawCenter = false;
        focus.BorderWidthTop = focus.BorderWidthBottom = focus.BorderWidthLeft = focus.BorderWidthRight = 2;
        tutorial.AddThemeStyleboxOverride("focus", focus);
        bubble.AddChild(tutorial);
        tutorial.Position = new(2, 9);
        tutorial.Size = new(diameter, diameter);

        bubble.AddChild(new TextureRect
        {
            Name = "TutorialPointer", MouseFilter = MouseFilterEnum.Ignore,
            Texture = GD.Load<Texture2D>("res://Assets/UI/tutorial-glove.svg"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new((diameter + 4 - 30) / 2f, 0), Size = new(30, 33)
        });
        UpdateTutorialLink();
    }

    internal void UpdateTutorialLink()
    {
        if (_tutorialPrompt != null) _tutorialPrompt.Visible = _page == 0 && !_main.TutorialCompleted;
    }
}
