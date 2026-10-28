using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Resumen personal disponible para cualquier cuenta, sin consultar estadísticas globales.</summary>
public partial class PersonalDamagePanel : Control
{
    private PanelContainer _panel = null!;
    private ScrollContainer _scroll = null!, _chartScroll = null!;
    private VBoxContainer _body = null!;
    private ElementTextLabel _context = null!, _total = null!, _absorbed = null!, _scaleNote = null!;
    private readonly List<ElementTextLabel> _values = [];
    private StatisticsBarChart? _chart;
    private MatchDamageSnapshot? _lastDamage;
    private bool _rendered;

    public void Build(Action close)
    {
        Name = "PersonalDamagePanel"; Theme = Ui.Theme(); Ui.Full(this);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new(0.015f, 0.03f, 0.055f, 0.94f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(shade); Ui.Full(shade);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore }; AddChild(center); Ui.Full(center);
        _panel = new PanelContainer { Name = "PersonalDamageCard", MouseFilter = MouseFilterEnum.Stop };
        center.AddChild(_panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10); _panel.AddChild(box);
        var header = new HBoxContainer(); box.AddChild(header);
        var title = Ui.Label("TU DAÑO EN ESTA PARTIDA", 22, Ui.Gold);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart; title.SizeFlagsHorizontal = SizeFlags.ExpandFill; header.AddChild(title);
        var exit = Ui.ExitButton("Atrás", close, "PersonalDamageClose"); exit.CustomMinimumSize = new(100, 48); header.AddChild(exit);
        _context = Ui.Label("", 16); _context.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_context);
        _total = Ui.Label("", 19, Ui.Gold); _total.Name = "PersonalDamageTotal";
        _total.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_total);
        _absorbed = Ui.Label("", 17, Ui.Shield); _absorbed.Name = "PersonalDamageAbsorbed";
        _absorbed.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_absorbed);
        _scroll = new ContentScrollContainer { Name = "PersonalDamageScroll", SizeFlagsVertical = SizeFlags.ExpandFill };
        Ui.VisibleVerticalScroll(_scroll); box.AddChild(_scroll);
        _body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 12); _scroll.AddChild(_body);
        var note = Ui.Label("Daño real a enemigos desde el inicio de esta partida: hechizos, áreas, auras, quemaduras y reflejos. No suma fuego amigo, daño propio ni exceso de un golpe mortal. Las mezclas reparten el daño entre sus elementos. Todos los porcentajes usan el daño total de la partida como referencia; la columna Total suma A vida y A escudos.", 15, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; _body.AddChild(note);
        var table = Ui.Table(4, "PersonalDamageTable"); _body.AddChild(table);
        foreach (string text in new[] { "Elemento", "A vida (%)", "A escudos (%)", "Total (%)" }) Ui.TableCell(table, text, header: true, size: 15);
        for (int element = 0; element < MatchDamageSnapshot.ElementCount; element++)
        {
            var tint = element < 8 ? Ui.ElementColor((Element)element) : Ui.Muted;
            Ui.TableCell(table, ElementName(element), color: tint, size: 16);
            for (int column = 0; column < 3; column++)
                _values.Add(Ui.TableCell(table, "0 %", number: true, color: column == 1 ? Ui.Shield : tint, size: 16));
        }
        var chartTitle = Ui.Label("DAÑO POR ELEMENTO", 18, Ui.Gold); _body.AddChild(chartTitle);
        _chartScroll = new ContentScrollContainer { Name = "PersonalDamageChartScroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new(0, 360), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddChild(_chartScroll);
        _scaleNote = Ui.Label("", 14, Ui.Muted); _scaleNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; _body.AddChild(_scaleNote);
        var hint = Ui.Label("Puedes volver a abrir este resumen desde Tabla / Clasificación → Ver mi daño de esta partida.", 15, Ui.Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(hint);
        ResizePanel(); GetViewport().SizeChanged += ResizePanel;
    }

    public void Update(WizardSnapshot player, bool ended)
    {
        _context.Text = ended ? "RESULTADO FINAL · daño acumulado de esta partida"
            : !player.Alive ? "HAS CAÍDO · daño acumulado hasta ahora; se conserva al reaparecer"
            : "DAÑO ACUMULADO · la partida continúa";
        var damage = player.Damage;
        if (_rendered && Equal(_lastDamage, damage)) return;
        _lastDamage = damage; _rendered = true;
        double health = damage?.HealthTotal ?? 0, shield = damage?.ShieldTotal ?? 0;
        _total.Text = $"Total: {Points(health + shield)} · a vida: {Points(health)} · a escudos: {Points(shield)}";
        _absorbed.Text = "Daño absorbido por tu escudo: " + Points(damage?.PersonalShieldAbsorbed ?? 0) +
            "\nPor tus barreras: " + Points(damage?.BarrierAbsorbed ?? 0);
        _absorbed.TooltipText = "Daño real detenido por tu escudo personal y, por separado, por las barreras que has creado. Acumulado durante la partida; la expiración del escudo no cuenta como daño absorbido.";
        var categories = new StatisticsBarChart.Category[MatchDamageSnapshot.ElementCount];
        for (int element = 0; element < categories.Length; element++)
        {
            double hp = damage?.Health[element] ?? 0, protection = damage?.Shield[element] ?? 0;
            double[] values = [hp, protection, hp + protection];
            for (int column = 0; column < values.Length; column++)
            {
                var cell = _values[element * 3 + column]; cell.Text = StatisticsPresentation.Percent(values[column], health + shield);
                cell.TooltipText = StatisticsPresentation.Number(values[column]) + " puntos";
            }
            categories[element] = new(ElementName(element), health + shield > 0 ? 100 * (hp + protection) / (health + shield) : 0,
                element < 8 ? Ui.ElementColor((Element)element) : Ui.Muted);
        }
        if (_chart == null)
        {
            _chart = new StatisticsBarChart { Name = "PersonalElementDamageBars", Categories = categories,
                ValueTitle = "% del daño total", Percentage = true, Logarithmic = false };
            _chartScroll.AddChild(_chart);
        }
        else _chart.ReplaceCategories(categories);
        _scaleNote.Text = "Escala lineal de 0 a 100 %: cada barra muestra la parte del daño total causada por ese elemento, sumando vida y escudos enemigos. Desliza horizontalmente si no caben todos.";
    }
    private static string Points(double value) => value >= 1_000_000_000 ? value.ToString("0.###E+0", System.Globalization.CultureInfo.InvariantCulture)
        : StatisticsPresentation.Number(value);
    private static string ElementName(int element) => element < 8 ? Ui.ElementName((Element)element) : "Sin elemento";
    private static bool Equal(MatchDamageSnapshot? a, MatchDamageSnapshot? b) => ReferenceEquals(a, b) ||
        a != null && b != null && a.PersonalShieldAbsorbed == b.PersonalShieldAbsorbed && a.BarrierAbsorbed == b.BarrierAbsorbed &&
        a.Health.AsSpan().SequenceEqual(b.Health) && a.Shield.AsSpan().SequenceEqual(b.Shield);
    private void ResizePanel()
    {
        var size = GetViewportRect().Size;
        _panel.CustomMinimumSize = new(Math.Max(240, Math.Min(900, size.X - 32)), Math.Max(240, Math.Min(740, size.Y - 28)));
        _scroll.CustomMinimumSize = Vector2.Zero;
        _panel.Size = Vector2.Zero;
    }
    public override void _ExitTree() => GetViewport().SizeChanged -= ResizePanel;
}
