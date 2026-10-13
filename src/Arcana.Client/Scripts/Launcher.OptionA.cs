using Godot;
using Arcana.Core;

namespace Arcana.Client;

/// <summary>
/// Opción A: composición panorámica del launcher sobre sus controles originales.
/// La lógica de botones, búsqueda LAN, servidor, bots y preferencias no cambia.
/// </summary>
public partial class Launcher
{
    private ArcanaMenuChrome? _optionAChrome;
    private HBoxContainer? _optionAPrimaryActions;
    private VBoxContainer? _optionAPreview;
    private VBoxContainer? _optionAMagic;
    private Label? _optionAHeading;

    private void ApplyOptionALayout(MarginContainer margin, HBoxContainer columns, VBoxContainer left,
        VBoxContainer rightColumn, ScrollContainer scroll, CenterContainer rightCenter,
        PanelContainer panel, VBoxContainer form, HBoxContainer identity, HBoxContainer typeRow,
        Label typeInfo, HBoxContainer obsoleteFooter, Button oldHelp, Button oldExit)
    {
        left.Visible = false;
        columns.AddThemeConstantOverride("separation", 0);
        columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        columns.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        rightColumn.SizeFlagsVertical = SizeFlags.ExpandFill;
        rightColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.CustomMinimumSize = Vector2.Zero;
        scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        rightCenter.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.CustomMinimumSize = Vector2.Zero;
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.AddThemeStyleboxOverride("panel", WidePanel());
        form.SizeFlagsVertical = SizeFlags.ExpandFill;
        form.AddThemeConstantOverride("separation", Ui.IsMobile ? 6 : 10);
        foreach (var child in form.GetChildren().OfType<Label>().Take(2)) child.Visible = false;

        var hero = new PanelContainer { Name = "OptionAHero" };
        hero.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0b2541"), 9, new Color("2c719a")));
        form.AddChild(hero);
        form.MoveChild(hero, 0);
        var heroRow = new HBoxContainer();
        heroRow.AddThemeConstantOverride("separation", 16);
        hero.AddChild(heroRow);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation", 0);
        heroRow.AddChild(titles);
        _optionAHeading = Ui.Label("ELIGE TU AVENTURA", Ui.IsMobile ? 21 : 26, Ui.Gold);
        titles.AddChild(_optionAHeading);
        titles.AddChild(Ui.Label("Hechizos, estrategia y duelos elementales", Ui.IsMobile ? 14 : 16, Ui.Muted));
        heroRow.AddChild(Ui.Label("✦  O P C I Ó N   A", Ui.IsMobile ? 14 : 15, Ui.Blue));

        // Accesos anchos en una única fila con tamaños y lugares coherentes.
        _homeMenu.AddThemeConstantOverride("separation", 12);
        var choices = new HBoxContainer { Name = "OptionAHomeCards", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        choices.AddThemeConstantOverride("separation", Ui.IsMobile ? 10 : 18);
        _homeMenu.AddChild(choices);
        _homeMenu.MoveChild(choices, 0);
        var existing = _homeMenu.GetChildren().OfType<Button>().ToArray();
        string[] titlesText =
        [
            "⚔\nPvP\nLucha con otros magos",
            "✧\nVERSUS IA\nEntrena con bots",
            "♙\nMI PERFIL\nCuenta y estadísticas"
        ];
        for (int i = 0; i < existing.Length && i < 3; i++)
        {
            var card = existing[i];
            card.Reparent(choices);
            card.Text = titlesText[i];
            CardStyle(card, i);
        }
        var helpTile = Ui.Button("▤\nCÓMO JUGAR\nControles y objetivos", () => _main.ToggleHelp());
        choices.AddChild(helpTile);
        CardStyle(helpTile, 3);
        _profileSummary.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 15 : 17);
        _profileSummary.AddThemeColorOverride("font_color", Ui.Gold);

        // Paneles de opciones de 2 columnas: no se necesita desplazamiento vertical ni horizontal.
        _playBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _playBody.SizeFlagsVertical = SizeFlags.ExpandFill;
        _playBody.AddThemeConstantOverride("separation", 7);
        var controls = new HBoxContainer { Name = "OptionASettingsColumns",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
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
        var settingLabel = Ui.Label("TU MAGO Y CONEXIÓN", Ui.IsMobile ? 18 : 20, Ui.Gold);
        settings.AddChild(settingLabel);
        identity.Reparent(settings);
        typeRow.Reparent(settings);
        typeInfo.Reparent(settings);
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


        _optionAPreview = new VBoxContainer { Visible = false, SizeFlagsVertical = SizeFlags.ExpandFill };
        _optionAPreview.AddThemeConstantOverride("separation", 16);
        tabBody.AddChild(_optionAPreview);
        _optionAPreview.AddChild(Ui.Label("PERFIL Y EQUIPACIÓN", 21, Ui.Gold));
        var profileHelp = Ui.Label("Tu personaje, el inventario, las bonificaciones individuales y el conjunto completo están disponibles desde el mismo perfil.\n\nLa sesión de Google y los datos de Firestore se consultan de forma independiente del servidor de partidas.", 17, Ui.Muted);
        profileHelp.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _optionAPreview.AddChild(profileHelp);
        _optionAPreview.AddChild(Ui.Button("Abrir perfil de Google", _main.OpenAccountProfile, true));
        _optionAPreview.AddChild(Ui.Button("Ver inventario y equipamiento", () => _main.OpenEquipment("automatic")));

        _optionAMagic = new VBoxContainer { Visible = false };
        _optionAMagic.AddThemeConstantOverride("separation", 12);
        settings.AddChild(_optionAMagic);
        _optionAMagic.AddChild(Ui.Label("LOS OCHO ELEMENTOS", 18, Ui.Gold));
        var elementGrid = new GridContainer { Columns = 4 };
        elementGrid.AddThemeConstantOverride("h_separation", 5);
        elementGrid.AddThemeConstantOverride("v_separation", 5);
        _optionAMagic.AddChild(elementGrid);
        foreach (var element in Enum.GetValues<Element>()) elementGrid.AddChild(Ui.ElementTile(element, true));
        var aiHint = Ui.Label("Configura los bots y el mapa sin servidor. Tu equipación se conservará.", 15, Ui.Muted);
        aiHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _optionAMagic.AddChild(aiHint);

        // Acción primaria y Volver juntos, anclados al mismo lugar en PvP y contra IA.
        var actions = new HBoxContainer { Name = "OptionAPrimaryActions" };
        actions.AddThemeConstantOverride("separation", 12);
        form.AddChild(actions);
        _optionAPrimaryActions = actions;
        _menuBack.Reparent(actions);
        _connect.Reparent(actions);
        _menuBack.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _connect.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _connect.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 54);
        _menuBack.CustomMinimumSize = new(0, Ui.IsMobile ? 64 : 54);

        obsoleteFooter.Visible = false;
        oldHelp.Visible = false;
        oldExit.Visible = false;
        if (_mobilePrimaryActions != null)
        {
            ((Control)_mobilePrimaryActions.GetParent().GetParent()).Visible = false;
        }

        _optionAChrome = ArcanaMenuChrome.Attach(this, margin, _main,
            "Inicio", 0, NavigateOptionA);
    }

    /// <summary>IA en dos columnas: conserva todos los ajustes pero evita desbordamientos a 720p.</summary>
    private void CompactPracticeTab()
    {
        if (_tabs.GetChild(2) is not VBoxContainer practice) return;
        var parts = practice.GetChildren().OfType<Control>().ToArray();
        if (parts.Length < 8) return;
        practice.AddThemeConstantOverride("separation", 6);
        var row = new HBoxContainer { Name = "OptionAIaSettingsColumns",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
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
        // El mapa, la modalidad y su objetivo a la izquierda; bots y laboratorio a la derecha.
        foreach (int index in new[] { 0, 1, 2, 3, 4 }) parts[index].Reparent(maps);
        foreach (int index in new[] { 5, 6, 7 }) parts[index].Reparent(bots);
        if (parts[0] is Label explanation)
            explanation.AddThemeFontSizeOverride("font_size", 14);
        if (parts[4] is Label objective)
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
        button.CustomMinimumSize = new(0, Ui.IsMobile ? 192 : 234);
    }

    private void UpdateOptionAPage(int page)
    {
        if (_optionAHeading != null) _optionAHeading.Text = page switch
        {
            1 => "PARTIDAS MULTIJUGADOR",
            2 => "ENTRENAMIENTO CONTRA IA",
            3 => "TU PERFIL Y PERSONAJE",
            _ => "ELIGE TU AVENTURA"
        };
        if (_optionAPreview != null) _optionAPreview.Visible = page == 3;
        if (_optionAMagic != null) _optionAMagic.Visible = page == 2;
        _optionAChrome?.Select(page switch { 1 => 1, 2 => 2, 3 => 3, _ => 0 });
    }

    public void NavigateOptionA(int target)
    {
        if (_main == null) return;
        switch (target)
        {
            case 0: CloseRooms(); OpenPage(0); break;
            case 1: CloseRooms(); OpenPage(1); break;
            case 2: CloseRooms(); OpenPage(2); break;
            case 3: _main.OpenAccountProfile(); break;
            case 4: _main.OpenEquipment("automatic"); break;
            case 5: _main.ToggleHelp(); break;
            case 6: GetTree().Quit(); break;
        }
    }
}
