using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class Main
{
    private bool _statisticsSmoke;
    private int _statisticsSmokeFrame, _statisticsSmokePhase;
    private string? _statisticsCaptures;
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
                if (_statisticsArchive != null || CanViewMatchStatistics || ((Button)_accountProfilePanel!.FindChild("ProfileMatchStatistics", true, false)).Visible)
                    throw new InvalidOperationException("La prueba envía estadísticas o expone el visor sin permiso.");
                CloseAccountProfile();
                var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); world.AddPlayer("NO DEBE APARECER"); world.Step();
                var metrics = new List<MatchStatistic>();
                foreach (var measure in new[] { StatisticMeasure.Damage, StatisticMeasure.ShieldDamage, StatisticMeasure.Casts, StatisticMeasure.Kills, StatisticMeasure.Healing, StatisticMeasure.GoldEarned, StatisticMeasure.GoldSpent }) metrics.Add(new(StatisticGroup.Total, 0, measure, 12345));
                for (int e = 0; e < 9; e++) { metrics.Add(new(StatisticGroup.Element, e, StatisticMeasure.Damage, 923.25 * (e + 1))); metrics.Add(new(StatisticGroup.Element, e, StatisticMeasure.ShieldDamage, 48 * e)); }
                foreach (var kind in Enum.GetValues<MinionKind>())
                {
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.Spawns, 34)); metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.SpawnHealth, 100000));
                    metrics.Add(new(StatisticGroup.Enemy, (int)kind, StatisticMeasure.Deaths, 28));
                    for (int e = 0; e < 9; e++) metrics.Add(new(StatisticGroup.EnemyElement, (int)kind * 9 + e, StatisticMeasure.DamageReceived, 4200));
                }
                foreach (var attack in Enum.GetValues<StatisticAttack>()) { metrics.Add(new(StatisticGroup.Attack, (int)attack, StatisticMeasure.Damage, 5455)); metrics.Add(new(StatisticGroup.Attack, (int)attack, StatisticMeasure.Casts, 35)); }
                for (int d = 1; d <= 8; d++) { metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.Spawns, 20)); metrics.Add(new(StatisticGroup.Difficulty, d, StatisticMeasure.SpawnHealth, d * 10000)); }
                for (int a = 0; a < 5; a++) { metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.Damage, 2500)); metrics.Add(new(StatisticGroup.Actor, a, StatisticMeasure.FrozenSeconds, 9)); }
                var report = world.MatchStatistics() with { Metrics = metrics.ToArray(), Revision = 18000, Seconds = 300, Status = MatchRecordStatus.Completed };
                report.Validate();
                _statisticsPanel = new(); _ui.AddChild(_statisticsPanel); _statisticsPanel.Build(this, [report,
                    report with { RunId = Guid.NewGuid().ToString("N"), Mode = GameMode.Duel, Map = ArenaMapId.Classic }]);
                _statisticsSmokePhase = 1; _statisticsSmokeFrame = 0; return true;
            }
            if (_statisticsSmokePhase == 1)
            {
                _statisticsPanel!.SelectModeForVerification(1);
                if (!_statisticsPanel.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.StartsWith("1 partida ·", StringComparison.Ordinal))) throw new InvalidOperationException("El filtro de modo no aísla los informes.");
                _statisticsPanel.SelectModeForVerification(0); _statisticsSmokePhase = 2; _statisticsSmokeFrame = 0; return true;
            }
            int view = (_statisticsSmokePhase - 2) / 2;
            if (view >= 6)
            { GD.Print("MATCH_STATISTICS_SMOKE_OK: permisos, seis vistas, filtros, tablas y aislamiento de telemetría."); GetTree().Quit(0); _statisticsSmoke = false; return true; }
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
            if (_statisticsCaptures != null)
            {
                if (_statisticsSmokeFrame == 12) { scroll.ScrollVertical = (int)((Control)scroll.GetChild(0)).GetChild<Control>(4).Position.Y; return true; }
                if (_statisticsSmokeFrame < 24) return true;
                System.IO.Directory.CreateDirectory(_statisticsCaptures);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_statisticsCaptures, "statistics-" + view + ".png"));
            }
            _statisticsSmokePhase++; _statisticsSmokeFrame = 0; return true;
        }
        catch (Exception error) { GD.PushError("MATCH_STATISTICS_SMOKE_FAILED: " + error.Message); GetTree().Quit(1); _statisticsSmoke = false; return true; }
    }
}
