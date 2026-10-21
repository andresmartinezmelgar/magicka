using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Arcana.Core;

public enum MatchRecordStatus { Playing, Completed, Left, Disconnected, Suspended, Interrupted }
public enum StatisticGroup { Total, Actor, Element, Attack, Recipe, Enemy, EnemyElement, Difficulty, Equipment, Relic, Performance, HumanElement, BotElement, EnemyElementDealt, HumanAttack, BotAttack, EnemyAttack, EnvironmentElement, EnvironmentAttack }
public enum StatisticMeasure { Damage, ShieldDamage, DamageReceived, ShieldAbsorbed, Overkill, ImmuneHits, Hits, Kills, Deaths, Spawns, Casts, Healing, HealingReceived, ShieldGranted, ShieldRepaired, AliveSeconds, WetSeconds, BurningSeconds, FrozenSeconds, FriendlyDamage, SelfDamage, Revives, Revived, Collected, GoldEarned, GoldSpent, Levels, SpawnHealth, Samples, ValueSum, Distance, Captures }
public enum StatisticAttack { Beam, Lightning, Spray, Rock, Shards, Force, Barrier, Melee, Self, Nova, Mine, Meteor, Haste, Teleport, Revive, FireAura, LightningAura, RegenerationAura, AutoMeteor, FrostNova, ChainLightning, Burn, EnemyMelee, EnemyArea, OracleBombardment, Regeneration }
public sealed record MatchStatistic(StatisticGroup Group, int Key, StatisticMeasure Measure, double Value);

/// <summary>Contrato cerrado: no acepta nombres, cuentas, direcciones ni identificadores de dispositivo.</summary>
public sealed record MatchStatisticsReport(int SchemaVersion, string RunId, long Revision, string GameVersion,
    string Day, GameMode Mode, ArenaMapId Map, bool Network, MatchRecordStatus Status, double Seconds,
    int Humans, int Bots, int Difficulty, int WinnerTeam, int[] Scores, MatchStatistic[] Metrics,
    int DroppedBuckets = 0, bool Continuation = false)
{
    public const int CurrentSchema = 1, MaxMetrics = 2048, MaxBytes = 240 * 1024;
    private static readonly JsonSerializerOptions StrictJson = new(Protocol.Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    [JsonIgnore] public bool Complete => Status == MatchRecordStatus.Completed;
    public double Sum(StatisticGroup group, StatisticMeasure measure, int? key = null) =>
        Metrics.Where(m => m.Group == group && m.Measure == measure && (key == null || m.Key == key)).Sum(m => m.Value);
    public string ToJson()
    {
        Validate();
        string json = JsonSerializer.Serialize(this, StrictJson);
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Informe demasiado grande.");
        return json;
    }
    public static MatchStatisticsReport Parse(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Informe demasiado grande.");
        var report = JsonSerializer.Deserialize<MatchStatisticsReport>(json, StrictJson) ?? throw new InvalidDataException("Informe vacío.");
        report.Validate(); return report;
    }
    public void Validate()
    {
        if (SchemaVersion != CurrentSchema || RunId == null || !Regex.IsMatch(RunId, "^[0-9a-f]{32}$") ||
            GameVersion == null || !Regex.IsMatch(GameVersion, "^[0-9]{1,3}[.][0-9]{1,3}[.][0-9]{1,3}$") ||
            Day == null || !DateOnly.TryParseExact(Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
            day.Year is < 2020 or > 2100 || Revision is < 0 or > 1892160000 || !double.IsFinite(Seconds) || Seconds < 0 ||
            Math.Abs(Seconds - Revision / 60.0) > 0.001 || Humans is < 0 or > 16 || Bots is < 0 or > 16 ||
            Difficulty is < 0 or > 1000000 || WinnerTeam is < -1 or > 1 || !Enum.IsDefined(Mode) || !Enum.IsDefined(Map) ||
            !Enum.IsDefined(Status) || Scores is not { Length: 2 } || Scores.Any(s => s is < 0 or > 1000000000) ||
            Metrics == null || Metrics.Length > MaxMetrics || DroppedBuckets < 0)
            throw new InvalidDataException("Cabecera de estadísticas inválida.");
        var seen = new HashSet<(StatisticGroup, int, StatisticMeasure)>();
        foreach (var m in Metrics)
            if (m == null || !Enum.IsDefined(m.Group) || !Enum.IsDefined(m.Measure) || !double.IsFinite(m.Value) ||
                m.Value < 0 || m.Value > 1e18 || !ValidKey(m.Group, m.Key) || !seen.Add((m.Group, m.Key, m.Measure)))
                throw new InvalidDataException("Métrica inválida o duplicada.");
    }
    private static bool ValidKey(StatisticGroup group, int key) => group switch
    {
        StatisticGroup.Total => key == 0,
        StatisticGroup.Actor => key >= 64 ? Enum.IsDefined((MinionKind)(key - 64)) : key >= 0 && key < 64 && Enum.IsDefined((WizardType)(key % 16)),
        StatisticGroup.Element or StatisticGroup.HumanElement or StatisticGroup.BotElement or StatisticGroup.EnemyElementDealt or StatisticGroup.EnvironmentElement => key is >= 0 and <= 8,
        StatisticGroup.Attack or StatisticGroup.HumanAttack or StatisticGroup.BotAttack or StatisticGroup.EnemyAttack or StatisticGroup.EnvironmentAttack => Enum.IsDefined((StatisticAttack)key),
        StatisticGroup.Recipe => ValidRecipe(key),
        StatisticGroup.Enemy => Enum.IsDefined((MinionKind)key),
        StatisticGroup.EnemyElement => key >= 0 && key % 9 <= 8 && Enum.IsDefined((MinionKind)(key / 9)),
        StatisticGroup.Difficulty => key is >= 0 and <= 1000000,
        StatisticGroup.Equipment => key is >= 0 and <= 10,
        StatisticGroup.Relic => Enum.IsDefined((BuffKind)key),
        StatisticGroup.Performance => key is >= 0 and <= 4,
        _ => false
    };
    private static bool ValidRecipe(int key)
    {
        if (key is < 0 or > 728) return false;
        int last = 9;
        while (key > 0) { int digit = key % 9; if (digit == 0 || digit > last) return false; last = digit; key /= 9; }
        return true;
    }
    private static int Rank(MatchRecordStatus status) => status == MatchRecordStatus.Completed ? 6 : (int)status;
    public static bool IsNewer(MatchStatisticsReport next, MatchStatisticsReport previous) =>
        next.RunId == previous.RunId && next.Mode == previous.Mode && next.Map == previous.Map && next.GameVersion == previous.GameVersion &&
        next.Day == previous.Day && !(previous.Complete && !next.Complete) &&
        (next.Revision > previous.Revision || next.Revision == previous.Revision && Rank(next.Status) > Rank(previous.Status));
}
