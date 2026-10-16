namespace Arcana.Core;

public enum DefenseFlow { Waves, Continuous }
public enum MinionKind { Melee, Archer, Mage, Captain, Tank }
public enum DefenseDropKind { Gold, Equipment, Relic }
public enum DefenseAura { Fire, Lightning, Regeneration }
public enum DefensePassive { Meteor, Berserker, FrostNova, ChainLightning }
internal enum CombatantRole { Player, Minion, Castle }

/// <summary>Reglas de la versión experimental. No alteran el catálogo ni el equilibrio PvP.</summary>
public static class DefenseRules
{
    public const int MaxDefenders = 4, MaxMinions = 64, MaxDrops = 96, InventoryLimit = 24;
    public const float CastleHealth = 12000, CastleRadius = 2.5f, SafeRadius = 20;
    public const float WavePause = 8, DropLifetime = 75, RewardInterval = 300;
    public const int RewardLimit = 4, MaxAuraLevel = 10, MaxPassiveLevel = 5;
    public static V2 CastlePosition => new(-32, 0);
    public static IReadOnlyList<V2> Entrances { get; } = Array.AsReadOnly<V2>([new(32, -18), new(34, 0), new(32, 18), new(4, -20), new(4, 20)]);
    public static int WaveSize(int wave) => wave switch
    { <= 3 => 20, 4 => 50, 5 => 100, 6 => 200, _ => Math.Min(500, 200 + (wave - 6) * 25) };
    public static int WaveSize(int wave, int defenders) =>
        (WaveSize(wave) * (10 + 3 * (Math.Clamp(defenders, 1, MaxDefenders) - 1)) + 9) / 10;
    public static float PartyPressure(int defenders) => 1 + (Math.Clamp(defenders, 1, MaxDefenders) - 1) * 0.3f;
    public static int SpawnLimit(int stage, int defenders) => Math.Min(MaxMinions,
        8 + (Math.Clamp(defenders, 1, MaxDefenders) - 1) * 4 + Math.Max(0, stage - 1) * 2);
    public static int TimedDifficulty(double seconds) => 1 + (int)(Math.Max(0, seconds) / 90);
    public static float EnemyStrength(int stage)
    {
        int growth = Math.Clamp(stage - 1, 0, 60);
        return MathF.Min(1000, MathF.Pow(1.1f, Math.Min(4, growth)) * MathF.Pow(1.14f, Math.Max(0, growth - 4)));
    }
    public static float AttackDelay(int stage) => Math.Max(0.4f, 1.35f - Math.Max(0, stage - 1) * 0.035f);
    public static float MinionHealth(MinionKind kind, int stage, int party) => (kind switch
    { MinionKind.Tank => 1200, MinionKind.Captain => 700, MinionKind.Mage => 380, MinionKind.Archer => 260, _ => 340 })
        * EnemyStrength(stage) * (1 + (Math.Clamp(party, 1, MaxDefenders) - 1) * 0.1f);
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
    public static int RepairCost(int repairs) => (int)Math.Min(100000000, Math.Ceiling(100 * Math.Pow(1.5, Math.Min(35, Math.Max(0, repairs)))));
    public static int AuraCost(int level) => 80 * (level + 1) * (level + 1);
    public static int UpgradeCost(int upgrades) => 35 * (upgrades + 1);
    public static int ExperienceForLevel(int level) => 60 + level * 40;
    public static float AuraRadius(int level) => level <= 0 ? 0 : 2.8f + level * 0.25f;
    public static string AuraName(DefenseAura aura) => aura switch
    { DefenseAura.Fire => "Aura de fuego", DefenseAura.Lightning => "Aura eléctrica", _ => "Aura de regeneración" };
    public static string MinionName(MinionKind kind) => kind switch
    { MinionKind.Melee => "Minion", MinionKind.Archer => "Arquero", MinionKind.Mage => "Mago invasor", MinionKind.Tank => "Tanque", _ => "Capitán" };
    public static string Clock(double seconds) => $"{(int)Math.Max(0, seconds) / 60:00}:{(int)Math.Max(0, seconds) % 60:00}";
}

internal sealed class DefenseProgress
{
    public int Level = 1, Experience, Gold, Kills, Deaths;
    public long ActiveTicks;
    public bool ShopOpen;
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
    public float Attack = 1, ChannelTime;
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
    float Wet, float Burning, float Chill, float Frozen, bool Casting, Element[] Elements, bool Empowered);
public sealed record DefenseDropSnapshot(int Id, int OwnerId, V2 Position, DefenseDropKind Kind, int Gold,
    BuffKind Buff, EquipmentSlot? Slot, ItemTier? Tier);
public sealed record DefenseItemSnapshot(EquipmentItem Item, int Upgrades, bool Equipped);
public sealed record DefensePlayerSnapshot(int PlayerId, int Level, int Experience, int NextLevelExperience, int Gold,
    double ActiveSeconds, int Kills, int Deaths, int[] Auras, DefenseItemSnapshot[] Items, EquipmentModifiers Modifiers,
    float DamageMultiplier, float IncomingMultiplier, float Regeneration, float SpeedMultiplier,
    int[] Passives, float[] PassiveCooldowns, float BerserkerRemaining);
public sealed record DefenseSnapshot(DefenseFlow Flow, double Elapsed, int Wave, int Difficulty, int RemainingToSpawn,
    float NextWaveIn, V2 CastlePosition, float CastleHealth, float CastleMaxHealth, int Repairs, int RepairCost,
    int TotalKills, MinionSnapshot[] Minions, DefenseDropSnapshot[] Drops, DefensePlayerSnapshot[] Defenders, bool Paused, int HumanCount);

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
