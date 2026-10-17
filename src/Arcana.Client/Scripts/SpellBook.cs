using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Selección directa de hasta tres elementos y una forma de lanzamiento.</summary>
public partial class SpellBook : VBoxContainer
{
    private readonly ElementBuffer _selection = new();
    private readonly ElementIcon[] _slots = new ElementIcon[3];
    private readonly Button[] _modes = new Button[3];
    private Label _title = null!, _description = null!, _damage = null!;
    private GridContainer _elements = null!;
    private GridContainer _columns = null!;
    private SubViewportContainer _preview = null!;
    private ArenaRenderer _arena = null!;
    private CastMode _mode;
    private DemonstrationKind _kind;
    private SpellDemonstration _demo = null!;
    private WorldSnapshot _snapshot = null!;
    private double _accumulator;
    public SpellDemonstration Demo => _demo;
    internal ArenaRenderer Preview => _arena;
    internal bool FixedStepForVerification { get; set; }
    public bool ShowPreview { get; set; } = true;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 9);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var hint = Ui.Label("Pulsa hasta tres elementos. Puedes repetirlos; los opuestos se anulan.", 16, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; hint.Visible = ShowPreview; AddChild(hint);
        _columns = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _columns.AddThemeConstantOverride("h_separation", 14); _columns.AddThemeConstantOverride("v_separation", 9); AddChild(_columns);
        var choices = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        choices.AddThemeConstantOverride("separation", 4); _columns.AddChild(choices);
        var result = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        result.AddThemeConstantOverride("separation", 9); _columns.AddChild(result);
        _elements = new GridContainer { Columns = 4, Name = "SpellElementChoices", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _elements.AddThemeConstantOverride("h_separation", 6); _elements.AddThemeConstantOverride("v_separation", 6); choices.AddChild(_elements);
        foreach (var element in Enum.GetValues<Element>())
        {
            var selected = element;
            var button = Ui.Button(Ui.ElementName(element), () => { _selection.Add(selected); _kind = DemonstrationKind.Combination; Restart(); });
            button.Name = "SpellElement" + element; button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CompactButton(button); button.AddThemeColorOverride("font_color", Ui.ElementColor(element));
            _elements.AddChild(button);
        }
        var selection = new HBoxContainer(); selection.AddThemeConstantOverride("separation", 8); choices.AddChild(selection);
        for (int i = 0; i < _slots.Length; i++)
        {
            var panel = new PanelContainer { CustomMinimumSize = new(36, 36) };
            var style = Ui.Panel(new("09182a"), 5, Ui.TableBorder);
            style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = style.ContentMarginBottom = 4;
            panel.AddThemeStyleboxOverride("panel", style); selection.AddChild(panel);
            panel.AddChild(_slots[i] = new ElementIcon());
        }
        var clear = Ui.Button("Limpiar", () => { _selection.Clear(); _kind = DemonstrationKind.Combination; Restart(); });
        clear.Name = "SpellClear"; CompactButton(clear); selection.AddChild(clear);
        var modes = new HBoxContainer(); modes.AddThemeConstantOverride("separation", 8); choices.AddChild(modes);
        string[] names = ["Frontal", "Sobre mí", "En área"];
        for (int i = 0; i < _modes.Length; i++)
        {
            int mode = i;
            var button = Ui.Button(names[i], () => { _mode = (CastMode)mode; _kind = DemonstrationKind.Combination; Restart(); });
            button.Name = "SpellCastMode" + i; button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            CompactButton(button);
            modes.AddChild(button); _modes[i] = button;
        }
        _title = Ui.Label("", 18, Ui.Gold); _title.Name = "SpellCombinationTitle";
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart; result.AddChild(_title);
        _description = Ui.Label("", 15); _description.AutowrapMode = TextServer.AutowrapMode.WordSmart; result.AddChild(_description);
        _damage = Ui.Label("", 14, Ui.Gold); _damage.Name = "SpellDamageValues";
        _damage.AutowrapMode = TextServer.AutowrapMode.WordSmart; result.AddChild(_damage);
        _preview = new SubViewportContainer { Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new(0, Ui.IsMobile ? 170 : 210) };
        AddChild(_preview);
        if (ShowPreview)
        {
            var viewport = new SubViewport { Size = new(640, 230), OwnWorld3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible, HandleInputLocally = false };
            _preview.AddChild(viewport);
            _arena = new ArenaRenderer { AudioEnabled = false, ShowControlPoints = false }; viewport.AddChild(_arena);
            _arena.SetPreviewZoom(12);
        }
        var note = Ui.Label("Cifras base sin equipo ni protección. La demostración se repite sola.", 12, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; note.Visible = ShowPreview; AddChild(note);
        Resized += UpdateLayout; UpdateLayout(); Restart();
    }

    private void UpdateLayout()
    {
        if (_columns != null) _columns.Columns = Size.X < 540 ? 1 : 2;
    }

    private static void CompactButton(Button button)
    {
        button.CustomMinimumSize = new(0, 44); button.AddThemeFontSizeOverride("font_size", 15);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
            if (button.GetThemeStylebox(state) is StyleBoxFlat current)
            {
                var style = (StyleBoxFlat)current.Duplicate();
                style.ContentMarginLeft = style.ContentMarginRight = 6;
                style.ContentMarginTop = style.ContentMarginBottom = 4;
                button.AddThemeStyleboxOverride(state, style);
            }
    }

    public void SelectCombination(string keys, CastMode mode = CastMode.Forward, DemonstrationKind kind = DemonstrationKind.Combination)
    {
        _selection.Clear();
        foreach (var element in (SpellCatalogue.FromKeys(keys) ?? SpellCatalogue.All[0]).Elements) _selection.Add(element);
        _mode = mode; _kind = kind; Restart();
    }

    public void Restart()
    {
        _accumulator = 0;
        var elements = _selection.Elements.ToArray(); var recipe = new SpellRecipe(elements);
        for (int i = 0; i < _slots.Length; i++) _slots[i].Value = i < elements.Length ? elements[i] : null;
        for (int i = 0; i < _modes.Length; i++)
            _modes[i].AddThemeColorOverride("font_color", i == (int)_mode ? Ui.Gold : Ui.Text);
        _demo = new(elements, _mode, _kind); _snapshot = _demo.World.Snapshot();
        if (_arena != null) { _arena.Clear(); _arena.ShowControlPoints = _kind == DemonstrationKind.Capture; }
        bool chosen = elements.Length > 0 || _kind != DemonstrationKind.Combination;
        _preview.Visible = chosen && ShowPreview; _damage.Visible = chosen && _kind == DemonstrationKind.Combination;
        _damage.Text = _kind == DemonstrationKind.Combination ? SpellBalance.Format(recipe, _mode) : "";
        _title.Text = !chosen ? "Elige hasta tres elementos" : _kind switch
        {
            DemonstrationKind.CrossedBeams => "CRUCE DE RAYOS",
            DemonstrationKind.ShieldRefraction => "REFRACCIÓN DEL ESCUDO",
            DemonstrationKind.Capture => "CAPTURA DE PUNTO", _ => SpellCatalogue.Name(elements)
        };
        _description.Text = !chosen ? "Puedes repetir elementos; los opuestos se anulan. Aquí verás el efecto y sus cifras." : _kind switch
        {
            DemonstrationKind.CrossedBeams => "Los rayos suman sus efectos desde el cruce y siguen la bisectriz.",
            DemonstrationKind.ShieldRefraction => "El escudo refleja el rayo y la guarda añade sus elementos.",
            DemonstrationKind.Capture => "Con ambos equipos dentro, la captura se detiene.",
            _ => SpellCatalogue.Describe(recipe, _mode)
        };
    }

    public override void _Process(double delta)
    {
        if (_arena == null || !IsVisibleInTree() || !_preview.Visible || _demo == null) return;
        _accumulator += FixedStepForVerification ? 1 / 60.0 : Math.Clamp(delta, 0, 0.1);
        bool advanced = false;
        while (_accumulator >= 1 / 60.0)
        {
            int cycle = _demo.Cycle; _demo.Step();
            if (_demo.Cycle != cycle) _arena.Clear();
            _accumulator -= 1 / 60.0; advanced = true;
        }
        if (advanced) _snapshot = _demo.World.Snapshot();
        _arena.Render(_snapshot, _demo.LocalId, FixedStepForVerification ? 1 / 60.0 : delta, true);
    }
}
