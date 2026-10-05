using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class SpellBook : VBoxContainer
{
    private ItemList _list = null!;
    private LineEdit _search = null!;
    private Label _title = null!, _description = null!, _clock = null!, _damage = null!;
    private OptionButton _mode = null!, _scenario = null!;
    private ArenaRenderer _arena = null!;
    private ProgressBar _timeline = null!;
    private Button _pause = null!;
    private readonly List<SpellCombination> _shown = [];
    private SpellCombination _combination = SpellCatalogue.All[0];
    private SpellDemonstration _demo = null!;
    private WorldSnapshot _snapshot = null!;
    private bool _paused;
    private double _accumulator;
    public SpellDemonstration Demo => _demo;
    internal ArenaRenderer Preview => _arena;
    internal bool FixedStepForVerification { get; set; }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 9);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 12); AddChild(top);
        _scenario = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string text in new[] { "Combinaciones", "Cruce de rayos", "Escudo prismático", "Captura de punto" }) _scenario.AddItem(text);
        top.AddChild(_scenario);
        _mode = new OptionButton();
        foreach (string text in Ui.IsMobile ? new[] { "Frontal", "Sobre ti", "Área" } : new[] { "Frontal · derecho", "Sobre ti · central", "Pulso · Mayús + derecho" }) _mode.AddItem(text);
        top.AddChild(_mode);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); AddChild(row);
        var choices = new VBoxContainer { CustomMinimumSize = new(188, 0) }; row.AddChild(choices);
        _search = new LineEdit { PlaceholderText = "EW, fuego…", Name = "SpellSearch", MaxLength = 64 }; choices.AddChild(_search);
        _list = new ItemList { CustomMinimumSize = new(188, 310), SizeFlagsVertical = SizeFlags.ExpandFill, Name = "SpellCombinations" };
        choices.AddChild(_list);
        choices.AddChild(Ui.Label($"{SpellCatalogue.All.Length} combinaciones válidas", 13, Ui.Muted));
        var view = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddChild(view);
        _title = Ui.Label("", 18, Ui.Gold); view.AddChild(_title);
        _damage = Ui.Label("", 14, Ui.Gold); _damage.Name = "SpellDamageValues";
        _damage.AutowrapMode = TextServer.AutowrapMode.WordSmart; view.AddChild(_damage);
        var container = new SubViewportContainer { CustomMinimumSize = new(640, 230), Stretch = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        view.AddChild(container);
        var viewport = new SubViewport { Size = new(640, 230), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenParentVisible, HandleInputLocally = false };
        container.AddChild(viewport);
        _arena = new ArenaRenderer { AudioEnabled = false, ShowControlPoints = false }; viewport.AddChild(_arena);
        _arena.SetPreviewZoom(12);
        _description = Ui.Label("", 14); _description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.CustomMinimumSize = new(640, 55); view.AddChild(_description);
        var controls = new HBoxContainer(); controls.AddThemeConstantOverride("separation", 12); AddChild(controls);
        _pause = Ui.Button("Pausar", () => { _paused = !_paused; _pause.Text = _paused ? "Reproducir" : "Pausar"; });
        _pause.CustomMinimumSize = new(110, 34); controls.AddChild(_pause);
        var replay = Ui.Button("Repetir", Restart); replay.CustomMinimumSize = new(100, 34); controls.AddChild(replay);
        _timeline = new ProgressBar { ShowPercentage = false, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 8) }; controls.AddChild(_timeline);
        _clock = Ui.Label("", 13, Ui.Muted); controls.AddChild(_clock);
        AddChild(Ui.Label("Cifras base: magos equilibrados, objetivo seco y sin protección. Afinidades, escudos y objetos modifican el resultado.", 12, Ui.Muted));
        _list.ItemSelected += SelectRow;
        _search.TextChanged += _ => Refresh();
        _mode.ItemSelected += _ => Restart();
        _scenario.ItemSelected += _ => Restart();
        Refresh();
    }
    private void Refresh()
    {
        string query = _search.Text.Trim().ToUpperInvariant();
        bool keys = query.Length > 0 && query.All(c => SpellCatalogue.ElementKeys.Contains(c));
        var required = keys ? query.GroupBy(c => c).Select(g => (Key: g.Key, Count: g.Count())).ToArray() : [];
        _list.Clear(); _shown.Clear();
        foreach (var combination in SpellCatalogue.All)
        {
            bool match = query.Length == 0 || keys && required.All(r => combination.Keys.Count(k => k == r.Key) >= r.Count) ||
                SpellCatalogue.Name(combination.Elements).ToUpperInvariant().Contains(query);
            if (!match) continue;
            _shown.Add(combination);
            _list.AddItem(combination.Keys + " · " + SpellCatalogue.Name(combination.Elements));
            _list.SetItemTooltip(_shown.Count - 1, SpellCatalogue.Name(combination.Elements) + "\n" + SpellBalance.Format(combination.Recipe, (CastMode)_mode.Selected));
        }
        if (_shown.Count > 0) { _list.Select(0); SelectRow(0); }
    }
    private void SelectRow(long index)
    {
        _combination = _shown[(int)index]; _scenario.Select(0); Restart();
    }
    public void SelectCombination(string keys, CastMode mode = CastMode.Forward, DemonstrationKind kind = DemonstrationKind.Combination)
    {
        var selected = SpellCatalogue.FromKeys(keys) ?? SpellCatalogue.All[0];
        _search.Text = "";
        _combination = selected;
        int index = _shown.FindIndex(c => c.Keys == selected.Keys);
        if (index >= 0) { _list.Select(index); _list.EnsureCurrentIsVisible(); }
        _mode.Select((int)mode); _scenario.Select((int)kind); Restart();
    }
    public void Restart()
    {
        _paused = false; _pause.Text = "Pausar"; _accumulator = 0;
        var mode = (CastMode)_mode.Selected; var kind = (DemonstrationKind)_scenario.Selected;
        _demo = new(_combination.Elements, mode, kind);
        _snapshot = _demo.World.Snapshot();
        _arena.Clear(); _arena.ShowControlPoints = kind == DemonstrationKind.Capture;
        _mode.Disabled = kind != DemonstrationKind.Combination;
        _damage.Text = kind == DemonstrationKind.Combination ? SpellBalance.Format(_combination.Recipe, mode) : "";
        _damage.Visible = kind == DemonstrationKind.Combination;
        _damage.TooltipText = "Valores base contra un mago equilibrado, seco y sin protección. Afinidades, guardas, escudos y objetos modifican el resultado. La curación respeta la vida máxima del objetivo.";
        for (int i = 0; i < _shown.Count; i++) _list.SetItemTooltip(i, SpellCatalogue.Name(_shown[i].Elements) + "\n" + SpellBalance.Format(_shown[i].Recipe, mode));
        _title.Text = kind switch { DemonstrationKind.CrossedBeams => "SSF + SSR · CRUCE DE RAYOS",
            DemonstrationKind.ShieldRefraction => "SS CONTRA EF · REFRACCIÓN",
            DemonstrationKind.Capture => "CAPTURA · AZUL Y ROJO", _ => _combination.Keys + " · " + SpellCatalogue.Name(_combination.Elements) };
        _description.Text = kind switch
        {
            DemonstrationKind.CrossedBeams => "Los rayos se detienen en el cruce. El tramo de salida suma sus efectos y sigue la bisectriz; si llegan de frente, se desvía 90°.",
            DemonstrationKind.ShieldRefraction => "Un escudo devuelve el 80 % de la intensidad. Una guarda añade sus elementos y desvía 20° más el tramo reflejado. Máximo dos rebotes.",
            DemonstrationKind.Capture => "El color ocupa el círculo según la captura. El rojo primero retira el progreso azul. Con los dos equipos dentro, la captura se detiene.",
            _ => SpellCatalogue.Describe(_combination.Recipe, mode)
        };
        _timeline.MaxValue = _demo.Duration;
    }
    public override void _Process(double delta)
    {
        if (!IsVisibleInTree() || _demo == null) return;
        if (!_paused)
        {
            _accumulator += FixedStepForVerification ? 1 / 60.0 : Math.Clamp(delta, 0, 0.1);
            bool advanced = false;
            while (_accumulator >= 1 / 60.0)
            {
                int cycle = _demo.Cycle; _demo.Step();
                if (_demo.Cycle != cycle) _arena.Clear();
                _accumulator -= 1 / 60.0; advanced = true;
            }
            if (advanced) _snapshot = _demo.World.Snapshot();
        }
        _arena.Render(_snapshot, _demo.LocalId, _paused ? 0 : FixedStepForVerification ? 1 / 60.0 : delta, true);
        _timeline.Value = _demo.Time;
        _clock.Text = $"{_demo.Time:0.0} / {_demo.Duration:0} s · bucle";
    }
}
