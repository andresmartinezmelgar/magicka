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
        Bars("Daño por origen", "Daño efectivo acumulado en las partidas seleccionadas; cada origen conserva su propia barra.",
            new[] { ("Humanos", StatisticGroup.HumanElement, Ui.Blue), ("Bots", StatisticGroup.BotElement, Ui.Health),
                ("Monstruos", StatisticGroup.EnemyElementDealt, Ui.Red), ("Entorno", StatisticGroup.EnvironmentElement, Ui.Muted) }
            .Select(p => new Bar(p.Item1, GroupSum(p.Item2, StatisticMeasure.Damage), GroupSum(p.Item2, StatisticMeasure.ShieldDamage), p.Item3)));
        Note("El filtro Origen del ataque se aplica a Elementos y Hechizos. Las demás vistas comparan todos los orígenes.");
    }
    private void ElementChart() => Bars("Daño por elemento", "Las mezclas reparten el daño real entre sus componentes. Las barras comparten la misma escala.",
        Enumerable.Range(0, 9).Select(e => new Bar(Elements[e], Sum(ElementGroup, StatisticMeasure.Damage, e), Sum(ElementGroup, StatisticMeasure.ShieldDamage, e), ElementTint(e)))
            .OrderByDescending(p => p.Value + p.Shield));
    private void EnemyChart()
    {
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
            Enum.GetValues<StatisticAttack>().Select(attack => new Bar(Attacks[(int)attack], Sum(AttackGroup, StatisticMeasure.Damage, (int)attack),
                Sum(AttackGroup, StatisticMeasure.ShieldDamage, (int)attack), Ui.Energy)).Where(p => p.Value + p.Shield > 0).OrderByDescending(p => p.Value + p.Shield).Take(10));
        Bars("Ataques más utilizados", "Lanzamientos acumulados del mismo origen. Las auras continuas pueden causar daño sin generar nuevos usos.",
            Enum.GetValues<StatisticAttack>().Select(attack => new Bar(Attacks[(int)attack], Sum(AttackGroup, StatisticMeasure.Casts, (int)attack), 0, Ui.Blue))
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
        $"{(actor % 32 >= 16 ? "Bot" : "Humano")} · {WizardTypes.Name((WizardType)(actor % 16))} · equipo {actor / 32 + 1}";
    private void ClassChart()
    {
        int[] actors = _totals.Keys.Where(k => k.Item1 == StatisticGroup.Actor).Select(k => k.Item2).Distinct().Where(k => k < 64).ToArray();
        Bars("Daño de las clases y equipos", "Solo magos humanos y bots, agrupados por clase y equipo. Se muestran los diez grupos con más daño acumulado.",
            actors.Select(actor => new Bar(ActorName(actor), Sum(StatisticGroup.Actor, StatisticMeasure.Damage, actor), Sum(StatisticGroup.Actor, StatisticMeasure.ShieldDamage, actor), actor % 32 >= 16 ? Ui.Health : Ui.Blue))
                .Where(p => p.Value + p.Shield > 0).OrderByDescending(p => p.Value + p.Shield).Take(10));
        Bars("Tiempo congelado", "Porcentaje del tiempo vivo de cada clase y equipo pasado bajo congelación. No atribuye el estado a un hechizo concreto.",
            actors.Where(actor => Sum(StatisticGroup.Actor, StatisticMeasure.AliveSeconds, actor) > 0).Select(actor => new Bar(ActorName(actor),
                100 * Sum(StatisticGroup.Actor, StatisticMeasure.FrozenSeconds, actor) / Sum(StatisticGroup.Actor, StatisticMeasure.AliveSeconds, actor), 0, Ui.ElementColor(Element.Cold)))
                .OrderByDescending(p => p.Value).Take(10), damage: false, unit: "%");
    }
    internal double ChartValueForVerification(string title, string row) => _chartValues.GetValueOrDefault(title + "/" + row).Value;
}
