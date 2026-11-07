using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Índice, búsqueda y artículos compartidos por el menú y la partida.</summary>
public partial class HelpPanel : Control
{
    private HelpArticle[] _articles = [];
    private VBoxContainer _body = null!;
    private ContentScrollContainer _scroll = null!;
    private LineEdit _search = null!;
    private HBoxContainer _searchRow = null!;
    private Button _back = null!, _clear = null!;
    private ElementTextLabel _title = null!;
    private readonly List<(GridContainer Grid, int Columns, float Minimum)> _grids = [];
    private HelpTopic? _topic;
    private SpellBook? _spellBook;
    private Main _main = null!;
    private Action _close = null!;
    private bool _inGame, _built;
    private string _match = "";

    internal SpellBook SpellBook => _spellBook ?? throw new InvalidOperationException("Abre Crear hechizos antes de usar el laboratorio.");
    internal float ContentWidthFraction => _body.Size.X / Math.Max(1, GetViewport().GetVisibleRect().Size.X);
    internal int SearchResultCount { get; private set; }

    internal void Build(Main main, bool inGame, Action close)
    {
        _main = main; _inGame = inGame; _close = close;
        Ui.Full(this);
        VisibilityChanged += EnsureBuilt;
        EnsureBuilt();
    }

    private void EnsureBuilt()
    {
        if (_built || !IsVisibleInTree()) return;
        _built = true;
        BuildContents();
    }

    private void BuildContents()
    {
        _articles = HelpCatalogue.Build(Ui.IsMobile);
        Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, Ui.IsMobile ? 10 : 20);
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var style = Ui.Panel(new("0c1c31"), 12, Ui.Blue);
        style.ContentMarginLeft = style.ContentMarginRight = Ui.IsMobile ? 12 : 20;
        style.ContentMarginTop = style.ContentMarginBottom = Ui.IsMobile ? 8 : 14;
        panel.AddThemeStyleboxOverride("panel", style); margin.AddChild(panel);
        var outer = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        outer.AddThemeConstantOverride("separation", 8); panel.AddChild(outer);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 10); outer.AddChild(header);
        _back = Ui.ExitButton("Índice", () => HandleBack(), "HelpBack"); header.AddChild(_back);
        _title = Ui.Label("Ayuda", 23, Ui.Gold); _title.ClipText = true;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill; _title.VerticalAlignment = VerticalAlignment.Center; header.AddChild(_title);
        header.AddChild(Ui.ExitButton(_inGame ? "Volver al juego" : "Cerrar", _close,
            _inGame ? "InGameHelpClose" : "HelpClose"));
        var searchRow = _searchRow = new HBoxContainer(); outer.AddChild(searchRow);
        _search = new LineEdit { Name = "HelpSearch", PlaceholderText = "Buscar: curación, escudo, oro…",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 44) };
        _search.AddThemeFontSizeOverride("font_size", 17); searchRow.AddChild(_search);
        foreach (string state in new[] { "normal", "focus" })
        {
            var fieldStyle = Ui.Panel(new("0c1626"), 7, state == "focus" ? Ui.Gold : Ui.TableBorder);
            fieldStyle.ContentMarginTop = fieldStyle.ContentMarginBottom = 6;
            _search.AddThemeStyleboxOverride(state, fieldStyle);
        }
        _clear = Ui.ExitButton("Limpiar", ClearSearch, "HelpClearSearch"); searchRow.AddChild(_clear);
        _search.TextChanged += _ => { _topic = null; Render(); };
        _scroll = new ContentScrollContainer { Name = "HelpContentScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        outer.AddChild(_scroll);
        _body = new VBoxContainer { Name = "HelpBody", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 12); _scroll.AddChild(_body);
        if (!_inGame) ArcanaMenuChrome.Attach(this, margin, _main, "Ayuda", 5, _main.NavigateOptionAMenu);
        Resized += UpdateLayout;
        Render();
    }

    internal void SetMatch(WorldSnapshot? snapshot)
    {
        _match = snapshot == null ? "" : MatchRules.Objective(snapshot.Mode, snapshot.ScoreLimit, snapshot.StartingTickets, snapshot.Map);
        if (_built && (_topic == null || _topic == HelpTopic.Modes)) Render();
    }

    internal void OpenTopic(HelpTopic topic, string? articleId = null)
    {
        _search.ReleaseFocus(); _topic = topic; Render(articleId);
    }

    internal bool HandleBack()
    {
        if (_topic != null) { _topic = null; Render(); return true; }
        if (_search.Text.Length > 0) { ClearSearch(); return true; }
        return false;
    }

    private void ClearSearch() { _search.Text = ""; _topic = null; Render(); }

    internal void SearchForVerification(string query)
    {
        _search.Text = query;
        _search.EmitSignal(LineEdit.SignalName.TextChanged, query);
    }
    internal void ScrollToEnd() => _scroll.ScrollVertical = int.MaxValue;

    // Mantiene los índices de las capturas de versiones anteriores.
    internal void SelectLegacyPage(int page) => OpenTopic(page switch
    {
        0 => HelpTopic.Controls, 1 => HelpTopic.Modes, 2 => HelpTopic.Wizards,
        3 => HelpTopic.Elements, 4 => HelpTopic.Relics, 5 => HelpTopic.Spells, _ => HelpTopic.Equipment
    });

    private void Render(string? expandedArticle = null)
    {
        _grids.Clear(); _spellBook = null;
        foreach (var child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        _scroll.ScrollVertical = 0;
        bool searching = !string.IsNullOrWhiteSpace(_search.Text);
        _clear.Visible = _search.Text.Length > 0;
        _back.Visible = _topic != null;
        _back.Text = searching ? "Resultados" : "Índice";
        SearchResultCount = 0;
        if (_topic is { } topic)
        {
            var section = HelpCatalogue.Sections.First(section => section.Id == topic);
            _title.Text = section.Title;
            Text(_body, section.Summary, Ui.Muted);
            if (topic == HelpTopic.Modes && _match.Length > 0) Note("Objetivo de esta partida", _match);
            if (topic == HelpTopic.Start && !_inGame) TutorialCard();
            if (topic == HelpTopic.Spells) BuildSpellLab();
            if (topic == HelpTopic.Elements) BuildElementKeys();
            if (topic == HelpTopic.Controls) BuildControls();
            var articles = _articles.Where(article => article.Topic == topic).ToArray();
            foreach (var article in articles)
            {
                if (article.Id == "stat-" + Enum.GetValues<EquipmentStat>()[0]) Text(_body, "Atributos", Ui.Gold, 21);
                if (article.Id == "affix-" + Enum.GetValues<ItemAffixKind>()[0]) Text(_body, "Efectos especiales", Ui.Gold, 21);
                Article(article, expandedArticle == null ? article == articles[0] : article.Id == expandedArticle);
            }
            if (topic == HelpTopic.Start) Link("Consultar los controles", HelpTopic.Controls);
            if (topic == HelpTopic.Elements) Link("Probar una combinación", HelpTopic.Spells);
        }
        else if (searching)
        {
            _title.Text = "Resultados";
            var matches = HelpCatalogue.Search(_articles, _search.Text).ToArray(); SearchResultCount = matches.Length;
            Text(_body, matches.Length == 0 ? "No hay resultados. Prueba con el nombre de un elemento, atributo o efecto."
                : $"{matches.Length} resultados · toca uno para ver su explicación", Ui.Muted);
            foreach (var article in matches)
            {
                string section = HelpCatalogue.Sections.First(section => section.Id == article.Topic).Title;
                var button = ActionCard(section + "  /  " + article.Title,
                    article.Body.Length > 130 ? article.Body[..130] + "…" : article.Body,
                    () => OpenTopic(article.Topic, article.Id));
                button.Name = "HelpResult_" + article.Id; _body.AddChild(button);
            }
        }
        else
        {
            _title.Text = "Ayuda";
            Text(_body, "Aprende a jugar o encuentra una respuesta", Ui.Text, 23);
            if (_inGame && _match.Length > 0) Note("Tu objetivo", _match);
            var topics = Grid(3, 285); topics.Name = "HelpTopicCards";
            foreach (var section in HelpCatalogue.Sections)
            {
                var button = ActionCard(section.Title + "  →", section.Summary, () => OpenTopic(section.Id));
                button.Name = "HelpTopic" + section.Id; topics.AddChild(button);
            }
            if (!_inGame) _body.AddChild(CompactButton("Abrir tutorial paso a paso", _main.OpenTutorial, "HelpTutorial"));
        }
        UpdateLayout();
        Callable.From(UpdateLayout).CallDeferred();
        if (expandedArticle != null) RevealArticle(expandedArticle);
    }

    private async void RevealArticle(string id)
    {
        var tree = GetTree();
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion()) return;
        if (_body.GetNodeOrNull<Control>("HelpArticle_" + id) is { } article)
            _scroll.ScrollVertical = (int)article.Position.Y;
    }

    private void TutorialCard()
    {
        var card = Card(Ui.Gold); _body.AddChild(card);
        var row = new VBoxContainer(); row.AddThemeConstantOverride("separation", 8); card.AddChild(row);
        Text(row, "¿Es tu primera partida?", Ui.Gold, 20);
        Text(row, "Empieza por el tutorial: movimiento, hechizos y equipo, paso a paso.");
        row.AddChild(CompactButton("Abrir tutorial", _main.OpenTutorial, "HelpTutorial"));
    }

    private void BuildSpellLab()
    {
        var card = Card(Ui.Blue); _body.AddChild(card);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); card.AddChild(box);
        Text(box, "Prueba tu hechizo", Ui.Gold, 21);
        _spellBook = new SpellBook { Name = "HelpSpellCombinations" }; box.AddChild(_spellBook);
    }

    private void BuildElementKeys()
    {
        var grid = Grid(8, 100);
        foreach (var element in Enum.GetValues<Element>()) grid.AddChild(Ui.ElementTile(element, compact: true));
    }

    private void BuildControls()
    {
        var table = Ui.Table(2, "HelpControlsTable"); _body.AddChild(table);
        Ui.TableCell(table, "Acción", header: true); Ui.TableCell(table, "Control", header: true);
        Ui.TableCell(table, "Moverse"); Ui.TableCell(table, Ui.IsMobile ? "Joystick izquierdo" : "Clic izquierdo / flechas");
        Ui.TableCell(table, "Lanzar"); Ui.TableCell(table, Ui.IsMobile ? "Joystick derecho" : "Mantener clic derecho");
        Ui.TableCell(table, "Sobre mí"); Ui.TableCell(table, Ui.IsMobile ? "Botón Sobre mí" : "Botón central");
        Ui.TableCell(table, "En área"); Ui.TableCell(table, Ui.IsMobile ? "Botón En área" : "Mayús + clic derecho");
    }

    private void Article(HelpArticle article, bool expanded)
    {
        var card = Card(); card.Name = "HelpArticle_" + article.Id; _body.AddChild(card);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); card.AddChild(box);
        var content = new VBoxContainer { Visible = expanded, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var button = CompactButton("", () => { content.Visible = !content.Visible; }, "HelpExpand_" + article.Id);
        void UpdateTitle() => button.Text = (content.Visible ? "−  " : "+  ") + article.Title;
        button.Alignment = HorizontalAlignment.Left;
        button.Pressed += UpdateTitle;
        UpdateTitle(); box.AddChild(button); box.AddChild(content);
        Text(content, article.Body);
    }

    private void Note(string title, string text)
    {
        var card = Card(Ui.Blue); _body.AddChild(card);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6); card.AddChild(box);
        Text(box, title, Ui.Blue, 19); Text(box, text);
    }

    private void Link(string title, HelpTopic topic) => _body.AddChild(CompactButton(title + "  →", () => OpenTopic(topic), "HelpLink" + topic));

    private GridContainer Grid(int columns, float minimum)
    {
        var grid = new GridContainer { Columns = columns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10); grid.AddThemeConstantOverride("v_separation", 10);
        _body.AddChild(grid); _grids.Add((grid, columns, minimum)); return grid;
    }

    private void UpdateLayout()
    {
        if (_body == null) return;
        // En paisaje corto el artículo tiene prioridad; Índice devuelve al buscador.
        _searchRow.Visible = _topic == null || GetViewport().GetVisibleRect().Size.Y > 520;
        float width = Math.Max(1, _scroll.Size.X - Ui.ScrollBarThickness);
        foreach (var (grid, columns, minimum) in _grids)
            grid.Columns = ResponsiveLayout.Columns(width, minimum, columns, 10);
    }

    private static Button CompactButton(string text, Action action, string name)
    {
        var button = Ui.ExitButton(text, action, name); button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        button.AutowrapMode = TextServer.AutowrapMode.WordSmart; button.CustomMinimumSize = new(0, 48);
        button.AddThemeFontSizeOverride("font_size", 17); return button;
    }

    private static PanelContainer ActionCard(string title, string summary, Action action)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        var button = CompactButton("", action, "HelpOpen"); button.TooltipText = title;
        button.CustomMinimumSize = new(0, 104); card.AddChild(button);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore }; card.AddChild(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 14);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; box.AddThemeConstantOverride("separation", 7); margin.AddChild(box);
        Text(box, title, Ui.Gold, 19); Text(box, summary, Ui.Muted, 16);
        return card;
    }

    private static PanelContainer Card(Color? border = null)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = Ui.Panel(new("122941"), 9, border ?? new Color("365d7c"));
        style.ContentMarginLeft = style.ContentMarginRight = 12; style.ContentMarginTop = style.ContentMarginBottom = 10;
        panel.AddThemeStyleboxOverride("panel", style); return panel;
    }

    private static void Text(Node parent, string text, Color? color = null, int size = 17)
    {
        var label = Ui.Label(text, size, color); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.MouseFilter = MouseFilterEnum.Ignore; label.SizeFlagsHorizontal = SizeFlags.ExpandFill; parent.AddChild(label);
    }
}
