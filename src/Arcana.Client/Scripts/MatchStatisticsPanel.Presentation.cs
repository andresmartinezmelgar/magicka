using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
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
        _totals.Clear(); _tableIndex = 0; _chartIndex = 0; _chartValues.Clear();
        _source.Disabled = _view.Selected is not (1 or 3);
        _source.TooltipText = "El origen del ataque filtra las vistas Elementos y Hechizos.";
        int days = _period.Selected switch { 1 => 7, 2 => 30, 3 => 90, _ => 0 };
        string since = DateTime.UtcNow.AddDays(-Math.Max(0, days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reports = _reports.Values.Where(p => (_mode.Selected == 0 || (int)p.Mode == _mode.Selected - 1) &&
            (_version.Selected == 0 || p.GameVersion == _version.GetItemText(_version.Selected)) &&
            (days == 0 || string.CompareOrdinal(p.Day, since) >= 0) && (_unfinished.ButtonPressed || p.Complete)).ToArray();
        foreach (var report in reports) foreach (var m in report.Metrics)
        { var key = (m.Group, m.Key, m.Measure); _totals[key] = _totals.GetValueOrDefault(key) + m.Value; }
        _status.Text = $"{reports.Length} {(reports.Length == 1 ? "partida" : "partidas")} · {_reports.Count} partidas de Firebase · {reports.Count(p => p.Complete)} finalizadas\nLos filtros se aplican a los informes consultados (máximo 1000 documentos).";
        if (_nextPageToken.Length > 0) _status.Text += _loadedPages < 100 ? " Carga más partidas para ampliar la muestra." : " Se alcanzó el límite de consulta.";
        if (reports.Length == 0) { Note("No hay partidas de Firebase para esta selección."); return; }
        if (reports.Any(p => p.DroppedBuckets > 0)) Note("Algunos informes agotaron el límite de detalle; los totales se conservan. Consulta otra selección para comparar el desglose.");
        double seconds = reports.Sum(p => p.Seconds);
        switch (_view.Selected)
        {
            case 0:
                OverviewCharts();
                var summary = Table("Métrica", "Valor");
                Row(summary, "Tiempo jugado", N(seconds / 60) + " min");
                foreach (var (title, measure) in new[] { ("Daño a vida", StatisticMeasure.Damage), ("Daño a escudos", StatisticMeasure.ShieldDamage), ("Bajas", StatisticMeasure.Kills), ("Muertes", StatisticMeasure.Deaths), ("Lanzamientos", StatisticMeasure.Casts), ("Curación efectiva", StatisticMeasure.Healing), ("Escudo concedido", StatisticMeasure.ShieldGranted), ("Escudo reparado", StatisticMeasure.ShieldRepaired), ("Fuego amigo", StatisticMeasure.FriendlyDamage), ("Daño propio", StatisticMeasure.SelfDamage), ("Reanimaciones", StatisticMeasure.Revives) }) Row(summary, title, N(Sum(StatisticGroup.Total, measure)));
                var performance = Table("Rendimiento", "Media");
                foreach (var (key, title) in new[] { (0, "Monstruos simultáneos"), (1, "Simulación (ms/paso)"), (2, "FPS comunicados"), (3, "p95 de fotograma (ms)") })
                { double samples = Sum(StatisticGroup.Performance, StatisticMeasure.Samples, key); Row(performance, title, samples > 0 ? N(Sum(StatisticGroup.Performance, StatisticMeasure.ValueSum, key) / samples) : "Sin muestras"); }
                Note($"{reports.Count(p => p.Continuation)} tramos de partidas continuadas tras un cambio de versión o al recuperar un guardado anterior.");
                break;
            case 1:
                ElementChart();
                var elements = Table("Elemento", "Daño / vida", "Escudos", "Inmunes");
                for (int e = 0; e < 9; e++) Row(elements, Elements[e], N(ElementSum(StatisticMeasure.Damage, e)), N(ElementSum(StatisticMeasure.ShieldDamage, e)), N(ElementSum(StatisticMeasure.ImmuneHits, e)));
                Note("El daño de una mezcla se reparte por la aportación real de sus componentes después de resistencias. Las inmunidades del prisma se cuentan por componente rechazado.");
                break;
            case 2:
                EnemyChart();
                foreach (var kind in Enum.GetValues<MinionKind>())
                {
                    int k = (int)kind; double spawns = Sum(StatisticGroup.Enemy, StatisticMeasure.Spawns, k);
                    if (spawns == 0) continue;
                    BestiaryPortrait.Heading(_body, kind, DefenseRules.MinionName(kind));
                    var monster = Table("Apariciones", "Vida inicial media", "Bajas", "Daño causado");
                    Row(monster, N(spawns), N(Sum(StatisticGroup.Enemy, StatisticMeasure.SpawnHealth, k) / spawns), N(Sum(StatisticGroup.Enemy, StatisticMeasure.Deaths, k)), N(Sum(StatisticGroup.Actor, StatisticMeasure.Damage, 64 + k)));
                    var received = Table("Elemento recibido", "Vida perdida", "Escudo absorbido");
                    for (int e = 0; e < 9; e++) if (Sum(StatisticGroup.EnemyElement, StatisticMeasure.DamageReceived, k * 9 + e) + Sum(StatisticGroup.EnemyElement, StatisticMeasure.ShieldAbsorbed, k * 9 + e) > 0)
                        Row(received, Elements[e], N(Sum(StatisticGroup.EnemyElement, StatisticMeasure.DamageReceived, k * 9 + e)), N(Sum(StatisticGroup.EnemyElement, StatisticMeasure.ShieldAbsorbed, k * 9 + e)));
                }
                Note("Los estados acumulados y el tiempo con vida se muestran por clase en Clases y equipos.");
                break;
            case 3:
                AttackCharts();
                var attacks = Table("Ataque", "Usos", "Daño / vida", "Escudos");
                foreach (var attack in Enum.GetValues<StatisticAttack>())
                { int k = (int)attack; Row(attacks, Attacks[k], N(AttackSum(StatisticMeasure.Casts, k)), N(AttackSum(StatisticMeasure.Damage, k)), N(AttackSum(StatisticMeasure.ShieldDamage, k))); }
                Note("Un haz cuenta un lanzamiento al iniciarse; sus impactos son acumulados. Las auras continuas aportan daño sin multiplicar los usos por fotograma.");
                var combos = Table("Combinación (todos los orígenes)", "Usos", "Daño / vida");
                foreach (int recipe in _totals.Keys.Where(k => k.Item1 == StatisticGroup.Recipe).Select(k => k.Item2).Distinct().OrderByDescending(k => Sum(StatisticGroup.Recipe, StatisticMeasure.Damage, k)).Take(20))
                { int key = recipe; var parts = new List<string>(); while (key > 0) { parts.Insert(0, Elements[(key % 9) - 1]); key /= 9; } Row(combos, parts.Count == 0 ? "Sin elementos" : string.Join(" + ", parts), N(Sum(StatisticGroup.Recipe, StatisticMeasure.Casts, recipe)), N(Sum(StatisticGroup.Recipe, StatisticMeasure.Damage, recipe))); }
                break;
            case 4:
                ProgressionChart();
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
                ClassChart();
                foreach (int actor in _totals.Keys.Where(k => k.Item1 == StatisticGroup.Actor).Select(k => CombinedActor(k.Item2)).Distinct().Order())
                {
                    Note(ActorName(actor));
                    var stats = Table("Métrica", "Valor");
                    foreach (var (title, measure) in new[] { ("Daño causado", StatisticMeasure.Damage), ("Daño recibido", StatisticMeasure.DamageReceived), ("Curación", StatisticMeasure.Healing), ("Bajas", StatisticMeasure.Kills), ("Muertes", StatisticMeasure.Deaths), ("Tiempo vivo (s)", StatisticMeasure.AliveSeconds), ("Mojado (s)", StatisticMeasure.WetSeconds), ("Ardiendo (s)", StatisticMeasure.BurningSeconds), ("Congelado (s)", StatisticMeasure.FrozenSeconds) }) Row(stats, title, N(ActorSum(actor, measure)));
                }
                break;
        }
    }
}
