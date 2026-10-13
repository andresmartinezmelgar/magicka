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
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", Ui.IsMobile ? 12 : 22);
        outer.AddChild(row);

        var topics = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.53f };
        topics.AddThemeConstantOverride("separation", 7);
        row.AddChild(topics);
        for (int i = 0; i < _topics.Length; i++)
        {
            int target = i;
            var button = Ui.Button((i + 1) + ".  " + _topics[i], () => Select(target));
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.CustomMinimumSize = new(0, Ui.IsMobile ? 56 : 48);
            topics.AddChild(button);
            _topicButtons.Add(button);
        }
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2.1f, SizeFlagsVertical = SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("10233c"), 8, new Color("285b80")));
        row.AddChild(card);
        var contents = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        contents.AddThemeConstantOverride("separation", 8);
        card.AddChild(contents);
        _topic = Ui.Label("", 23, Ui.Gold); contents.AddChild(_topic);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        contents.AddChild(_body);
        ArcanaMenuChrome.Attach(this, margin, main, "Cómo jugar", 5, main.NavigateOptionAMenu);
        Select(0);
    }

    private void Select(int page)
    {
        _page = page;
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
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 20);
        grid.AddThemeConstantOverride("v_separation", Ui.IsMobile ? 7 : 12);
        _body.AddChild(grid);
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            grid.AddChild(Ui.Label(rows[i, 0], 15, Ui.Gold));
            var label = Ui.Label(rows[i, 1], 15, Ui.Text);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            grid.AddChild(label);
        }
        _body.AddChild(Ui.Label("FFF: fuego  ·  E: escudo  ·  QF: vapor  ·  QR: hielo  ·  DDF: roca incendiaria", 15, Ui.Muted));
    }

    private void ObjectivesPage()
    {
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 12);
        _body.AddChild(cards);
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
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        foreach (var wizard in Enum.GetValues<WizardType>())
        {
            var panel = SmallCard();
            grid.AddChild(panel);
            var box = new VBoxContainer(); panel.AddChild(box);
            box.AddChild(Ui.Label(WizardTypes.Name(wizard), 17, Ui.WizardColor(wizard)));
            Wrapped(box, WizardTypes.Description(wizard), Ui.Text);
        }
    }

    private void ElementPage()
    {
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        foreach (var element in Enum.GetValues<Element>())
        {
            var panel = SmallCard();
            grid.AddChild(panel);
            var box = new VBoxContainer(); panel.AddChild(box);
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
        var controls = new HBoxContainer();
        _body.AddChild(controls);
        var previous = Ui.Button("◀  Anteriores", () => { _relicOffset = Math.Max(0, _relicOffset - 6); Select(4); });
        previous.Disabled = _relicOffset == 0;
        controls.AddChild(previous);
        controls.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        controls.AddChild(Ui.Label($"{_relicOffset + 1}–{Math.Min(values.Length, _relicOffset + 6)} de {values.Length}", 15, Ui.Muted));
        var next = Ui.Button("Siguientes  ▶", () => { _relicOffset = Math.Min(maxOffset, _relicOffset + 6); Select(4); });
        next.Disabled = _relicOffset + 6 >= values.Length;
        controls.AddChild(next);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        _body.AddChild(grid);
        foreach (var buff in values.Skip(_relicOffset).Take(6))
        {
            var panel = SmallCard(); grid.AddChild(panel);
            var box = new VBoxContainer(); panel.AddChild(box);
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
        var grid = new GridContainer { Columns = 4 };
        _body.AddChild(grid);
        foreach (var element in Enum.GetValues<Element>()) grid.AddChild(Ui.ElementTile(element, true));
    }

    private static PanelContainer SmallCard()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new Color("132c46"), 8, new Color("31658d")));
        return panel;
    }

    private static void Wrapped(Node parent, string body, Color? color = null)
    {
        var label = Ui.Label(body, 15, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        parent.AddChild(label);
    }
}
