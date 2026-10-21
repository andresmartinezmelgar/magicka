using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

/// <summary>Balance global sobre informes sin identidad; Firestore comprueba el permiso en cada página.</summary>
public partial class MatchStatisticsPanel : Control
{
    private Main _main = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, MatchStatisticsReport> _reports = new(StringComparer.Ordinal);
    private VBoxContainer _body = null!;
    private Label _status = null!;
    private Button _more = null!;
    private GridContainer _filters = null!;
    private OptionButton _mode = null!, _period = null!, _version = null!, _view = null!, _source = null!;
    private CheckBox _unfinished = null!;
    private string _next = "";
    private bool _loading;
    private int _tableIndex;
    private readonly Dictionary<(StatisticGroup, int, StatisticMeasure), double> _totals = [];
    private static readonly string[] Elements = ["Agua", "Vida", "Escudo", "Frío", "Rayo", "Arcano", "Tierra", "Fuego", "Físico"];
    private static readonly string[] Attacks = ["Haz", "Relámpago", "Cono", "Roca", "Fragmentos", "Empuje", "Barrera", "Cuerpo a cuerpo", "Sobre uno mismo", "Nova", "Mina", "Meteorito", "Prisa", "Teletransporte", "Reanimar", "Aura de fuego", "Aura de rayo", "Aura de regeneración", "Meteorito pasivo", "Pulso glacial", "Cadena de rayos", "Quemadura", "Golpe enemigo", "Área enemiga", "Bombardeo rúnico", "Regeneración"];
    private static string N(double value) => value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.GetCultureInfo("es-ES"));
    public void Build(Main main, MatchStatisticsReport[]? fixture = null)
    {
        _main = main; Name = "MatchStatisticsPanel"; Theme = Ui.Theme(); Ui.Full(this);
        var shade = new ColorRect { Color = ArcanaMenuChrome.Backdrop }; AddChild(shade); Ui.Full(shade);
        var margin = new MarginContainer(); AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 18);
        var scroll = new ContentScrollContainer { Name = "StatisticsScroll", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; margin.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 12); scroll.AddChild(content);
        content.AddChild(Ui.Label("ESTADÍSTICAS DE PARTIDAS", 24, Ui.Gold));
        var explanation = Ui.Label("Balance global · sin nombres ni cuentas en los informes", 16, Ui.Muted); explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(explanation);
        _filters = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddChild(_filters);
        _filters.AddThemeConstantOverride("h_separation", 10); _filters.AddThemeConstantOverride("v_separation", 8);
        _mode = Select("Modo", ["Todos los modos", "Duelo", "Arena", "Guerra", "Supervivencia"]);
        _period = Select("Periodo", ["Todo el periodo", "Últimos 7 días", "Últimos 30 días", "Últimos 90 días"]);
        _version = Select("Versión", ["Todas las versiones"]);
        _view = Select("Vista", ["Resumen", "Elementos", "Monstruos", "Hechizos", "Progresión", "Clases y equipos"]);
        _source = Select("Origen del ataque", ["Todos los orígenes", "Humanos", "Bots", "Monstruos"]);
        _unfinished = new CheckBox { Text = "Incluir partidas interrumpidas", ButtonPressed = true }; _unfinished.Toggled += _ => Render(); _filters.AddChild(_unfinished);
        _status = Ui.Label("Cargando informes…", 16, Ui.Muted); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(_status);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 12); content.AddChild(_body);
        _more = Ui.Button("Cargar más partidas", () => _ = Fetch()); content.AddChild(_more);
        ArcanaMenuChrome.Attach(this, margin, main, "Estadísticas", 3, main.NavigateOptionAMenu);
        GetViewport().SizeChanged += ResizeFilters; ResizeFilters();
        if (fixture != null) { foreach (var report in fixture) _reports[report.RunId] = report; Versions(); Render(); _more.Visible = false; }
        else _ = Fetch();
    }
    private OptionButton Select(string title, string[] choices)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; box.AddChild(Ui.Label(title, 14, Ui.Muted));
        var select = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 44), FitToLongestItem = false };
        foreach (string text in choices) select.AddItem(text);
        select.ItemSelected += _ => { if (_body != null) Render(); }; box.AddChild(select); _filters.AddChild(box); return select;
    }
    private void ResizeFilters() => _filters.Columns = GetViewport().GetVisibleRect().Size.X < 720 ? 1 : 3;
    private void Versions()
    {
        string selected = _version.GetItemText(_version.Selected);
        _version.Clear(); _version.AddItem("Todas las versiones");
        foreach (string version in _reports.Values.Select(p => p.GameVersion).Distinct().OrderDescending()) _version.AddItem(version);
        for (int i = 0; i < _version.ItemCount; i++) if (_version.GetItemText(i) == selected) _version.Select(i);
    }
    private async Task Fetch()
    {
        if (_loading || _reports.Count >= 1000) return;
        _loading = true; _more.Disabled = true;
        try
        {
            var page = await _main.Accounts.ReadMatchStatistics(_next, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || !_main.CanViewMatchStatistics) return;
            foreach (var report in page.Reports)
                if (!_reports.TryGetValue(report.RunId, out var previous) || MatchStatisticsReport.IsNewer(report, previous)) _reports[report.RunId] = report;
            _next = page.NextPageToken; Versions(); Render();
            _more.Visible = _next.Length > 0 && _reports.Count < 1000;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is HttpRequestException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            if (_lifetime.IsCancellationRequested) return;
            if (error is UnauthorizedAccessException) { _reports.Clear(); Render(); }
            _status.Text = error is UnauthorizedAccessException ? "Esta cuenta no tiene acceso a las estadísticas." : "No se pudieron cargar los informes. Puedes reintentar.";
            _more.Visible = true; _more.Text = "Reintentar";
        }
        finally { _loading = false; if (!_lifetime.IsCancellationRequested) _more.Disabled = false; }
    }
    private double Sum(StatisticGroup group, StatisticMeasure measure, int key = 0) => _totals.GetValueOrDefault((group, key, measure));
    private StatisticGroup ElementGroup => _source.Selected switch { 1 => StatisticGroup.HumanElement, 2 => StatisticGroup.BotElement, 3 => StatisticGroup.EnemyElementDealt, _ => StatisticGroup.Element };
    private StatisticGroup AttackGroup => _source.Selected switch { 1 => StatisticGroup.HumanAttack, 2 => StatisticGroup.BotAttack, 3 => StatisticGroup.EnemyAttack, _ => StatisticGroup.Attack };
    private GridContainer Table(params string[] headers)
    {
        var table = Ui.Table(headers.Length, "StatisticsTable_" + _tableIndex++); _body.AddChild(table);
        foreach (string header in headers) Ui.TableCell(table, header, header: true, size: 15);
        return table;
    }
    private static void Row(GridContainer table, params string[] cells)
    { foreach (string cell in cells) Ui.TableCell(table, cell, size: 15); }
    private void Note(string text) { var label = Ui.Label(text, 16, Ui.Muted); label.AutowrapMode = TextServer.AutowrapMode.WordSmart; _body.AddChild(label); }
    private void Render()
    {
        foreach (Node child in _body.GetChildren()) { _body.RemoveChild(child); child.QueueFree(); }
        _totals.Clear(); _tableIndex = 0;
        int days = _period.Selected switch { 1 => 7, 2 => 30, 3 => 90, _ => 0 };
        string since = DateTime.UtcNow.AddDays(-Math.Max(0, days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reports = _reports.Values.Where(p => (_mode.Selected == 0 || (int)p.Mode == _mode.Selected - 1) &&
            (_version.Selected == 0 || p.GameVersion == _version.GetItemText(_version.Selected)) &&
            (days == 0 || string.CompareOrdinal(p.Day, since) >= 0) && (_unfinished.ButtonPressed || p.Complete)).ToArray();
        foreach (var report in reports) foreach (var m in report.Metrics)
        { var key = (m.Group, m.Key, m.Measure); _totals[key] = _totals.GetValueOrDefault(key) + m.Value; }
        _status.Text = $"{reports.Length} {(reports.Length == 1 ? "partida" : "partidas")} · {_reports.Count} informes cargados · {reports.Count(p => p.Complete)} finalizados\nLos filtros se aplican a los informes cargados (máximo 1000).";
        if (reports.Length == 0) { Note("No hay partidas para esta selección."); return; }
        if (reports.Any(p => p.DroppedBuckets > 0)) Note("Algunos informes agotaron el límite de detalle; los totales se conservan. Consulta otra selección para comparar el desglose.");
        double seconds = reports.Sum(p => p.Seconds);
        switch (_view.Selected)
        {
            case 0:
                var summary = Table("Métrica", "Valor");
                Row(summary, "Tiempo jugado", N(seconds / 60) + " min");
                foreach (var (title, measure) in new[] { ("Daño a vida", StatisticMeasure.Damage), ("Daño a escudos", StatisticMeasure.ShieldDamage), ("Bajas", StatisticMeasure.Kills), ("Muertes", StatisticMeasure.Deaths), ("Lanzamientos", StatisticMeasure.Casts), ("Curación efectiva", StatisticMeasure.Healing), ("Escudo concedido", StatisticMeasure.ShieldGranted), ("Escudo reparado", StatisticMeasure.ShieldRepaired), ("Fuego amigo", StatisticMeasure.FriendlyDamage), ("Daño propio", StatisticMeasure.SelfDamage), ("Reanimaciones", StatisticMeasure.Revives) }) Row(summary, title, N(Sum(StatisticGroup.Total, measure)));
                var performance = Table("Rendimiento", "Media");
                foreach (var (key, title) in new[] { (0, "Monstruos simultáneos"), (1, "Simulación (ms/paso)"), (2, "FPS comunicados"), (3, "p95 de fotograma (ms)") })
                { double samples = Sum(StatisticGroup.Performance, StatisticMeasure.Samples, key); Row(performance, title, samples > 0 ? N(Sum(StatisticGroup.Performance, StatisticMeasure.ValueSum, key) / samples) : "Sin muestras"); }
                Note($"{reports.Count(p => p.Continuation)} tramos de partidas continuadas tras un cambio de versión o al recuperar un guardado anterior.");
                break;
            case 1:
                var elements = Table("Elemento", "Daño / vida", "Escudos", "Inmunes");
                for (int e = 0; e < 9; e++) Row(elements, Elements[e], N(Sum(ElementGroup, StatisticMeasure.Damage, e)), N(Sum(ElementGroup, StatisticMeasure.ShieldDamage, e)), N(Sum(ElementGroup, StatisticMeasure.ImmuneHits, e)));
                Note("El daño de una mezcla se reparte por la aportación real de sus componentes después de resistencias. Las inmunidades del prisma se cuentan por componente rechazado.");
                break;
            case 2:
                foreach (var kind in Enum.GetValues<MinionKind>())
                {
                    int k = (int)kind; double spawns = Sum(StatisticGroup.Enemy, StatisticMeasure.Spawns, k);
                    if (spawns == 0) continue;
                    Note(DefenseRules.MinionName(kind));
                    var monster = Table("Apariciones", "Vida inicial media", "Bajas", "Daño causado");
                    Row(monster, N(spawns), N(Sum(StatisticGroup.Enemy, StatisticMeasure.SpawnHealth, k) / spawns), N(Sum(StatisticGroup.Enemy, StatisticMeasure.Deaths, k)), N(Sum(StatisticGroup.Actor, StatisticMeasure.Damage, 64 + k)));
                    var received = Table("Elemento recibido", "Vida perdida", "Escudo absorbido");
                    for (int e = 0; e < 9; e++) if (Sum(StatisticGroup.EnemyElement, StatisticMeasure.DamageReceived, k * 9 + e) + Sum(StatisticGroup.EnemyElement, StatisticMeasure.ShieldAbsorbed, k * 9 + e) > 0)
                        Row(received, Elements[e], N(Sum(StatisticGroup.EnemyElement, StatisticMeasure.DamageReceived, k * 9 + e)), N(Sum(StatisticGroup.EnemyElement, StatisticMeasure.ShieldAbsorbed, k * 9 + e)));
                }
                Note("Los estados acumulados y el tiempo con vida se muestran por clase en Clases y equipos.");
                break;
            case 3:
                var attacks = Table("Ataque", "Usos", "Daño / vida", "Escudos");
                foreach (var attack in Enum.GetValues<StatisticAttack>())
                { int k = (int)attack; Row(attacks, Attacks[k], N(Sum(AttackGroup, StatisticMeasure.Casts, k)), N(Sum(AttackGroup, StatisticMeasure.Damage, k)), N(Sum(AttackGroup, StatisticMeasure.ShieldDamage, k))); }
                Note("Un haz cuenta un lanzamiento al iniciarse; sus impactos son acumulados. Las auras continuas aportan daño sin multiplicar los usos por fotograma.");
                var combos = Table("Combinación (todos los orígenes)", "Usos", "Daño / vida");
                foreach (int recipe in _totals.Keys.Where(k => k.Item1 == StatisticGroup.Recipe).Select(k => k.Item2).Distinct().OrderByDescending(k => Sum(StatisticGroup.Recipe, StatisticMeasure.Damage, k)).Take(20))
                { int key = recipe; var parts = new List<string>(); while (key > 0) { parts.Insert(0, Elements[(key % 9) - 1]); key /= 9; } Row(combos, parts.Count == 0 ? "Sin elementos" : string.Join(" + ", parts), N(Sum(StatisticGroup.Recipe, StatisticMeasure.Casts, recipe)), N(Sum(StatisticGroup.Recipe, StatisticMeasure.Damage, recipe))); }
                break;
            case 4:
                var progression = Table("Dificultad", "Apariciones", "Vida inicial media");
                foreach (int stage in _totals.Keys.Where(k => k.Item1 == StatisticGroup.Difficulty).Select(k => k.Item2).Distinct().Order())
                { double count = Sum(StatisticGroup.Difficulty, StatisticMeasure.Spawns, stage); Row(progression, stage.ToString(), N(count), N(count > 0 ? Sum(StatisticGroup.Difficulty, StatisticMeasure.SpawnHealth, stage) / count : 0)); }
                var gold = Table("Economía", "Valor");
                Row(gold, "Oro ganado", N(Sum(StatisticGroup.Total, StatisticMeasure.GoldEarned))); Row(gold, "Oro gastado", N(Sum(StatisticGroup.Total, StatisticMeasure.GoldSpent))); Row(gold, "Subidas de nivel", N(Sum(StatisticGroup.Total, StatisticMeasure.Levels)));
                var drops = Table("Calidad del objeto", "Recogidos");
                foreach (var tier in Enum.GetValues<ItemTier>()) Row(drops, EquipmentCatalogue.TierName(tier), N(Sum(StatisticGroup.Equipment, StatisticMeasure.Collected, (int)tier)));
                var relics = Table("Reliquia", "Recogidas"); foreach (var relic in Enum.GetValues<BuffKind>()) Row(relics, RelicBuffs.Name(relic), N(Sum(StatisticGroup.Relic, StatisticMeasure.Collected, (int)relic)));
                break;
            case 5:
                foreach (int actor in _totals.Keys.Where(k => k.Item1 == StatisticGroup.Actor).Select(k => k.Item2).Distinct().Order())
                {
                    Note(actor >= 64 ? DefenseRules.MinionName((MinionKind)(actor - 64)) : $"{(actor % 32 >= 16 ? "Bot" : "Humano")} · {WizardTypes.Name((WizardType)(actor % 16))} · equipo {actor / 32 + 1}");
                    var stats = Table("Métrica", "Valor");
                    foreach (var (title, measure) in new[] { ("Daño causado", StatisticMeasure.Damage), ("Daño recibido", StatisticMeasure.DamageReceived), ("Curación", StatisticMeasure.Healing), ("Bajas", StatisticMeasure.Kills), ("Muertes", StatisticMeasure.Deaths), ("Tiempo vivo (s)", StatisticMeasure.AliveSeconds), ("Mojado (s)", StatisticMeasure.WetSeconds), ("Ardiendo (s)", StatisticMeasure.BurningSeconds), ("Congelado (s)", StatisticMeasure.FrozenSeconds) }) Row(stats, title, N(Sum(StatisticGroup.Actor, measure, actor)));
                }
                break;
        }
    }
    internal void SelectViewForVerification(int view) { _view.Select(view); Render(); }
    internal void SelectModeForVerification(int mode) { _mode.Select(mode); Render(); }
    public override void _ExitTree() { GetViewport().SizeChanged -= ResizeFilters; _lifetime.Cancel(); _lifetime.Dispose(); }
}
