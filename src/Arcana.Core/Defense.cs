namespace Arcana.Core;

public enum DefenseFlow { Continuous = 1 }
public enum MinionKind { Melee, Archer, Mage, Captain, Tank, Boss, Runner, Brute, Bomber, Shaman, Warlock, Cryomancer, Sentinel }
public enum DefenseDropKind { Gold, Equipment, Relic }
public enum DefenseAura { Fire, Lightning, Regeneration }
public enum DefensePassive { Meteor, Berserker, FrostNova, ChainLightning }
internal enum CombatantRole { Player, Minion }

/// <summary>Reglas de la versión experimental. No alteran el catálogo ni el equilibrio PvP.</summary>
public static partial class DefenseRules
{
    public const int MaxDefenders = 4, MaxMinions = 192, MaxDrops = 96, InventoryLimit = 24;
    public const int PlayerLives = 2;
    public static readonly int MinionTypeCount = Enum.GetValues<MinionKind>().Length;
    public const float SafeRadius = 16, DropLifetime = 75, RewardInterval = 300;
    public const float SpeedBoostMultiplier = 1.6f;
    public static float BossInterval(Random random, bool first) => (first ? 120 : 90) + (float)random.NextDouble() * 60;
    public const int RewardLimit = 4, MaxAuraLevel = 10, MaxPassiveLevel = 5;
    // Moderate permanent improvement; subsequent personal shields and existing active
    // shields scale in the same way. Never amplifies PvP equipment.
    public static float ShieldLevelMultiplier(int level) =>
        1f + Math.Min(0.35f, Math.Max(0, level - 1) * 0.0125f);
    public const float CommanderSurgeSeconds = 13, CommanderSurgeCooldown = 50;
    public const int CommanderSurgeBonus = 112;
    public static int SurgeLimit(int stage, int party) =>
        Math.Min(MaxMinions, SpawnLimit(stage, party) + CommanderSurgeBonus);
    public const int GoldDropChance = 35, EquipmentDropChance = 6, RelicDropChance = 2;
    public const float ArrowPower = 0.4f, MagePower = 0.42f;
    public const float FreezeRecoverySeconds = 3;
    public static float MinionMeleeAttack(MinionKind kind) => kind switch
    { MinionKind.Boss => 280, MinionKind.Tank => 110, MinionKind.Captain => 95, MinionKind.Runner => 32,
        MinionKind.Brute => 180, MinionKind.Bomber => 260, MinionKind.Shaman => 30, MinionKind.Sentinel => 160, MinionKind.Warlock => 65, MinionKind.Cryomancer => 55, _ => 48 };
    public static float MinionRange(MinionKind kind) => kind switch
    { MinionKind.Archer or MinionKind.Mage or MinionKind.Warlock or MinionKind.Cryomancer => 10, MinionKind.Boss => 5.0f, MinionKind.Captain => 3.4f, MinionKind.Sentinel => 3.8f, MinionKind.Brute or MinionKind.Bomber => 2, _ => 1.4f };
    public static float AreaAttackRadius(MinionKind kind) => kind switch
    { MinionKind.Brute => 2.8f, MinionKind.Bomber => 3.2f, MinionKind.Captain => 3.4f,
        MinionKind.Sentinel => 3.8f, MinionKind.Boss => 5.0f, _ => 0 };
    public static float AttackWindup(MinionKind kind) => kind switch
    { MinionKind.Brute => 0.9f, MinionKind.Bomber => 1.1f, MinionKind.Captain => 1.05f,
        MinionKind.Sentinel => 1.2f, MinionKind.Boss => 1.6f, _ => 0 };
    public const float ShamanHealInterval = 3, ShamanHealRadius = 8, ShamanHealAmount = 90;
    public static float MinionSpeed(MinionKind kind, int stage) =>
        (0.58f + Math.Min(0.27f, stage * 0.012f)) * (kind switch
        { MinionKind.Tank => 0.65f, MinionKind.Boss => 0.8f, MinionKind.Runner => 1.45f,
            MinionKind.Brute => 0.6f, MinionKind.Bomber => 1.15f, MinionKind.Shaman => 0.85f, MinionKind.Sentinel => 0.72f, MinionKind.Warlock => 0.85f, MinionKind.Cryomancer => 0.95f, _ => 1 });
    public static float PartyPressure(int defenders) => 1 + (Math.Clamp(defenders, 1, MaxDefenders) - 1) * 0.3f;
    public static float SpawnInterval(int stage, int defenders, double variation) =>
        (0.32f + (float)Math.Clamp(variation, 0, 1) * 0.16f) /
        ((1 + Math.Max(0, stage - 1) * 0.07f) * PartyPressure(defenders));
    public static int SpawnLimit(int stage, int defenders) => Math.Min(MaxMinions,
        24 + (Math.Clamp(defenders, 1, MaxDefenders) - 1) * 8 + Math.Max(0, stage - 1) * 8);
    public static int TimedDifficulty(double seconds) => 1 + (int)(Math.Max(0, seconds) / 90);
    public static float EnemyStrength(int stage)
    {
        int growth = Math.Clamp(stage - 1, 0, 60);
        return MathF.Min(1000, MathF.Pow(1.18f, Math.Min(4, growth)) * MathF.Pow(1.22f, Math.Max(0, growth - 4)));
    }
    public static float AttackDelay(int stage) => Math.Max(0.4f, 1.35f - Math.Max(0, stage - 1) * 0.035f);
    public static float MinionHealth(MinionKind kind, int stage, int party) => (kind switch
    { MinionKind.Boss => 4200, MinionKind.Tank => 1200, MinionKind.Captain => 700, MinionKind.Mage => 380, MinionKind.Archer => 260,
        MinionKind.Runner => 200, MinionKind.Brute => 1500, MinionKind.Bomber => 240, MinionKind.Shaman => 540, MinionKind.Sentinel => 1900, MinionKind.Warlock => 920, MinionKind.Cryomancer => 760, _ => 340 })
        * EnemyStrength(stage) * (1 + (Math.Clamp(party, 1, MaxDefenders) - 1) * (kind == MinionKind.Boss ? 0.4f : 0.1f));
    public static int PassiveCost(int level) => 140 * (level + 1) * (level + 1);
    public static float BerserkerDuration(int level) => 4 + level * 0.5f;
    public static float BerserkerPower(int level) => 1.4f + level * 0.1f;
    public static string PassiveName(DefensePassive passive) => passive switch
    { DefensePassive.Meteor => "Lluvia de meteoritos", DefensePassive.Berserker => "Berserker",
        DefensePassive.FrostNova => "Pulso glacial", _ => "Cadena de relámpagos" };
    public static string PassiveDescription(DefensePassive passive, int level) => passive switch
    {
        DefensePassive.Meteor => $"Cada 3 s: meteorito de {260 * level} daño base en 3 m. Busca enemigos a 16 m.",
        DefensePassive.Berserker => $"Cada 16 s en combate: +{(BerserkerPower(level) - 1) * 100:0}% daño y +15% velocidad durante {BerserkerDuration(level):0.#} s.",
        DefensePassive.FrostNova => $"Cada 5 s: {65 * level} daño base y ralentización a enemigos en 5 m.",
        _ => $"Cada 4 s: {140 * level} daño base a un máximo de {Math.Min(6, 2 + level)} enemigos a 10 m."
    };
    public static int AuraCost(int level) => 80 * (level + 1) * (level + 1);
    public static int UpgradeCost(ItemTier tier, int upgrades)
    {
        if (upgrades is < 0 or >= ItemUpgradeLimit) throw new ArgumentException("Nivel de mejora inválido.");
        int basis = tier switch { ItemTier.Silver => 80, ItemTier.Gold => 120, ItemTier.Platinum => 180,
            ItemTier.Diamond => 260, ItemTier.Mythic => 400, _ => throw new ArgumentException("Calidad desconocida.") };
        return basis * (upgrades + 1) * (upgrades + 2) / 2;
    }
    public const int ItemUpgradeLimit = 10;
    public const float ItemUpgradeStep = 0.01f;
    public static int ExperienceForLevel(int level) => 60 + level * 40;
    public static float AuraRadius(int level) => level <= 0 ? 0 : 2.8f + level * 0.25f;
    public static string AuraName(DefenseAura aura) => aura switch
    { DefenseAura.Fire => "Aura de fuego", DefenseAura.Lightning => "Aura eléctrica", _ => "Aura de regeneración" };
    public static string MinionName(MinionKind kind) => kind switch
    { MinionKind.Melee => "Minion", MinionKind.Archer => "Arquero", MinionKind.Mage => "Mago invasor", MinionKind.Tank => "Tanque", MinionKind.Boss => "Coloso",
        MinionKind.Runner => "Corredor", MinionKind.Brute => "Bruto", MinionKind.Bomber => "Detonador", MinionKind.Shaman => "Chamán", MinionKind.Warlock => "Conjurador ígneo", MinionKind.Cryomancer => "Tejedor glacial", MinionKind.Sentinel => "Guardián rúnico", _ => "Comandante" };
    public static string Clock(double seconds) => $"{(int)Math.Max(0, seconds) / 60:00}:{(int)Math.Max(0, seconds) % 60:00}";
}

internal sealed class DefenseProgress
{
    public Wizard? CombatState;
    public int Level = 1, Experience, Gold, Kills, Deaths;
    public long ActiveTicks;
    public readonly Dictionary<string, long> BestiaryKills = new(StringComparer.Ordinal);
    public bool ShopOpen;
    public DefensePerformanceBudget Performance = new();
    public double LastPerformanceReport = -100;
    public float BerserkerRemaining;
    public readonly int[] Passives = new int[4];
    public readonly float[] PassiveCooldowns = new float[4];
    public double LastActivity = -100;
    public readonly List<EquipmentItem> Items = [];
    public EquipmentLoadout Loadout = new();
    public readonly Dictionary<string, int> Upgrades = [];
    public readonly int[] Auras = new int[3];
    public readonly int[] Buffs = new int[RelicBuffs.Count];
}

internal sealed class DefenseMinion(Wizard body, MinionKind kind, float strength)
{
    public readonly Wizard Body = body;
    public readonly MinionKind Kind = kind;
    public readonly float Strength = strength;
    public float Attack = 1, ChannelTime, SpeedBoost, SpeedClock, NavigationClock, Windup, SupportCooldown = 3, TacticClock, ShieldCooldown;
    public int SpellTurn;
    public V2 Navigation;
    public int NeighborhoodOrder;
}

internal sealed class DefenseDrop
{
    public int Id, OwnerId, Gold;
    public V2 Position;
    public DefenseDropKind Kind;
    public EquipmentItem? Item;
    public BuffKind Buff;
    public float Remaining = DefenseRules.DropLifetime;
}

public sealed record MinionSnapshot(int Id, MinionKind Kind, V2 Position, V2 Facing, float Health, float MaxHealth,
    float Wet, float Burning, float Chill, float Frozen, bool Casting, Element[] Elements, bool Empowered)
{
    public float SpeedBoostSeconds { get; init; }
    public float AttackWarningSeconds { get; init; }
}
public sealed record DefenseEnemySnapshot(MinionKind Kind, int Difficulty, float MaxHealth, float Attack, float AttackMaximum,
    float AttackInterval, float Speed, float Range, int Spawned, int Killed);
public sealed record DefenseDropSnapshot(int Id, int OwnerId, V2 Position, DefenseDropKind Kind, int Gold,
    BuffKind Buff, EquipmentSlot? Slot, ItemTier? Tier);
public sealed record DefenseItemSnapshot(EquipmentItem Item, int Upgrades, bool Equipped);
public sealed record DefensePlayerSnapshot(int PlayerId, int Level, int Experience, int NextLevelExperience, int Gold,
    double ActiveSeconds, int Kills, int Deaths, int[] Auras, DefenseItemSnapshot[] Items, EquipmentModifiers Modifiers,
    float DamageMultiplier, float IncomingMultiplier, float Regeneration, float SpeedMultiplier,
    int[] Passives, float[] PassiveCooldowns, float BerserkerRemaining)
{
    public int LivesRemaining => Math.Max(0, DefenseRules.PlayerLives - Deaths);
}
public sealed record DefenseSnapshot(double Elapsed, int Difficulty,
    int TotalKills, MinionSnapshot[] Minions, DefenseDropSnapshot[] Drops, DefensePlayerSnapshot[] Defenders, bool Paused, int HumanCount)
{
    public DefenseEnemySnapshot[] Bestiary { get; init; } = [];
    public float NextBossIn { get; init; }
    public int BossesKilled { get; init; }
    public int MinionLimit { get; init; } = DefensePerformanceBudget.DefaultLimit;
    public int PerformanceLimit { get; init; } = DefensePerformanceBudget.DefaultLimit;
}

public static class DefenseVisibility
{
    /// <summary>En red solo se replica el inventario y el botín asignado al destinatario. Los bonos públicos y auras siguen visibles.</summary>
    public static WorldSnapshot ForPlayer(WorldSnapshot state, int playerId) => state.Defense is not { } d ? state
        : state with { Defense = d with
        {
            Defenders = d.Defenders.Select(p => p.PlayerId == playerId ? p : p with { Items = [] }).ToArray(),
            Drops = d.Drops.Where(p => p.OwnerId == 0 || p.OwnerId == playerId).ToArray()
        } };
}
