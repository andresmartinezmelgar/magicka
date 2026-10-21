using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _statisticsSmoke;
    private int _statisticsSmokeFrame, _statisticsSmokePhase;
    private string? _statisticsCaptures;
    private string? _statisticsSmokeDirectory;
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
                if (_statisticsArchive != null || _statisticsTransport != null || CanViewMatchStatistics || access.Visible)
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
                    metrics.Add(new(StatisticGroup.Actor, 64 + (int)kind, StatisticMeasure.Damage, 800 * ((int)kind + 1)));
                    for (int e = 0; e < 9; e++) metrics.Add(new(StatisticGroup.EnemyElement, (int)kind * 9 + e, StatisticMeasure.DamageReceived, 4200));
                }
                foreach (var attack in Enum.GetValues<StatisticAttack>())
                { metrics.Add(new(StatisticGroup.Attack, (int)attack, StatisticMeasure.Damage, 545 * ((int)attack + 1))); metrics.Add(new(StatisticGroup.Attack, (int)attack, StatisticMeasure.Casts, 3 * ((int)attack + 1))); }
                for (int d = 1; d <= 8; d++) { metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.Spawns, 20)); metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.SpawnHealth, d * 10000)); }
                foreach (int a in new[] { 0, 1, 16, 17, 32 })
                { metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.Damage, 2500 * (a + 1))); metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.AliveSeconds, 300)); metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.FrozenSeconds, a + 9)); }
                foreach (var tier in Enum.GetValues<ItemTier>()) metrics.Add(new(StatisticGroup.Equipment, (int)tier, StatisticMeasure.Collected, 30.0 / ((int)tier + 1)));
                var report = world.MatchStatistics() with { Metrics = metrics.ToArray(), Revision = 18000, Seconds = 300, Status = MatchRecordStatus.Completed };
                report.Validate();
                MatchStatisticsReport[] reports = [report,
                    report with { RunId = Guid.NewGuid().ToString("N"), Metrics = metrics.Select(m => m.Group == StatisticGroup.Difficulty && m.Measure == StatisticMeasure.Spawns ? m with { Value = 10 } : m).ToArray() },
                    report with { RunId = Guid.NewGuid().ToString("N"), Mode = GameMode.Duel, Map = ArenaMapId.Classic,
                        Metrics = metrics.Where(m => m.Group is not (StatisticGroup.Difficulty or StatisticGroup.Enemy or StatisticGroup.EnemyElement) && !(m.Group == StatisticGroup.Actor && m.Key >= 64)).ToArray() }];
                _statisticsSmokeDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arcana-statistics-smoke-" + Guid.NewGuid().ToString("N"));
                _statisticsArchive = new(_statisticsSmokeDirectory);
                foreach (var entry in reports) _statisticsArchive.Save(entry, upload: false);
                Accounts.RestoreOfflineProfile(new("qRUza2DieeTypFqBIRA5KhbtXoY2", "Prueba local", 0, 0, 0, new(), []));
                OpenMatchStatistics();
                _statisticsSmokePhase = 1; _statisticsSmokeFrame = 0; return true;
            }
            if (_statisticsSmokePhase == 1)
            {
                if (!_statisticsPanel!.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.StartsWith("3 partidas ·", StringComparison.Ordinal)))
                { if (_statisticsSmokeFrame < 120) return true; throw new InvalidOperationException("El visor no lee las partidas del archivo local."); }
                _statisticsPanel!.SelectViewForVerification(1);
                void CheckWater(double expected)
                { if (Math.Abs(_statisticsPanel.ChartValueForVerification("Daño por elemento", "Agua") - expected) > 0.001) throw new InvalidOperationException("Las gráficas mezclan modos u orígenes de ataque."); }
                CheckWater(2769.75);
                _statisticsPanel!.SelectModeForVerification(1);
                if (!_statisticsPanel.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.StartsWith("1 partida ·", StringComparison.Ordinal))) throw new InvalidOperationException("El filtro de modo no aísla los informes.");
                CheckWater(923.25);
                _statisticsPanel.SelectSourceForVerification(1); CheckWater(600);
                _statisticsPanel.SelectSourceForVerification(2); CheckWater(300);
                _statisticsPanel.SelectSourceForVerification(3); CheckWater(23.25);
                _statisticsPanel.SelectSourceForVerification(0); _statisticsPanel.SelectModeForVerification(0);
                _statisticsSmokePhase = 2; _statisticsSmokeFrame = 0; return true;
            }
            int view = (_statisticsSmokePhase - 2) / 2;
            if (view >= 6)
            { CleanupStatisticsSmoke(); GD.Print("MATCH_STATISTICS_SMOKE_OK: dos UID, cierre de sesión, archivos locales, seis vistas, gráficas, filtros, tablas y aislamiento de telemetría."); GetTree().Quit(0); _statisticsSmoke = false; return true; }
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
            if (view == 4)
            {
                var trend = (StatisticsTrendChart)_statisticsPanel.FindChild("StatisticsTrend", true, false);
                if (trend.Points.Length != 8 || Math.Abs(trend.Points[0].Health - 20000.0 / 30) > 0.001 || Math.Abs(trend.Points[^1].Health - 160000.0 / 30) > 0.001) throw new InvalidOperationException("La curva no pondera correctamente la vida inicial por apariciones.");
            }
            if (_statisticsCaptures != null)
            {
                if (_statisticsSmokeFrame == 12) { scroll.ScrollVertical = (int)((Control)scroll.GetChild(0)).GetChild<Control>(4).Position.Y; return true; }
                if (_statisticsSmokeFrame < 24) return true;
                System.IO.Directory.CreateDirectory(_statisticsCaptures);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_statisticsCaptures, "statistics-" + view + ".png"));
            }
            _statisticsSmokePhase++; _statisticsSmokeFrame = 0; return true;
        }
        catch (Exception error) { CleanupStatisticsSmoke(); GD.PushError("MATCH_STATISTICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _statisticsSmoke = false; return true; }
    }
    private void CleanupStatisticsSmoke()
    {
        _statisticsArchive = null;
        if (_statisticsSmokeDirectory != null && System.IO.Directory.Exists(_statisticsSmokeDirectory)) System.IO.Directory.Delete(_statisticsSmokeDirectory, true);
        _statisticsSmokeDirectory = null;
    }
}
