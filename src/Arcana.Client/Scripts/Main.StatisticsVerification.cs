using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _statisticsSmoke;
    private int _statisticsSmokeFrame, _statisticsSmokePhase;
    private string? _statisticsCaptures;
    private MatchStatisticsReport[]? _statisticsReportsForVerification;
    private void StartStatisticsSmoke(string[] arguments)
    {
        _statisticsSmoke = true;
        _statisticsCaptures = arguments.FirstOrDefault(a => a.StartsWith("--statistics-captures="))?["--statistics-captures=".Length..];
        string? requested = arguments.FirstOrDefault(a => a.StartsWith("--smoke-viewport="));
        if (requested != null)
        { var dimensions = requested["--smoke-viewport=".Length..].Split('x'); var size = new Vector2I(int.Parse(dimensions[0]), int.Parse(dimensions[1])); GetWindow().ContentScaleSize = size; GetWindow().Size = size; }
        OpenAccountProfile();
    }
    private bool CheckStatisticsSmoke()
    {
        if (!_statisticsSmoke || ++_statisticsSmokeFrame < 12) return false;
        try
        {
            if (_statisticsSmokePhase == 0)
            {
                var access = (Button)_accountProfilePanel!.FindChild("ProfileMatchStatistics", true, false);
                if (_statisticsOutbox != null || _statisticsTransport != null || CanViewMatchStatistics || access.Visible)
                    throw new InvalidOperationException("La prueba envía estadísticas o expone el visor sin permiso.");
                foreach (var (uid, allowed) in new[] { ("hg7F74kCNVQU3tU2itdjWu9SkyS2", true), ("qRUza2DieeTypFqBIRA5KhbtXoY2", true),
                    ("hg7f74kcnvqu3tu2itdjwu9skys2", false), ("otra-cuenta", false) })
                {
                    Accounts.RestoreOfflineProfile(new(uid, "Prueba local", 0, 0, 0, new(), []));
                    RefreshStatisticsAccess();
                    if (CanViewMatchStatistics != allowed || access.Visible != allowed || allowed && access.Disabled)
                        throw new InvalidOperationException("El botón no aplica los dos UID exactos sin conexión.");
                    if (allowed) access.EmitSignal(Button.SignalName.Pressed); else OpenMatchStatistics();
                    if (GodotObject.IsInstanceValid(_statisticsPanel) != allowed) throw new InvalidOperationException("El botón local no abre el visor o permite otra cuenta.");
                    CloseMatchStatistics();
                }
                Accounts.RestoreOfflineProfile(new("hg7F74kCNVQU3tU2itdjWu9SkyS2", "Prueba local", 0, 0, 0, new(), []));
                RefreshStatisticsAccess(); access.EmitSignal(Button.SignalName.Pressed);
                Accounts.SignOut().GetAwaiter().GetResult(); TickMatchStatistics(0);
                if (CanViewMatchStatistics || access.Visible || _statisticsPanel != null) throw new InvalidOperationException("El visor sigue abierto al cerrar sesión.");
                CloseAccountProfile();
                var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); world.AddPlayer("NO DEBE APARECER"); world.Step();
                var metrics = new List<MatchStatistic>();
                foreach (var measure in new[] { StatisticMeasure.Damage, StatisticMeasure.ShieldDamage, StatisticMeasure.Casts, StatisticMeasure.Kills, StatisticMeasure.Healing, StatisticMeasure.GoldEarned, StatisticMeasure.GoldSpent }) metrics.Add(new(StatisticGroup.Total, 0, measure, 12345));
                for (int e = 0; e < 9; e++)
                {
                    foreach (var (group, damage, shield) in new[] { (StatisticGroup.Element, 923.25, 48), (StatisticGroup.HumanElement, 600.0, 32), (StatisticGroup.BotElement, 300.0, 12), (StatisticGroup.EnemyElementDealt, 23.25, 4) })
                    { metrics.Add(new(group, e, StatisticMeasure.Damage, damage * (e + 1))); metrics.Add(new(group, e, StatisticMeasure.ShieldDamage, shield * e)); }
                }
                foreach (var kind in Enum.GetValues<MinionKind>())
                {
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.Spawns, 34)); metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.SpawnHealth, 100000));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.Deaths, 28));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.Damage, 800 * ((int)kind + 1)));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.ShieldDamage, 150 * ((int)kind + 1)));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.DamageReceived, 4_000_000 * ((int)kind + 1)));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.ShieldAbsorbed, 250 * ((int)kind + 1)));
                    metrics.Add(new(StatisticGroup.Actor, 64 + (int)kind, StatisticMeasure.Damage, 800 * ((int)kind + 1)));
                    metrics.Add(new(StatisticGroup.Actor, 64 + (int)kind, StatisticMeasure.Healing, 7));
                    metrics.Add(new(StatisticGroup.Actor, 64 + (int)kind, StatisticMeasure.ShieldAbsorbed, 11));
                    for (int e = 0; e < 9; e++) metrics.Add(new(StatisticGroup.EnemyElement, (int)kind * 9 + e, StatisticMeasure.DamageReceived, 4200));
                }
                foreach (var (group, multiplier) in new[] { (StatisticGroup.HumanElementSource, 2.0), (StatisticGroup.BotElementSource, 1.0),
                    (StatisticGroup.EnemyElementSource, .1), (StatisticGroup.EnvironmentElementSource, .05) })
                    foreach (var source in Enum.GetValues<StatisticDamageSource>())
                    {
                        int key = (int)source * 9 + (int)Element.Fire;
                        metrics.Add(new(group, key, StatisticMeasure.Damage, 200 / Math.Pow(2, (int)source) * multiplier));
                        metrics.Add(new(group, key, StatisticMeasure.ShieldDamage, 20 / Math.Pow(2, (int)source) * multiplier));
                    }
                foreach (var attack in Enum.GetValues<StatisticAttack>())
                {
                    foreach (var (group, damage, casts) in new[] { (StatisticGroup.Attack, 545, 3), (StatisticGroup.HumanAttack, 400, 1), (StatisticGroup.BotAttack, 140, 1), (StatisticGroup.EnemyAttack, 5, 1) })
                    { metrics.Add(new(group, (int)attack, StatisticMeasure.Damage, damage * ((int)attack + 1))); metrics.Add(new(group, (int)attack, StatisticMeasure.Casts, casts * ((int)attack + 1))); }
                }
                for (int d = 1; d <= 8; d++) { metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.Spawns, 20)); metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.SpawnHealth, d * 10000)); }
                foreach (int a in new[] { 0, 1, 16, 17, 32 })
                { metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.Damage, 2500 * (a + 1))); metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.AliveSeconds, 300)); metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.FrozenSeconds, a + 9));
                    metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.Healing, a + 10));
                    metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.ShieldAbsorbed, a + 20)); }
                foreach (var tier in Enum.GetValues<ItemTier>()) metrics.Add(new(StatisticGroup.Equipment, (int)tier, StatisticMeasure.Collected, 30.0 / ((int)tier + 1)));
                var report = world.MatchStatistics() with { Metrics = metrics.ToArray(), Revision = 18000, Seconds = 300, Status = MatchRecordStatus.Completed };
                report.Validate();
                MatchStatisticsReport[] reports = [report,
                    report with { RunId = Guid.NewGuid().ToString("N"), Metrics = metrics.Select(m => m.Group == StatisticGroup.Difficulty && m.Measure == StatisticMeasure.Spawns ? m with { Value = 10 } : m).ToArray() },
                    report with { RunId = Guid.NewGuid().ToString("N"), Mode = GameMode.Duel, Map = ArenaMapId.Classic,
                        Metrics = metrics.Where(m => m.Group is not (StatisticGroup.Difficulty or StatisticGroup.Enemy or StatisticGroup.EnemyElement) && !(m.Group == StatisticGroup.Actor && m.Key >= 64)).ToArray() }];
                // La consulta simulada cruza dos páginas e incluye una revisión antigua duplicada.
                _statisticsReportsForVerification = [reports[0] with { Status = MatchRecordStatus.Left }, .. reports];
                Accounts.RestoreOfflineProfile(new("qRUza2DieeTypFqBIRA5KhbtXoY2", "Prueba local", 0, 0, 0, new(), []));
                OpenMatchStatistics();
                _statisticsSmokePhase = 1; _statisticsSmokeFrame = 0; return true;
            }
            if (_statisticsSmokePhase == 1)
            {
                if (!_statisticsPanel!.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>().Any(l => l.Text.StartsWith("3 partidas ·", StringComparison.Ordinal)))
                { if (_statisticsSmokeFrame < 120) return true; throw new InvalidOperationException("El visor no pagina o duplica revisiones de Firebase."); }
                _statisticsPanel!.SelectViewForVerification(1);
                void CheckWater(double expected)
                { if (Math.Abs(_statisticsPanel.ChartValueForVerification("Daño por elemento", "Agua") - expected) > 0.001) throw new InvalidOperationException("Las gráficas mezclan modos u orígenes de ataque."); }
                CheckWater(2700);
                foreach (var (label, expected) in new[] { ("Ataques directos", 1800.0), ("Áreas de hechizos", 900.0), ("Auras pasivas", 450.0), ("Quemaduras", 225.0), ("Sin desglose", 18225.0) })
                    if (Math.Abs(_statisticsPanel.ChartValueForVerification("Origen del daño de Fuego", label) - expected) > .001)
                        throw new InvalidOperationException("El desglose de fuego mezcla ataques, áreas, auras, quemaduras o datos antiguos.");
                _statisticsPanel.SelectDamageElementForVerification((int)Element.Water);
                if (_statisticsPanel.ChartValueForVerification("Origen del daño de Agua", "Sin desglose") != 2700 ||
                    _statisticsPanel.ChartValueForVerification("Origen del daño de Agua", "Ataques directos") != 0)
                    throw new InvalidOperationException("Los informes anteriores se atribuyen a ataques directos sin evidencia.");
                _statisticsPanel.SelectDamageElementForVerification((int)Element.Fire);
                foreach (int scale in new[] { 2, 1 })
                {
                    _statisticsPanel.SelectScaleForVerification(scale);
                    var chart = (StatisticsBarChart)_statisticsPanel.FindChild("ElementDamageBars", true, false);
                    foreach (var category in chart.Categories.Where(c => c.Parts != null && c.Label != "Vida" && c.Label != "Escudo"))
                    {
                        var parts = category.Parts!;
                        if (parts.Length < 2 || Math.Abs(parts.Sum(p => p.Value) - category.Value) > .001 ||
                            parts[0].Label != "Hechizos directos" || parts[1].Label != "Áreas")
                            throw new InvalidOperationException("La gráfica no separa hechizos y áreas o duplica daño.");
                    }
                    var water = chart.Categories.Single(c => c.Label == "Agua");
                    if (water.Parts!.Single(p => p.Label == "Sin desglose").Value != 2700)
                        throw new InvalidOperationException("La gráfica atribuye daño antiguo a hechizos o áreas.");
                    if (chart.DataScale.Logarithmic != (scale == 2)) throw new InvalidOperationException("El selector de escala no cambia la gráfica.");
                }
                _statisticsPanel.SelectScaleForVerification(0);
                if (!_statisticsPanel.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>().Any(l => l.Text == "% del total"))
                    throw new InvalidOperationException("Faltan los valores y porcentajes legibles bajo las barras.");
                void CheckSupport(double healing, double absorbed)
                {
                    if (Math.Abs(_statisticsPanel.ChartValueForVerification("Daño por elemento", "Vida") - healing) > 0.001 ||
                        Math.Abs(_statisticsPanel.ChartValueForVerification("Daño por elemento", "Escudo") - absorbed) > 0.001)
                        throw new InvalidOperationException("Vida o Escudo no usan la curación/absorción efectiva del origen filtrado.");
                }
                CheckSupport(348, 498);
                int species = Enum.GetValues<MinionKind>().Length;
                _statisticsPanel.SelectSourceForVerification(3); CheckSupport(species * 14, species * 22);
                _statisticsPanel.SelectSourceForVerification(4); CheckSupport(348 + species * 14, 498 + species * 22);
                _statisticsPanel.SelectSourceForVerification(0);
                _statisticsPanel!.SelectModeForVerification(1);
                if (!_statisticsPanel.FindChildren("*", "Control", true, false).OfType<ElementTextLabel>().Any(l => l.Text.StartsWith("1 partida ·", StringComparison.Ordinal))) throw new InvalidOperationException("El filtro de modo no aísla los informes.");
                CheckWater(900); CheckSupport(116, 166);
                _statisticsPanel.SelectSourceForVerification(1); CheckWater(600); CheckSupport(63, 93);
                _statisticsPanel.SelectSourceForVerification(2); CheckWater(300); CheckSupport(53, 73);
                _statisticsPanel.SelectSourceForVerification(3); CheckWater(23.25); CheckSupport(0, 0);
                _statisticsPanel.SelectSourceForVerification(4); CheckWater(923.25); CheckSupport(116, 166);
                _statisticsPanel.SelectSourceForVerification(0); _statisticsPanel.SelectModeForVerification(0);
                _statisticsPanel.SelectViewForVerification(2);
                const string dealtTitle = "Daño realizado por enemigo", receivedTitle = "Daño recibido por enemigo";
                foreach (var kind in Enum.GetValues<MinionKind>())
                    if (Math.Abs(_statisticsPanel.ChartValueForVerification(dealtTitle, DefenseRules.MinionName(kind)) - 1900 * ((int)kind + 1)) > 0.001 ||
                        Math.Abs(_statisticsPanel.ChartValueForVerification(receivedTitle, DefenseRules.MinionName(kind)) - 8_000_500 * ((int)kind + 1)) > 0.001)
                        throw new InvalidOperationException("Las gráficas por enemigo no suman el daño real a vida y escudos.");
                var dealt = (StatisticsBarChart)_statisticsPanel.FindChild("EnemyDamageDealtBars", true, false);
                var received = (StatisticsBarChart)_statisticsPanel.FindChild("EnemyDamageReceivedBars", true, false);
                foreach (var chart in new[] { dealt, received })
                    if (chart.EnemyPortraits.Count != species || chart.EnemyPortraits.Any(p => chart.Categories[p.Key].Enemy != p.Value.Kind ||
                        p.Value.FindChild("PortraitModel", true, false) is not Node3D))
                        throw new InvalidOperationException("Una gráfica de enemigos perdió el sprite de su especie.");
                if (dealt.ScaleMaximum != 1900 * species || received.ScaleMaximum != 8_000_500 * species ||
                    !dealt.Categories.Select(c => c.Label).SequenceEqual(received.Categories.Select(c => c.Label)))
                    throw new InvalidOperationException("El daño recibido aplana la escala del realizado o cambia el orden de las especies.");
                _statisticsPanel.SelectModeForVerification(1);
                foreach (string name in new[] { "EnemyDamageDealtBars", "EnemyDamageReceivedBars" })
                {
                    var emptyEnemies = (StatisticsBarChart)_statisticsPanel.FindChild(name, true, false);
                    if (emptyEnemies.Categories.Length != species || emptyEnemies.Categories.Any(c => c.Value != 0) || emptyEnemies.ScaleMaximum != 1 ||
                        emptyEnemies.EnemyPortraits.Count != species)
                        throw new InvalidOperationException("Las especies sin datos desaparecen o muestran daño inexistente.");
                }
                _statisticsPanel.SelectModeForVerification(0);
                _statisticsPanel.SelectViewForVerification(3);
                if (Math.Abs(_statisticsPanel.ChartValueForVerification("Ataques con más daño", "Regeneración") - 42120) > 0.001)
                    throw new InvalidOperationException("Hechizos no suma humanos y bots sin duplicar los totales.");
                _statisticsPanel.SelectViewForVerification(5);
                string actor = $"Magos · {WizardTypes.Name((WizardType)0)} · equipo 1";
                if (Math.Abs(_statisticsPanel.ChartValueForVerification("Daño de las clases y equipos", actor) - 135000) > 0.001)
                    throw new InvalidOperationException("Las clases no suman humanos y bots en un registro.");
                _statisticsSmokePhase = 2; _statisticsSmokeFrame = 0; return true;
            }
            int view = (_statisticsSmokePhase - 2) / 2;
            if (view >= 6)
            { CleanupStatisticsSmoke(); GD.Print("MATCH_STATISTICS_SMOKE_OK: dos UID, consulta Firebase paginada sin red de producción, seis vistas, daño directo/áreas/auras/quemaduras por elemento, compatibilidad sin desglose, escalas automática/lineal/logarítmica, valores y porcentajes, enemigos separados, soporte efectivo y humanos+bots."); GetTree().Quit(0); _statisticsSmoke = false; return true; }
            var scroll = (ScrollContainer)_statisticsPanel!.FindChild("StatisticsScroll", true, false);
            if (_statisticsSmokePhase % 2 == 0)
            {
                _statisticsPanel.SelectViewForVerification(view); scroll.ScrollVertical = 0;
                _statisticsSmokePhase++; _statisticsSmokeFrame = 0; return true;
            }
            var bounds = scroll.GetGlobalRect();
            if (!GetViewport().GetVisibleRect().Encloses(bounds)) throw new InvalidOperationException("El área de lectura sale de la pantalla.");
            foreach (var table in _statisticsPanel.FindChildren("StatisticsTable_*", "GridContainer", true, false).OfType<Control>())
                if (table.GetGlobalRect().Position.X < bounds.Position.X - 1 || table.GetGlobalRect().End.X > bounds.End.X + 1) throw new InvalidOperationException("Una tabla desborda horizontalmente.");
            var charts = _statisticsPanel.FindChildren("StatisticsChart_*", "PanelContainer", true, false).OfType<Control>().ToArray();
            if (charts.Length == 0) throw new InvalidOperationException("Faltan las gráficas de esta vista.");
            foreach (var chart in charts)
                if (chart.GetGlobalRect().Position.X < bounds.Position.X - 1 || chart.GetGlobalRect().End.X > bounds.End.X + 1) throw new InvalidOperationException("Una gráfica desborda horizontalmente.");
            if (view == 2)
                foreach (var plot in _statisticsPanel.FindChildren("*", "Control", true, false).OfType<StatisticsBarChart>())
                    if (plot.EnemyPortraits.Count > 0 && (plot.PlotHeight < 100 ||
                        plot.EnemyPortraits.Values.Any(p => p.Position.Y < plot.PlotBaseline + 40 || p.Size.DistanceTo(new(72, 72)) > 1)))
                        throw new InvalidOperationException("Los retratos comprimen los ejes o se solapan con las etiquetas de las barras.");
            if (view == 4)
            {
                var trend = (StatisticsTrendChart)_statisticsPanel.FindChild("StatisticsTrend", true, false);
                if (trend.Points.Length != 8 || Math.Abs(trend.Points[0].Health - 20000.0 / 30) > 0.001 || Math.Abs(trend.Points[^1].Health - 160000.0 / 30) > 0.001) throw new InvalidOperationException("La curva no pondera correctamente la vida inicial por apariciones.");
            }
            if (_statisticsCaptures != null)
            {
                if (_statisticsSmokeFrame == 12)
                {
                    var plot = _statisticsPanel.FindChildren("*", "Control", true, false).OfType<StatisticsBarChart>().FirstOrDefault();
                    scroll.ScrollVertical = plot != null ? scroll.ScrollVertical + (int)(plot.GlobalPosition.Y - bounds.Position.Y)
                        : (int)((Control)scroll.GetChild(0)).GetChild<Control>(4).Position.Y;
                    return true;
                }
                if (_statisticsSmokeFrame < 24) return true;
                System.IO.Directory.CreateDirectory(_statisticsCaptures);
                if (view == 2 && _statisticsSmokeFrame < 36)
                {
                    if (_statisticsSmokeFrame == 24)
                    {
                        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_statisticsCaptures, "statistics-2.png"));
                        var received = (Control)_statisticsPanel.FindChild("EnemyDamageReceivedBars", true, false);
                        scroll.ScrollVertical += (int)(received.GlobalPosition.Y - bounds.Position.Y);
                    }
                    return true;
                }
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_statisticsCaptures,
                    view == 2 ? "statistics-2-received.png" : "statistics-" + view + ".png"));
            }
            _statisticsSmokePhase++; _statisticsSmokeFrame = 0; return true;
        }
        catch (Exception error) { CleanupStatisticsSmoke(); GD.PushError("MATCH_STATISTICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _statisticsSmoke = false; return true; }
    }
    private void CleanupStatisticsSmoke()
    {
        _statisticsReportsForVerification = null;
    }
}
