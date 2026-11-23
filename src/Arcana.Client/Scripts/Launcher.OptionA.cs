using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>
/// Opción A: composición panorámica del launcher sobre sus controles originales.
/// Navegación común, acceso directo a PvP y selección de modos locales.
/// </summary>
public partial class Launcher
{
    private ArcanaMenuChrome? _optionAChrome;
    private Container? _optionAPrimaryActions;
    private VBoxContainer? _optionAMagic;
    private ElementTextLabel? _optionAHeading;
    private ElementTextLabel? _optionAPasswordLabel;
    private HBoxContainer? _tutorialPrompt;
    private GridContainer? _optionAHomeCards, _optionASettingsGrid, _optionAPracticeGrid, _optionAElements;

    private void ApplyOptionALayout(MarginContainer margin, HBoxContainer columns,
        VBoxContainer rightColumn, ScrollContainer scroll,
        PanelContainer panel, VBoxContainer form, HBoxContainer identity)
    {
        columns.AddThemeConstantOverride("separation", 0);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.Name = "LauncherSettingsScroll";
        scroll.CustomMinimumSize = Vector2.Zero;
        scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        Ui.VisibleVerticalScroll(scroll);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.Name = "OptionAFullWidthPanel";
        panel.CustomMinimumSize = Vector2.Zero;
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeStyleboxOverride("panel", WidePanel());
        form.SizeFlagsVertical = SizeFlags.ExpandFill;
        form.AddThemeConstantOverride("separation", Ui.IsMobile ? 6 : 10);

        var hero = new PanelContainer { Name = "OptionAHero" };
        var heroStyle = Ui.Panel(new Color("0b2541"), 9, new Color("2c719a"));
        heroStyle.ContentMarginTop = heroStyle.ContentMarginBottom = 10;
        hero.AddThemeStyleboxOverride("panel", heroStyle);
        form.AddChild(hero);
        form.MoveChild(hero, 0);
        var heroRow = new HBoxContainer();
        heroRow.AddThemeConstantOverride("separation", 16);
        hero.AddChild(heroRow);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        titles.AddThemeConstantOverride("separation", 0);
        heroRow.AddChild(titles);
        _optionAHeading = Ui.Label("ELIGE TU AVENTURA", Ui.IsMobile ? 21 : 26, Ui.Gold);
        titles.AddChild(_optionAHeading);
        var subtitle = Ui.Label("Hechizos, estrategia y duelos elementales", Ui.IsMobile ? 14 : 16, Ui.Muted);
        subtitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        titles.AddChild(subtitle);
        BuildTutorialPrompt(heroRow);

        // Cuatro accesos principales organizados siempre en 2 filas por 2 columnas.
        _homeMenu.AddThemeConstantOverride("separation", 12);
        var choices = new GridContainer { Name = "OptionAHomeCards", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _optionAHomeCards = choices;
        choices.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 18);
        _homeMenu.AddChild(choices);
        _homeMenu.MoveChild(choices, 0);
        var existing = _homeMenu.GetChildren().OfType<Button>().ToArray();
        string[] titlesText =
        [
            "⚔\nPvP\nLucha con otros magos",
            "✧\nIA / DEFENSA\nCombate y supervivencia",
            "♙\nMI PERFIL\nPersonaje, cuenta y estadísticas"
        ];
        for (int i = 0; i < existing.Length && i < 3; i++)
        {
            var card = existing[i];
            if (i < 2) card.Name = i == 0 ? "HomePvp" : "HomeAi";
            card.Reparent(choices);
            card.Text = titlesText[i];
            CardStyle(card, i);
        }
        var helpTile = Ui.Button("▤\nCÓMO JUGAR\nControles y objetivos", () => _main.ToggleHelp());
        choices.AddChild(helpTile);
        CardStyle(helpTile, 3);

        // Los paneles se apilan cuando no cabe una segunda columna legible.
        _playBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playBody.SizeFlagsVertical = SizeFlags.ExpandFill;
        _playBody.AddThemeConstantOverride("separation", 7);
        var controls = new GridContainer { Name = "OptionASettingsColumns", Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _optionASettingsGrid = controls;
        controls.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 18);
        _playBody.AddChild(controls);
        _playBody.MoveChild(controls, 0);
        var settingsCard = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.79f,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        settingsCard.AddThemeStyleboxOverride("panel", CompactCard());
        controls.AddChild(settingsCard);
        var settings = new VBoxContainer();
        settings.AddThemeConstantOverride("separation", Ui.IsMobile ? 7 : 9);
        settingsCard.AddChild(settings);
        var settingLabel = Ui.Label("EQUIPO Y CONEXIÓN", Ui.IsMobile ? 18 : 20, Ui.Gold);
        settings.AddChild(settingLabel);
        identity.Reparent(settings);
        _optionAPasswordLabel = form.GetChildren().OfType<ElementTextLabel>()
            .FirstOrDefault(label => label.Text == "Contraseña del servidor (si tiene)");
        _optionAPasswordLabel?.Reparent(settings);
        _password.Reparent(settings);
        _status.Reparent(settings);
        _status.CustomMinimumSize = Vector2.Zero;
        var tabCard = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.65f,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        tabCard.AddThemeStyleboxOverride("panel", CompactCard());
        controls.AddChild(tabCard);
        var tabBody = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        tabBody.AddThemeConstantOverride("separation", 6);
        tabCard.AddChild(tabBody);
        _tabs.Reparent(tabBody);
        _tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
        _tabs.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0b1e34"), 7, new Color("2a5676")));
        _tabs.AddThemeStyleboxOverride("tab_selected", Ui.Panel(new Color("144c7c"), 7, Ui.Blue));
        _tabs.AddThemeStyleboxOverride("tab_unselected", Ui.Panel(new Color("122943"), 7, new Color("244968")));
        CompactPracticeTab();


        _optionAMagic = new VBoxContainer { Visible = false };
        _optionAMagic.AddThemeConstantOverride("separation", 12);
        settings.AddChild(_optionAMagic);
        _optionAMagic.AddChild(Ui.Label("LOS OCHO ELEMENTOS", 18, Ui.Gold));
        var elementGrid = new GridContainer { Columns = 4 };
        _optionAElements = elementGrid;
        elementGrid.AddThemeConstantOverride("h_separation", 5);
        elementGrid.AddThemeConstantOverride("v_separation", 5);
        _optionAMagic.AddChild(elementGrid);
        foreach (var element in Enum.GetValues<Element>()) elementGrid.AddChild(Ui.ElementTile(element, true));
        var aiHint = Ui.Label("Configura los bots y el mapa sin servidor. Tu equipación se conservará.", 15, Ui.Muted);
        aiHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _optionAMagic.AddChild(aiHint);

        // Acción primaria y Volver juntos, anclados al mismo lugar en PvP y contra IA.
        var actions = new GridContainer
        {
            Name = "OptionAPrimaryActions",
            Columns = 2,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        actions.AddThemeConstantOverride("h_separation", 12);
        actions.AddThemeConstantOverride("v_separation", 8);
        // Fuera de la zona desplazable: Volver y Jugar no quedan tapados por el
        // pie de navegación a 720p ni en pantallas panorámicas más pequeñas.
        rightColumn.AddChild(actions);
        _optionAPrimaryActions = actions;
        _menuBack.Reparent(actions);
        _connect.Reparent(actions);
        _menuBack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _connect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _connect.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 54);
        _menuBack.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 54);

        _optionAChrome = ArcanaMenuChrome.Attach(this, margin, _main,
            "Inicio", 0, NavigateOptionA);
        GetViewport().SizeChanged += UpdateResponsiveMenu;
        UpdateResponsiveMenu();
    }

}
