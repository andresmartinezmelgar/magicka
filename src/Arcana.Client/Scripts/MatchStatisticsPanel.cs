using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

/// <summary>Gráficas sobre los informes anónimos de Firestore, agrupados por partida.</summary>
public partial class MatchStatisticsPanel : Control
{
    private Main _main = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, MatchStatisticsReport> _reports = new(StringComparer.Ordinal);
    private VBoxContainer _body = null!;
    private ElementTextLabel _status = null!;
    private Button _more = null!, _refresh = null!, _deleteHistory = null!;
    private string _nextPageToken = "";
    private int _loadedPages;
    private readonly HashSet<string> _pageTokens = new(StringComparer.Ordinal);
    private GridContainer _filters = null!;
    private ContentScrollContainer _scroll = null!;
    private OptionButton _mode = null!, _period = null!, _version = null!, _view = null!, _source = null!, _scale = null!;
    private int _damageElement = (int)Element.Fire;
    private bool? Logarithmic => _scale.Selected switch { 1 => false, 2 => true, _ => null };
    private CheckBox _unfinished = null!;
    private bool _loading;
    private int _tableIndex;
    private readonly Dictionary<(StatisticGroup, int, StatisticMeasure), double> _totals = [];
    private static readonly string[] Elements = ["Agua", "Vida", "Escudo", "Frío", "Rayo", "Arcano", "Tierra", "Fuego", "Físico"];
    private static readonly string[] Attacks = ["Haz", "Relámpago", "Cono", "Roca", "Fragmentos", "Empuje", "Barrera", "Cuerpo a cuerpo", "Sobre uno mismo", "Nova", "Mina", "Meteorito", "Prisa", "Teletransporte", "Reanimar", "Aura de fuego", "Aura de rayo", "Aura de regeneración", "Meteorito pasivo", "Pulso glacial", "Cadena de rayos", "Quemadura", "Golpe enemigo", "Área enemiga", "Bombardeo rúnico", "Regeneración", "Escudo del anillo", "Acción del anillo", "Escudo vital", "Reflejo de escudo", "Aura arcana", "Aura de agua", "Marca arcana", "Eco arcano"];
    public void Build(Main main, bool verification = false)
    {
        _main = main; Name = "MatchStatisticsPanel"; Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        var layout = new VBoxContainer(); layout.AddThemeConstantOverride("separation", 10); margin.AddChild(layout);
        var header = new HBoxContainer(); layout.AddChild(header);
        var title = Ui.Label("ESTADÍSTICAS DE PARTIDAS", 24, Ui.Gold);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        header.AddChild(Ui.ExitButton("Volver", () => main.NavigateBack(), "StatisticsBack"));
        var scroll = new ContentScrollContainer { Name = "StatisticsScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; layout.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        _scroll = scroll;
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 12); scroll.AddChild(content);
        var explanation = Ui.Label(verification ? "Datos de prueba de QA · consulta paginada simulada" : "Estadísticas globales de Firebase · informes anónimos de todos los dispositivos", 16, Ui.Muted); explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(explanation);
        _filters = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(_filters);
        _filters.AddThemeConstantOverride("h_separation", 10); _filters.AddThemeConstantOverride("v_separation", 8);
        _mode = Select("Modo", ["Todos los modos", "Duelo", "Arena", "Guerra", "Supervivencia"]);
        _period = Select("Periodo", ["Todo el periodo", "Últimos 7 días", "Últimos 30 días", "Últimos 90 días"]);
        _version = Select("Versión", ["Todas las versiones"]);
        _view = Select("Vista", ["Resumen", "Elementos", "Monstruos", "Hechizos", "Progresión", "Clases y equipos"]);
        _source = Select("Origen del ataque", ["Humanos + bots", "Humanos", "Bots", "Monstruos", "Todos los orígenes"]);
        _scale = Select("Escala de las gráficas", ["Automática", "Lineal", "Logarítmica"]);
        _unfinished = new CheckBox { Text = "Incluir partidas interrumpidas", ButtonPressed = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _unfinished.Toggled += _ => { Render(); _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0); }; _filters.AddChild(_unfinished);
        _status = Ui.Label("Cargando informes…", 16, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_status);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 12); content.AddChild(_body);
        _refresh = Ui.Button("Actualizar desde Firebase", () => _ = Fetch(true)); content.AddChild(_refresh);
        _more = Ui.Button("Cargar más partidas", () => _ = Fetch(false)); _more.Visible = false; content.AddChild(_more);
        _deleteHistory = Ui.Button("Borrar historial global de Firebase", ConfirmDeleteHistory);
        _deleteHistory.Name = "DeleteCloudStatistics"; _deleteHistory.Disabled = verification; content.AddChild(_deleteHistory);
        ArcanaMenuChrome.Attach(this, margin, main, "Estadísticas", 3, main.NavigateOptionAMenu);
        GetViewport().SizeChanged += ResizeFilters; ResizeFilters();
        _ = Fetch(true);
    }
    private OptionButton Select(string title, string[] choices)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddChild(Ui.Label(title, 14, Ui.Muted));
        var select = new ScrollOptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 44), FitToLongestItem = false };
        foreach (string text in choices) select.AddItem(text);
        select.ItemSelected += _ =>
        {
            if (_body == null) return;
            Render(); _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, 0);
        }; box.AddChild(select); _filters.AddChild(box); return select;
    }
    private void ResizeFilters()
    {
        _filters.Columns = ResponsiveLayout.Columns(Math.Max(1, GetViewport().GetVisibleRect().Size.X - 90), 220, 3, 10);
        foreach (var chart in _body.FindChildren("*", "Control", true, false).OfType<StatisticsBarChart>())
        {
            chart.CustomMinimumSize = new(chart.CustomMinimumSize.X, BarChartHeight);
            ((Control)chart.GetParent()).CustomMinimumSize = new(0, BarChartHeight + 22);
        }
    }
    internal void SelectViewForVerification(int view) { _view.Select(view); Render(); }
    internal void SelectModeForVerification(int mode) { _mode.Select(mode); Render(); }
    internal void SelectSourceForVerification(int source) { _source.Select(source); Render(); }
    internal void SelectScaleForVerification(int scale) { _scale.Select(scale); Render(); }
    internal void SelectDamageElementForVerification(int element) { _damageElement = element; Render(); }
    public override void _ExitTree() { GetViewport().SizeChanged -= ResizeFilters; _lifetime.Cancel(); _lifetime.Dispose(); }
}
