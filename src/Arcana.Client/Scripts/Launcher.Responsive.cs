using Godot;
using Arcana.Core;

namespace Arcana.Client;

public partial class Launcher
{
    private void UpdateResponsiveMenu()
    {
        float viewportWidth = GetViewport().GetVisibleRect().Size.X;
        float viewportHeight = GetViewport().GetVisibleRect().Size.Y;
        bool shortMobile = Ui.IsMobile && viewportHeight <= 560;
        float available = Math.Max(280, viewportWidth - (Ui.IsMobile ? 55 : 120));
        if (_optionAPrimaryActions is GridContainer primary)
            primary.Columns = ResponsiveLayout.Columns(available, 220, 2, 12);
        if (_optionAHomeCards != null)
        {
            // Mantener estrictamente 2×2 en móvil y escritorio; altura ajustada
            // al área útil para no esconder la segunda fila bajo la barra inferior.
            _optionAHomeCards.Columns = 2;
            float height = GetViewport().GetVisibleRect().Size.Y;
            float free = height - ArcanaMenuChrome.HeaderHeight - ArcanaMenuChrome.FooterHeight -
                (Ui.IsMobile ? 145 : 185);
            float tileHeight = Math.Clamp((free - (Ui.IsMobile ? 10 : 18)) / 2f,
                Ui.IsMobile ? shortMobile ? 96 : 125 : 150, Ui.IsMobile ? 210 : 245);
            foreach (Button card in _optionAHomeCards.GetChildren().OfType<Button>())
            {
                card.CustomMinimumSize = new Vector2(0, tileHeight);
                card.AddThemeFontSizeOverride("font_size", shortMobile ? 18 : Ui.IsMobile ? 21 : 24);
                foreach (string state in new[] { "normal", "hover", "pressed" })
                    if (card.GetThemeStylebox(state) is StyleBoxFlat style)
                        style.ContentMarginTop = style.ContentMarginBottom = shortMobile ? 8 : 16;
            }
        }
        float scrollWidth = available - Ui.ScrollBarThickness;
        if (_optionASettingsGrid != null)
        {
            int columns = ResponsiveLayout.Columns(scrollWidth, 415, 2, Ui.IsMobile ? 10 : 18);
            _optionASettingsGrid.Columns = columns;
            if (_optionAPracticeGrid != null)
            {
                float practiceWidth = columns == 2 ? (scrollWidth - 24) / 2 : scrollWidth;
                _optionAPracticeGrid.Columns = ResponsiveLayout.Columns(practiceWidth, 385, 2, 14);
            }
        }
        if (_optionAElements != null)
            _optionAElements.Columns = ResponsiveLayout.Columns(
                _optionASettingsGrid?.Columns == 2 ? (scrollWidth - 24) / 2 : scrollWidth, 85, 4, 5);
    }

    /// <summary>La modalidad IA se reordena según el espacio; no pierde controles.</summary>
    private void CompactPracticeTab()
    {
        if (_tabs.GetChild(2) is not VBoxContainer practice) return;
        var parts = practice.GetChildren().OfType<Control>().ToArray();
        if (parts.Length < 6) return;
        foreach (var label in parts.OfType<ElementTextLabel>()) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        practice.AddThemeConstantOverride("separation", 6);
        var row = new GridContainer { Name = "OptionAIaSettingsColumns", Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _optionAPracticeGrid = row;
        row.AddThemeConstantOverride("separation", Ui.IsMobile ? 14 : 22);
        practice.AddChild(row);
        practice.MoveChild(row, 0);
        var maps = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.1f };
        maps.AddThemeConstantOverride("separation", 7);
        row.AddChild(maps);
        var bots = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.0f };
        bots.AddThemeConstantOverride("separation", 10);
        row.AddChild(bots);
        // El mapa, la modalidad y su objetivo a la izquierda; bots y medición a la derecha.
        foreach (var part in parts.Take(5)) part.Reparent(maps);
        foreach (var part in parts.Skip(5)) part.Reparent(bots);
        if (parts[0] is ElementTextLabel explanation)
            explanation.AddThemeFontSizeOverride("font_size", 14);
        if (parts[4] is ElementTextLabel objective)
            objective.AddThemeFontSizeOverride("font_size", 14);
    }

    private static StyleBoxFlat WidePanel()
    {
        var box = Ui.Panel(new Color("081629"), 12, new Color("2878a2"));
        box.ContentMarginLeft = box.ContentMarginRight = Ui.IsMobile ? 10 : 16;
        box.ContentMarginTop = box.ContentMarginBottom = Ui.IsMobile ? 7 : 12;
        return box;
    }

    private static StyleBoxFlat CompactCard()
    {
        var box = Ui.Panel(new Color("0c2038"), 9, new Color("2a6085"));
        box.ContentMarginLeft = box.ContentMarginRight = Ui.IsMobile ? 10 : 16;
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        return box;
    }

    private static void CardStyle(Button button, int idx)
    {
        var border = idx == 0 ? new Color("3480c2") : idx == 1 ? Ui.Gold :
            idx == 2 ? new Color("8662cb") : new Color("3d9caf");
        var fill = idx == 0 ? new Color("113252") : idx == 1 ? new Color("40301a") :
            idx == 2 ? new Color("291c42") : new Color("132f3c");
        button.AddThemeStyleboxOverride("normal", Ui.Panel(fill, 9, border));
        button.AddThemeStyleboxOverride("hover", Ui.Panel(fill.Lightened(0.18f), 9, Ui.Gold));
        button.AddThemeStyleboxOverride("pressed", Ui.Panel(fill.Darkened(0.12f), 9, Ui.Gold));
        button.AddThemeColorOverride("font_color", Ui.Text);
        button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 21 : 24);
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.CustomMinimumSize = new(0, Ui.IsMobile ? 165 : 195);
    }
}
