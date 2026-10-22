using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
    private int _chartIndex;
    private readonly Dictionary<string, (double Value, double Shield)> _chartValues = new(StringComparer.Ordinal);
    private readonly record struct Bar(string Label, double Value, double Shield, Color Color);
    private static Color ElementTint(int element) => element < 8 ? Ui.ElementColor((Element)element) : Ui.Muted;
    private double GroupSum(StatisticGroup group, StatisticMeasure measure) =>
        _totals.Where(p => p.Key.Item1 == group && p.Key.Item3 == measure).Sum(p => p.Value);
    private VBoxContainer Chart(string title, string description)
    {
        var panel = new PanelContainer { Name = "StatisticsChart_" + _chartIndex++, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Ui.Panel(new("101f32"), 10, new("365d80"))); _body.AddChild(panel);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; content.AddThemeConstantOverride("separation", 10); panel.AddChild(content);
        var heading = Ui.Label(title, 20, Ui.Gold); heading.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(heading);
        var note = Ui.Label(description, 14, Ui.Muted); note.AutowrapMode = TextServer.AutowrapMode.WordSmart; content.AddChild(note);
        return content;
    }
    private void Bars(string title, string description, IEnumerable<Bar> source, bool damage = true, string unit = "")
    {
        var data = source.ToArray();
        var box = Chart(title, description);
        double maximum = data.Length == 0 ? 0 : data.Max(p => p.Value + p.Shield);
        if (maximum <= 0) { box.AddChild(Ui.Label(data.Length == 0 ? "Sin datos para esta selección." : "Todos los valores de esta selección son cero.", 16, Ui.Muted)); return; }
        var legend = Ui.Label(damage ? $"Color: daño a vida · Dorado: daño a escudos · Escala 0–{N(maximum)}" : $"Escala 0–{N(maximum)} {unit}", 14, Ui.Muted);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(legend);
        foreach (var item in data)
        {
            _chartValues[title + "/" + item.Label] = (item.Value, item.Shield);
            var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4); box.AddChild(row);
            var labels = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; labels.AddThemeConstantOverride("separation", 12); row.AddChild(labels);
            var name = Ui.Label(item.Label, 15, item.Color); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; name.SizeFlagsHorizontal = SizeFlags.ExpandFill; labels.AddChild(name);
            string text = damage ? $"{N(item.Value)} vida · {N(item.Shield)} escudo" : N(item.Value) + (unit.Length > 0 ? " " + unit : "");
            var value = Ui.Label(text, 15); value.HorizontalAlignment = HorizontalAlignment.Right; value.AutowrapMode = TextServer.AutowrapMode.WordSmart; value.SizeFlagsHorizontal = SizeFlags.ExpandFill; labels.AddChild(value);
            var plot = new Control { CustomMinimumSize = new(0, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(plot);
            var track = new ColorRect { Color = new("24364d"), MouseFilter = MouseFilterEnum.Ignore }; plot.AddChild(track); Ui.Full(track);
            void Segment(double start, double end, Color tint)
            {
                if (end <= start) return;
                var segment = new ColorRect { Color = tint, AnchorLeft = (float)(start / maximum), AnchorRight = (float)(end / maximum), AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
                plot.AddChild(segment);
            }
            Segment(0, item.Value, item.Color); Segment(item.Value, item.Value + item.Shield, Ui.Shield);
            if (item.Value > 0 && item.Shield > 0)
            {
                float edge = (float)(item.Value / maximum);
                plot.AddChild(new ColorRect { Color = new("101f32"), AnchorLeft = edge, AnchorRight = edge, AnchorBottom = 1,
                    OffsetLeft = -1, OffsetRight = 1, MouseFilter = MouseFilterEnum.Ignore });
            }
        }
    }
    private void OverviewCharts()
    {
        Bars("Daño por origen", "Humanos y bots se suman en un único registro, sin duplicar los totales de los informes.",
            new[] { new Bar("Humanos + bots", GroupSum(StatisticGroup.HumanElement, StatisticMeasure.Damage) + GroupSum(StatisticGroup.BotElement, StatisticMeasure.Damage),
                    GroupSum(StatisticGroup.HumanElement, StatisticMeasure.ShieldDamage) + GroupSum(StatisticGroup.BotElement, StatisticMeasure.ShieldDamage), Ui.Blue),
                new Bar("Monstruos", GroupSum(StatisticGroup.EnemyElementDealt, StatisticMeasure.Damage), GroupSum(StatisticGroup.EnemyElementDealt, StatisticMeasure.ShieldDamage), Ui.Red),
                new Bar("Entorno", GroupSum(StatisticGroup.EnvironmentElement, StatisticMeasure.Damage), GroupSum(StatisticGroup.EnvironmentElement, StatisticMeasure.ShieldDamage), Ui.Muted) });
        Note("El filtro Origen del ataque se aplica a Elementos y Hechizos. Las demás vistas comparan todos los orígenes.");
    }
    private float BarChartHeight => GetViewport().GetVisibleRect().Size.Y < 500 ? 190 : 340;
    private void VerticalChart(string title, string description, StatisticsBarChart.Category[] categories,
        string name = "ElementDamageBars", bool enemies = false, string valueTitle = "Daño / curación / absorción")
    {
        var box = Chart(title, description);
        var scroll = new ContentScrollContainer { Name = name + "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, BarChartHeight + 22) };
        box.AddChild(scroll);
        var chart = new StatisticsBarChart { Name = name, Categories = categories,
            CustomMinimumSize = new(0, BarChartHeight), CategoryWidth = enemies ? 116 : 80,
            HorizontalTitle = enemies ? "Tipo de enemigo" : "Elemento", ValueTitle = valueTitle };
        scroll.AddChild(chart);
        var bar = scroll.GetHScrollBar(); bar.CustomMinimumSize = new(0, Ui.IsMobile ? 20 : 16);
        bar.AddThemeStyleboxOverride("scroll", Ui.Panel(ArcanaMenuChrome.Backdrop, 6, ArcanaMenuChrome.Border));
        bar.AddThemeStyleboxOverride("grabber", Ui.Panel(Ui.Blue.Darkened(0.35f), 6, Ui.Blue));
        foreach (var category in categories) _chartValues[title + "/" + category.Label] = (category.Value, 0);
        if (categories.All(c => c.Value == 0)) box.AddChild(Ui.Label("Sin actividad registrada para esta selección.", 15, Ui.Muted));
        var legend = Ui.Label(enemies ? "Desliza horizontalmente para ver todos los enemigos."
            : "Desliza horizontalmente si no caben todos los elementos.", 14, Ui.Muted);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(legend);
    }
    private void ElementChart() => VerticalChart("Daño por elemento",
        "Daño acumulado por elemento. Vida: curación efectiva; Escudo: daño absorbido. Incluye daño a escudos y la aportación real de las mezclas.",
        Enumerable.Range(0, 8).Select(e => new StatisticsBarChart.Category(Elements[e], (Element)e switch
        {
            Element.Life => SupportSum(StatisticMeasure.Healing), Element.Shield => SupportSum(StatisticMeasure.ShieldAbsorbed),
            _ => ElementSum(StatisticMeasure.Damage, e) + ElementSum(StatisticMeasure.ShieldDamage, e)
        }, ElementTint(e))).ToArray());
    private void EnemyChart()
    {
        VerticalChart("Daño realizado por enemigo", "Rojo: daño que inflige cada especie, incluido el daño a escudos. Escala propia desde cero, independiente del daño recibido.",
            Enum.GetValues<MinionKind>().Select(kind => new StatisticsBarChart.Category(DefenseRules.MinionName(kind),
                Sum(StatisticGroup.Enemy, StatisticMeasure.Damage, (int)kind) + Sum(StatisticGroup.Enemy, StatisticMeasure.ShieldDamage, (int)kind), Ui.Red)).ToArray(),
            name: "EnemyDamageDealtBars", enemies: true, valueTitle: "Daño realizado");
        VerticalChart("Daño recibido por enemigo", "Azul: daño que recibe cada especie en vida y escudos. Escala propia desde cero; el orden de los enemigos coincide con la gráfica de daño realizado.",
            Enum.GetValues<MinionKind>().Select(kind => new StatisticsBarChart.Category(DefenseRules.MinionName(kind),
                Sum(StatisticGroup.Enemy, StatisticMeasure.DamageReceived, (int)kind) + Sum(StatisticGroup.Enemy, StatisticMeasure.ShieldAbsorbed, (int)kind), Ui.Blue)).ToArray(),
            name: "EnemyDamageReceivedBars", enemies: true, valueTitle: "Daño recibido");
        Bars("Amenaza media por monstruo", "Daño causado dividido entre apariciones de cada especie. Ayuda a comparar enemigos frecuentes y raros; no representa daño por segundo.",
            Enum.GetValues<MinionKind>().Select(kind =>
            {
                int key = (int)kind; double count = Sum(StatisticGroup.Enemy, StatisticMeasure.Spawns, key);
                return new Bar(DefenseRules.MinionName(kind), count > 0 ? Sum(StatisticGroup.Enemy, StatisticMeasure.Damage, key) / count : 0,
                    count > 0 ? Sum(StatisticGroup.Enemy, StatisticMeasure.ShieldDamage, key) / count : 0, Ui.Red);
            }).Where(p => p.Value + p.Shield > 0).OrderByDescending(p => p.Value + p.Shield));
    }
    private void AttackCharts()
    {
        Bars("Ataques con más daño", "Los diez ataques con más daño acumulado del origen seleccionado. Los usos y combinaciones aparecen en las tablas.",
            Enum.GetValues<StatisticAttack>().Select(attack => new Bar(Attacks[(int)attack], AttackSum(StatisticMeasure.Damage, (int)attack),
                AttackSum(StatisticMeasure.ShieldDamage, (int)attack), Ui.Energy)).Where(p => p.Value + p.Shield > 0).OrderByDescending(p => p.Value + p.Shield).Take(10));
        Bars("Ataques más utilizados", "Lanzamientos acumulados del mismo origen. Las auras continuas pueden causar daño sin generar nuevos usos.",
            Enum.GetValues<StatisticAttack>().Select(attack => new Bar(Attacks[(int)attack], AttackSum(StatisticMeasure.Casts, (int)attack), 0, Ui.Blue))
                .Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(10), damage: false, unit: "usos");
    }
    private void ProgressionChart()
    {
        var stages = _totals.Keys.Where(k => k.Item1 == StatisticGroup.Difficulty).Select(k => k.Item2).Distinct().Order()
            .Where(stage => Sum(StatisticGroup.Difficulty, StatisticMeasure.Spawns, stage) > 0).ToArray();
        var box = Chart("Vida de los monstruos por dificultad", "Media ponderada por apariciones. El eje horizontal es la dificultad y el vertical parte de cero; combina las especies que aparecieron en cada etapa.");
        if (stages.Length == 0) { box.AddChild(Ui.Label("Sin apariciones de supervivencia para esta selección.", 16, Ui.Muted)); return; }
        var points = stages.Select(stage => new StatisticsTrendChart.Sample(stage,
            Sum(StatisticGroup.Difficulty, StatisticMeasure.SpawnHealth, stage) / Sum(StatisticGroup.Difficulty, StatisticMeasure.Spawns, stage))).ToArray();
        box.AddChild(new StatisticsTrendChart { Name = "StatisticsTrend", Points = points });
        Bars("Objetos recogidos por calidad", "Recuento de objetos recogidos en las partidas seleccionadas.", Enum.GetValues<ItemTier>()
            .Select(tier => new Bar(EquipmentCatalogue.TierName(tier), Sum(StatisticGroup.Equipment, StatisticMeasure.Collected, (int)tier), 0, Ui.Blue)), damage: false, unit: "objetos");
    }
    private static string ActorName(int actor) => actor >= 64 ? DefenseRules.MinionName((MinionKind)(actor - 64)) :
        $"Magos · {WizardTypes.Name((WizardType)(actor % 16))} · equipo {actor / 32 + 1}";
    private void ClassChart()
    {
        int[] actors = _totals.Keys.Where(k => k.Item1 == StatisticGroup.Actor).Select(k => CombinedActor(k.Item2)).Distinct().Where(k => k < 64).ToArray();
        Bars("Daño de las clases y equipos", "Humanos y bots sumados en un registro por clase y equipo. Se muestran los diez grupos con más daño acumulado.",
            actors.Select(actor => new Bar(ActorName(actor), ActorSum(actor, StatisticMeasure.Damage), ActorSum(actor, StatisticMeasure.ShieldDamage), actor % 32 >= 16 ? Ui.Health : Ui.Blue))
                .Where(p => p.Value + p.Shield > 0).OrderByDescending(p => p.Value + p.Shield).Take(10));
        Bars("Tiempo congelado", "Porcentaje del tiempo vivo de cada clase y equipo pasado bajo congelación. No atribuye el estado a un hechizo concreto.",
            actors.Where(actor => ActorSum(actor, StatisticMeasure.AliveSeconds) > 0).Select(actor => new Bar(ActorName(actor),
                100 * ActorSum(actor, StatisticMeasure.FrozenSeconds) / ActorSum(actor, StatisticMeasure.AliveSeconds), 0, Ui.ElementColor(Element.Cold)))
                .OrderByDescending(p => p.Value).Take(10), damage: false, unit: "%");
    }
    internal double ChartValueForVerification(string title, string row) => _chartValues.GetValueOrDefault(title + "/" + row).Value;
}
