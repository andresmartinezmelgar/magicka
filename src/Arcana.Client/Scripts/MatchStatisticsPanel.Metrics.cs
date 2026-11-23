using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

public partial class MatchStatisticsPanel
{
    private static string N(double value) => value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.GetCultureInfo("es-ES"));

    private double Sum(StatisticGroup group, StatisticMeasure measure, int key = 0) => _totals.GetValueOrDefault((group, key, measure));

    private double ElementSum(StatisticMeasure measure, int key) => _source.Selected switch
    {
        0 => Sum(StatisticGroup.HumanElement, measure, key) + Sum(StatisticGroup.BotElement, measure, key),
        1 => Sum(StatisticGroup.HumanElement, measure, key), 2 => Sum(StatisticGroup.BotElement, measure, key),
        3 => Sum(StatisticGroup.EnemyElementDealt, measure, key), _ => Sum(StatisticGroup.Element, measure, key)
    };

    private double AttackSum(StatisticMeasure measure, int key) => _source.Selected switch
    {
        0 => Sum(StatisticGroup.HumanAttack, measure, key) + Sum(StatisticGroup.BotAttack, measure, key),
        1 => Sum(StatisticGroup.HumanAttack, measure, key), 2 => Sum(StatisticGroup.BotAttack, measure, key),
        3 => Sum(StatisticGroup.EnemyAttack, measure, key), _ => Sum(StatisticGroup.Attack, measure, key)
    };

    private double DamageSourceSum(StatisticMeasure measure, int element, StatisticDamageSource source)
    {
        int key = (int)source * 9 + element;
        double humans = Sum(StatisticGroup.HumanElementSource, measure, key), bots = Sum(StatisticGroup.BotElementSource, measure, key),
            enemies = Sum(StatisticGroup.EnemyElementSource, measure, key), environment = Sum(StatisticGroup.EnvironmentElementSource, measure, key);
        return _source.Selected switch { 0 => humans + bots, 1 => humans, 2 => bots, 3 => enemies, _ => humans + bots + enemies + environment };
    }

    private double SupportSum(StatisticMeasure measure) => _totals.Where(p => p.Key.Item1 == StatisticGroup.Actor && p.Key.Item3 == measure &&
        (_source.Selected switch { 0 => p.Key.Item2 < 64, 1 => p.Key.Item2 < 64 && p.Key.Item2 % 32 < 16,
            2 => p.Key.Item2 < 64 && p.Key.Item2 % 32 >= 16, 3 => p.Key.Item2 >= 64, _ => true })).Sum(p => p.Value);

    private static int CombinedActor(int actor) => actor < 64 ? actor & ~16 : actor;

    private double ActorSum(int actor, StatisticMeasure measure) => Sum(StatisticGroup.Actor, measure, actor) +
        (actor < 64 ? Sum(StatisticGroup.Actor, measure, actor + 16) : 0);
}
