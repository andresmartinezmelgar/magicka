using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
    private int _chartIndex;
    private readonly Dictionary<string, (double Value, double Shield)> _chartValues = new(StringComparer.Ordinal);
    private readonly record struct Bar(string ElementTextLabel, double Value, double Shield, Color Color);
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
        var scale = new MatchStatisticsScale(data.Select(p => p.Value + p.Shield), unit == "%" ? false : Logarithmic);
        double total = data.Sum(p => p.Value + p.Shield);
        var legend = Ui.Label((damage ? "Color: daño a vida · Dorado: daño a escudos. " : "") + StatisticsPresentation.ScaleNote(scale) +
            (unit == "%" ? " Cada porcentaje se calcula sobre el tiempo vivo de su grupo." : $" Porcentajes sobre el total mostrado: {StatisticsPresentation.Number(total)}{(unit.Length > 0 ? " " + unit : " puntos")}.") , 14, Ui.Muted);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(legend);
        foreach (var item in data)
        {
            _chartValues[title + "/" + item.ElementTextLabel] = (item.Value, item.Shield);
            var row = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 4); box.AddChild(row);
            var labels = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; labels.AddThemeConstantOverride("separation", 12); row.AddChild(labels);
            var name = Ui.Label(item.ElementTextLabel, 15, item.Color); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; name.SizeFlagsHorizontal = SizeFlags.ExpandFill; labels.AddChild(name);
            string text = damage ? $"{StatisticsPresentation.Number(item.Value)} vida · {StatisticsPresentation.Number(item.Shield)} escudo"
                : StatisticsPresentation.Number(item.Value) + (unit.Length > 0 ? " " + unit : "");
            if (unit != "%") text += " · " + StatisticsPresentation.Percent(item.Value + item.Shield, total);
            var value = Ui.Label(text, 15); value.HorizontalAlignment = HorizontalAlignment.Right; value.AutowrapMode = TextServer.AutowrapMode.WordSmart; value.SizeFlagsHorizontal = SizeFlags.ExpandFill; labels.AddChild(value);
            var plot = new Control { CustomMinimumSize = new(0, 12), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(plot);
            var track = new ColorRect { Color = new("24364d"), MouseFilter = MouseFilterEnum.Ignore }; plot.AddChild(track); Ui.Full(track);
            double amount = item.Value + item.Shield, width = scale.Normalize(amount);
            double split = amount > 0 ? width * item.Value / amount : 0;
            void Segment(double start, double end, Color tint)
            {
                if (end <= start) return;
                var segment = new ColorRect { Color = tint, AnchorLeft = (float)start, AnchorRight = (float)end, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
                plot.AddChild(segment);
            }
            Segment(0, split, item.Color); Segment(split, width, Ui.Shield);
            if (item.Value > 0 && item.Shield > 0)
            {
                float edge = (float)split;
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
    private float BarChartHeight => GetViewport().GetVisibleRect().Size.Y < 500 ? 280 : 400;
    private void VerticalChart(string title, string description, StatisticsBarChart.Category[] categories,
        string name = "ElementDamageBars", bool enemies = false, string valueTitle = "Daño / curación / absorción")
    {
        var box = Chart(title, description);
        var scroll = new ContentScrollContainer { Name = name + "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new(0, BarChartHeight + 22) };
        box.AddChild(scroll);
        var chart = new StatisticsBarChart { Name = name, Categories = categories,
            CustomMinimumSize = new(0, BarChartHeight), CategoryWidth = categories.Any(c => c.Parts != null) ? 190 : enemies ? 116 : 80,
            HorizontalTitle = enemies ? "Tipo de enemigo" : "Elemento", ValueTitle = valueTitle, Logarithmic = Logarithmic };
        scroll.AddChild(chart);
        var bar = scroll.GetHScrollBar(); bar.CustomMinimumSize = new(0, Ui.IsMobile ? 20 : 16);
        bar.AddThemeStyleboxOverride("scroll", Ui.Panel(ArcanaMenuChrome.Backdrop, 6, ArcanaMenuChrome.Border));
        bar.AddThemeStyleboxOverride("grabber", Ui.Panel(Ui.Blue.Darkened(0.35f), 6, Ui.Blue));
        foreach (var category in categories) _chartValues[title + "/" + category.ElementTextLabel] = (category.Value, 0);
        if (categories.All(c => c.Value == 0)) box.AddChild(Ui.Label("Sin actividad registrada para esta selección.", 15, Ui.Muted));
        var legend = Ui.Label(StatisticsPresentation.ScaleNote(chart.DataScale) + " Porcentajes sobre la suma de los valores de esta gráfica. " +
            (enemies ? "Desliza horizontalmente para ver todos los enemigos." : "Vida mide curación y Escudo, absorción. Desliza si no caben los elementos."), 14, Ui.Muted);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(legend);
        var values = Ui.Table(3, name + "Values"); box.AddChild(values);
        foreach (string header in new[] { enemies ? "Enemigo" : "Elemento", "Valor", "% del total" }) Ui.TableCell(values, header, header: true, size: 14);
        double total = categories.Sum(c => c.Value);
        foreach (var category in categories)
            foreach (string cell in new[] { category.ElementTextLabel, StatisticsPresentation.Number(category.Value), StatisticsPresentation.Percent(category.Value, total) })
                Ui.TableCell(values, cell, size: 14);
        if (categories.Any(c => c.Parts != null))
        {
            var detail = Ui.Table(3, name + "Sources"); box.AddChild(detail);
            foreach (string header in new[] { "Elemento / origen", "Valor", "% del elemento" }) Ui.TableCell(detail, header, header: true, size: 14);
            foreach (var category in categories.Where(c => c.Parts != null))
                foreach (var part in category.Parts!)
                    foreach (string cell in new[] { category.ElementTextLabel + " · " + part.ElementTextLabel, StatisticsPresentation.Number(part.Value), StatisticsPresentation.Percent(part.Value, category.Value) })
                        Ui.TableCell(detail, cell, size: 14);
        }
    }
    private void ElementChart()
    {
        VerticalChart("Daño por elemento",
        "Todas las barras de cada elemento comparten su color. Las etiquetas distinguen daño directo, áreas, auras, quemaduras y datos sin detalle. Incluye daño a vida y escudos, sin duplicarlo. Vida separa curación directa, área, aura, regeneración, anillo y Escudo vital. Escudo separa absorción personal y barreras.",
        Enumerable.Range(0, 8).Select(e => new StatisticsBarChart.Category(Elements[e], (Element)e switch
        {
            Element.Life => SupportSum(StatisticMeasure.Healing), Element.Shield => SupportSum(StatisticMeasure.ShieldAbsorbed),
            _ => ElementSum(StatisticMeasure.Damage, e) + ElementSum(StatisticMeasure.ShieldDamage, e)
        }, ElementTint(e), e == (int)Element.Life ? HealingParts() : e == (int)Element.Shield ? AbsorptionParts() : ElementDamageParts(e))).ToArray());
        var selector = new OptionButton { Name = "DamageBreakdownElement", CustomMinimumSize = new(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (int e in new[] { 0, 3, 4, 5, 6, 7, 8 }) selector.AddItem("Desglose de daño: " + Elements[e], e);
        selector.Select(selector.GetItemIndex(_damageElement));
        selector.ItemSelected += index => { _damageElement = selector.GetItemId((int)index); Render(); }; _body.AddChild(selector);
        string[] names = ["Ataques directos", "Áreas de hechizos", "Auras pasivas", "Quemaduras"];
        var parts = Enum.GetValues<StatisticDamageSource>().Select(source => new Bar(names[(int)source],
            DamageSourceSum(StatisticMeasure.Damage, _damageElement, source), DamageSourceSum(StatisticMeasure.ShieldDamage, _damageElement, source), ElementTint(_damageElement))).ToList();
        parts.Add(new("Sin desglose", Math.Max(0, ElementSum(StatisticMeasure.Damage, _damageElement) - parts.Sum(p => p.Value)),
            Math.Max(0, ElementSum(StatisticMeasure.ShieldDamage, _damageElement) - parts.Sum(p => p.Shield)), ElementTint(_damageElement)));
        Bars("Origen del daño de " + Elements[_damageElement],
            "Directos: haces, conos, rayos, proyectiles y golpes. Áreas: barreras, novas, minas, meteoritos y bombardeos. Auras: zonas pasivas de fuego o rayo. Quemaduras: daño posterior por arder. Sin desglose: informes anteriores o detalle incompleto. Cada daño se cuenta una sola vez.", parts);
    }
    private StatisticsBarChart.Part[] HealingParts()
    {
        var buckets = new Dictionary<string, double>();
        foreach (var attack in Enum.GetValues<StatisticAttack>())
        {
            string label = attack switch
            {
                StatisticAttack.ShieldLeech => "Escudo vital",
                StatisticAttack.Regeneration => "Regeneración",
                StatisticAttack.RegenerationAura => "Aura",
                StatisticAttack.RingPulse => "Anillo",
                StatisticAttack.Nova or StatisticAttack.Mine or StatisticAttack.Meteor or StatisticAttack.Barrier or
                    StatisticAttack.AutoMeteor or StatisticAttack.FrostNova or StatisticAttack.EnemyArea or StatisticAttack.OracleBombardment => "Área",
                _ => "Curación"
            };
            buckets[label] = buckets.GetValueOrDefault(label) + AttackSum(StatisticMeasure.Healing, (int)attack);
        }
        var parts = buckets.Where(p => p.Value > 0).Select(p => new StatisticsBarChart.Part(p.Key, p.Value,
            ElementTint((int)Element.Life))).ToList();
        double unknown = Math.Max(0, SupportSum(StatisticMeasure.Healing) - parts.Sum(p => p.Value));
        if (unknown > 0) parts.Add(new("Sin desglose", unknown, ElementTint((int)Element.Life)));
        return parts.ToArray();
    }
    private StatisticsBarChart.Part[] AbsorptionParts()
    {
        double Source(int kind) => _totals.Where(p => p.Key.Item1 == StatisticGroup.ActorShieldSource &&
            p.Key.Item3 == StatisticMeasure.ShieldAbsorbed && p.Key.Item2 % 2 == kind &&
            (_source.Selected switch { 0 => p.Key.Item2 / 2 < 64, 1 => p.Key.Item2 / 2 < 64 && p.Key.Item2 / 2 % 32 < 16,
                2 => p.Key.Item2 / 2 < 64 && p.Key.Item2 / 2 % 32 >= 16, 3 => p.Key.Item2 / 2 >= 64, _ => true })).Sum(p => p.Value);
        double personal = Source(0), barriers = Source(1);
        var parts = new List<StatisticsBarChart.Part> { new("Personal", personal, ElementTint((int)Element.Shield)), new("Barrera", barriers, ElementTint((int)Element.Shield)) };
        double unknown = Math.Max(0, SupportSum(StatisticMeasure.ShieldAbsorbed) - personal - barriers);
        if (unknown > 0) parts.Add(new("Sin desglose", unknown, ElementTint((int)Element.Shield)));
        return parts.ToArray();
    }
    private StatisticsBarChart.Part[] ElementDamageParts(int element)
    {
        string[] labels = ["Hechizos directos", "Áreas", "Auras", "Quemaduras"];
        var parts = Enum.GetValues<StatisticDamageSource>().Select(source => new StatisticsBarChart.Part(labels[(int)source],
            DamageSourceSum(StatisticMeasure.Damage, element, source) + DamageSourceSum(StatisticMeasure.ShieldDamage, element, source), ElementTint(element))).ToList();
        double unknown = Math.Max(0, ElementSum(StatisticMeasure.Damage, element) + ElementSum(StatisticMeasure.ShieldDamage, element) - parts.Sum(p => p.Value));
        if (unknown > 0) parts.Add(new("Sin desglose", unknown, ElementTint(element)));
        return parts.ToArray();
    }
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
        var scale = new MatchStatisticsScale(points.Select(p => p.Health), Logarithmic);
        var scaleNote = Ui.Label(StatisticsPresentation.ScaleNote(scale), 14, Ui.Muted); scaleNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(scaleNote);
        box.AddChild(new StatisticsTrendChart { Name = "StatisticsTrend", Points = points, Logarithmic = Logarithmic });
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
