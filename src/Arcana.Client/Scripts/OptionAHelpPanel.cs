using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Ayuda desde los menús en opción A; la ayuda utilizada durante las partidas permanece intacta.</summary>
public partial class OptionAHelpPanel : Control
{
    private Main _main = null!;
    private VBoxContainer _body = null!;
    private Label _topic = null!;
    private readonly List<Button> _topicButtons = [];
    private GridContainer? _topicNavigation;
    private readonly List<(GridContainer Grid, int MaxColumns, float MinimumWidth)> _adaptiveGrids = [];
    private int _page;
    private int _relicOffset;

    private readonly string[] _topics =
    [
        "Controles", "Cómo ganar", "Tipos de mago", "Elementos", "Objetos y reliquias", "Combinaciones"
    ];

    public void Build(Main main)
    {
        _main = main;
        Theme = Ui.Theme();
        Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop };
        AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 10 : 22);

        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("0c1c31"), 10, new Color("2a78a4")));
        margin.AddChild(panel);
        var outer = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddThemeConstantOverride("separation", Ui.IsMobile ? 8 : 14);
        panel.AddChild(outer);
        var header = Ui.Label("CÓMO JUGAR  /  GUÍA DEL MAGO", 24, Ui.Gold);
        outer.AddChild(header);
        // Las seis secciones ocupan una banda horizontal: el contenido usa todo el ancho.
        var navigation = new GridContainer { Name = "HelpTopics", Columns = 6,
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        navigation.AddThemeConstantOverride("h_separation", Ui.IsMobile ? 6 : 10);
        navigation.AddThemeConstantOverride("v_separation", 6);
        outer.AddChild(navigation);
        _topicNavigation = navigation;
        for (int i = 0; i < _topics.Length; i++)
        {
            int target = i;
            var button = Ui.Button(_topics[i], () => Select(target));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new(0, Ui.IsMobile ? 54 : 46);
            button.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            button.AddThemeFontSizeOverride("font_size", Ui.IsMobile ? 17 : 16);
            navigation.AddChild(button);
            _topicButtons.Add(button);
        }
        var card = new PanelContainer { Name = "HelpContentCard",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10233c"), 8, new Color("285b80")));
        outer.AddChild(card);
        var contents = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        contents.AddThemeConstantOverride("separation", 8);
        card.AddChild(contents);
        _topic = Ui.Label("", Ui.IsMobile ? 22 : 24, Ui.Gold);
        contents.AddChild(_topic);
        var bodyScroll = new ScrollContainer { Name = "HelpContentScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        contents.AddChild(bodyScroll);
        _body = new VBoxContainer { Name = "HelpFullWidthBody",
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        bodyScroll.AddChild(_body);
        ArcanaMenuChrome.Attach(this, margin, main, "Cómo jugar", 5, main.NavigateOptionAMenu);
        GetViewport().SizeChanged += UpdateResponsiveHelp;
        Select(0);
        UpdateResponsiveHelp();
    }

    public override void _ExitTree() => GetViewport().SizeChanged -= UpdateResponsiveHelp;

    internal void SelectForSmoke(int page) => Select(page);
    internal float ContentWidthFraction => _body.Size.X / Math.Max(1f, GetViewport().GetVisibleRect().Size.X);

    private void Select(int page)
    {
        _page = page;
        _adaptiveGrids.Clear();
        _topic.Text = _topics[page].ToUpperInvariant();
        foreach (Node child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        for (int i = 0; i < _topicButtons.Count; i++)
            _topicButtons[i].AddThemeStyleboxOverride("normal",
                Ui.Panel(new Color(i == page ? "1b4d72" : "132a43"), 7,
                    i == page ? Ui.Blue : new Color("2a5071")));
        switch (page)
        {
            case 0: ControlsPage(); break;
            case 1: ObjectivesPage(); break;
            case 2: WizardPage(); break;
            case 3: ElementPage(); break;
            case 4: RelicPage(); break;
            case 5: SpellPage(); break;
        }
        UpdateResponsiveHelp();
    }

    private void AddAdaptiveGrid(GridContainer grid, int maximumColumns, float minimumWidth)
    {
        _adaptiveGrids.Add((grid, maximumColumns, minimumWidth));
    }

    private void UpdateResponsiveHelp()
    {
        float width = Math.Max(260, GetViewport().GetVisibleRect().Size.X - (Ui.IsMobile ? 60 : 105));
        if (_topicNavigation != null)
            _topicNavigation.Columns = ResponsiveLayout.Columns(width, 146, _topics.Length, 10);
        foreach (var (grid, maximumColumns, minimumWidth) in _adaptiveGrids)
            if (GodotObject.IsInstanceValid(grid) && !grid.IsQueuedForDeletion())
                grid.Columns = ResponsiveLayout.Columns(width, minimumWidth, maximumColumns, 12);
    }

    private void ControlsPage()
    {
        string[,] rows = Ui.IsMobile ? new string[,]
        {
            { "Moverse", "Desliza el joystick izquierdo" },
            { "Apuntar", "Usa el joystick derecho" },
            { "Magia", "Combina hasta 3 elementos" },
            { "Lanzar", "Arrastra y suelta el joystick derecho" },
            { "Sobre ti", "Curar, escudo o secar" },
            { "Área", "Hechizos de alrededor" },
            { "Especiales", "Cuatro magias equipadas" },
            { "Ayuda", "Ayuda, tabla y menú" }
        } : new string[,]
        {
            { "Q W E R / A S D F", "Agua, vida, escudo, frío / rayo, arcano, tierra, fuego" },
            { "Clic izquierdo", "Caminar hacia el cursor" },
            { "Clic derecho", "Lanzar magia; soltar interrumpe" },
            { "Botón central", "Lanzar magia sobre ti" },
            { "Mayús + derecho", "Magia en área alrededor" },
            { "Mayús + izquierdo", "Golpe de bastón" },
            { "1 / 2 / 3 / 4", "Magias especiales" },
            { "TAB / F1 / ESC", "Clasificación, ayuda y menú" }
        };
        var list = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("h_separation", Ui.IsMobile ? 12 : 20);
        list.AddThemeConstantOverride("v_separation", Ui.IsMobile ? 4 : 8);
        _body.AddChild(list);
        AddAdaptiveGrid(list, 2, 310);
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            // Cada pareja clave/explicación ocupa dos columnas o dos filas en pantallas estrechas.
            var key = Ui.Label(rows[i, 0], Ui.IsMobile ? 16 : 18, Ui.Gold);
            key.CustomMinimumSize = Vector2.Zero;
            key.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            key.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            list.AddChild(key);
            var description = Ui.Label(rows[i, 1], Ui.IsMobile ? 16 : 18, Ui.Text);
            description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            list.AddChild(description);
        }
        var note = Ui.Label("FFF: fuego  ·  E: escudo  ·  QF: vapor  ·  QR: hielo  ·  DDF: roca incendiaria", 15, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.AddChild(note);
    }

    private void ObjectivesPage()
    {
        var cards = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 12);
        _body.AddChild(cards);
        AddAdaptiveGrid(cards, 3, 290);
        foreach (var mode in new[] { GameMode.Warfare, GameMode.Duel, GameMode.Arena })
        {
            var panel = SmallCard();
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            cards.AddChild(panel);
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 14);
            panel.AddChild(box);
            box.AddChild(Ui.Label(Ui.ModeName(mode).ToUpperInvariant(), 18, Ui.Gold));
            Wrapped(box, MatchRules.Objective(mode, 15, 75));
            Wrapped(box, MatchRules.Details(mode), Ui.Muted);
        }
    }

    private void WizardPage()
    {
        var grid = new GridContainer { Columns = 5,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        AddAdaptiveGrid(grid, 5, 215);
        foreach (var wizard in Enum.GetValues<WizardType>())
        {
            var panel = SmallCard();
            grid.AddChild(panel);
            var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; panel.AddChild(box);
            box.AddChild(Ui.Label(WizardTypes.Name(wizard), 17, Ui.WizardColor(wizard)));
            Wrapped(box, WizardTypes.Description(wizard), Ui.Text);
        }
    }

    private void ElementPage()
    {
        var grid = new GridContainer { Columns = 4,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        AddAdaptiveGrid(grid, 4, 205);
        foreach (var element in Enum.GetValues<Element>())
        {
            var panel = SmallCard();
            grid.AddChild(panel);
            var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; panel.AddChild(box);
            box.AddChild(Ui.Label(Ui.ElementName(element).ToUpperInvariant(), 18, Ui.ElementColor(element)));
            Wrapped(box, "Supera a: " + Ui.ElementName(ElementInteractions.StrongAgainst(element)));
            Wrapped(box, "Débil ante: " + Ui.ElementName(ElementInteractions.Weakness(element)), Ui.Muted);
        }
    }

    private void RelicPage()
    {
        var values = Enum.GetValues<BuffKind>();
        int maxOffset = Math.Max(0, values.Length - 6);
        _relicOffset = Math.Clamp(_relicOffset, 0, maxOffset);
        var controls = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddChild(controls);
        var previous = Ui.Button("◀  Anteriores", () => { _relicOffset = Math.Max(0, _relicOffset - 6); Select(4); });
        previous.Disabled = _relicOffset == 0;
        controls.AddChild(previous);
        controls.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        controls.AddChild(Ui.Label($"{_relicOffset + 1}–{Math.Min(values.Length, _relicOffset + 6)} de {values.Length}", 15, Ui.Muted));
        var next = Ui.Button("Siguientes  ▶", () => { _relicOffset = Math.Min(maxOffset, _relicOffset + 6); Select(4); });
        next.Disabled = _relicOffset + 6 >= values.Length;
        controls.AddChild(next);
        var grid = new GridContainer { Columns = 3,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        AddAdaptiveGrid(grid, 3, 260);
        foreach (var buff in values.Skip(_relicOffset).Take(6))
        {
            var panel = SmallCard(); grid.AddChild(panel);
            var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; panel.AddChild(box);
            box.AddChild(Ui.Label(RelicBuffs.Name(buff), 17, RelicVisual.ColorFor(buff)));
            Wrapped(box, RelicBuffs.Description(buff));
        }
    }

    private void SpellPage()
    {
        Wrapped(_body, "Mezcla hasta tres elementos, incluidos repetidos. Cada combinación puede lanzar un hechizo distinto. Consulta la demostración completa en el libro de hechizos.");
        var button = Ui.Button("Abrir libro interactivo de hechizos", () => _main.OpenOriginalSpellBookFromMenu(), true);
        button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _body.AddChild(button);
        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddChild(grid);
        AddAdaptiveGrid(grid, 4, 105);
        foreach (var element in Enum.GetValues<Element>()) grid.AddChild(Ui.ElementTile(element, true));
    }

    private static PanelContainer SmallCard()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        var style = Ui.Panel(new Color("132c46"), 8, new Color("31658d"));
        style.ContentMarginLeft = style.ContentMarginRight = Ui.IsMobile ? 15 : 24;
        style.ContentMarginTop = style.ContentMarginBottom = Ui.IsMobile ? 10 : 20;
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static void Wrapped(Node parent, string body, Color? color = null)
    {
        var label = Ui.Label(body, Ui.IsMobile ? 16 : 18, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
    }
}
