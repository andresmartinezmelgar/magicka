using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class SavedSurvival
    {
        public WorldOptions Options = new();
        public SurvivalMapSpec? SurvivalMap;
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public MatchStatisticsReport? Statistics = null; // Solo lectura de puntos antiguos; nunca se escribe telemetría nueva.
        public long Tick, NextEvent;
        public int NextMinion, NextEntity, Kills, Bosses, PartySize;
        public ulong RandomState;
        public double Elapsed;
        public float SpawnClock, AuraClock, BossClock, SurgeTime, SurgeCooldown, SurgeClock, BotClock;
        public float OracleSpawnCooldown;
        public float SpecialSpawnCooldown;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public float ColossusMomentumRemaining;
        public float PrismSpawnCooldown = DefenseRules.PrismMinimumSpawnGap;
        public int Reinforcements;
        public SavedDefender[] Defenders = [];
        public SavedMinion[] Minions = [];
        public SavedEffectSource[] EffectSources = [];
        public SavedProjectile[] Projectiles = [];
        public SavedWall[] Walls = [];
        public SavedField[] Fields = [];
        public SavedDrop[] Drops = [];
        public DefenseEnemySnapshot[] Bestiary = [];
        public SavedBotPlan[] BotPlans = [];
        public ArcaneBurst[] ArcaneBursts = [];
    }

    private sealed record SavedDefender(SurvivalWizard Body, SavedProgress Progress);

    private sealed class SavedProgress
    {
        public int Level, Experience, Gold;
        public int GoldCoinRemainder, GoldPercentRemainder;
        public int GoldCoinRemainderScale;
        public long ActiveTicks;
        public Dictionary<string, long> BestiaryKills = new(StringComparer.Ordinal);
        public float BerserkerRemaining;
        public int[] Passives = [], Auras = [];
        public float[] PassiveCooldowns = [];
        public double LastActivity;
        public EquipmentItem[] Items = [];
        public string[] Favorites = [];
        private string[] _equippedItemHistory = [];
        [JsonIgnore] public bool HasEquippedItemHistory { get; private set; }
        public string[] EquippedItemHistory
        {
            get => _equippedItemHistory;
            set
            {
                _equippedItemHistory = value;
                HasEquippedItemHistory = true;
            }
        }
        public EquipmentStyle[] AutoSellStyles = [];
        public int AutoSellRuleVersion;
        public int AutoSold;
        public SurvivalEquipmentSetup EquipmentSetup = new();
        public bool EquipmentSetupInitialized;
        public EquipmentSlot[] ManualEquipmentSlots = [];
        public EquipmentLoadout Loadout = new();
        public Dictionary<string, int> Upgrades = [];
    }

    private sealed record SavedMinion(SurvivalWizard Body, MinionKind Kind, float Strength, float Attack,
        float ChannelTime, float SpeedBoost, float SpeedClock, float NavigationClock, V2 Navigation,
        float Windup, float SupportCooldown, float ShieldCooldown, int SpellTurn)
    {
        public int PartySize { get; init; }
        public int FocusTargetId { get; init; }
        public float FocusTime { get; init; }
        public float AuraRepairCooldown { get; init; }
        public DefenseSpecialAbility SpecialAbility { get; init; }
        public float SpecialShieldSeconds { get; init; }
        public float SpecialShieldCooldown { get; init; }
    }

    private sealed record SavedProjectile(int Id, int Owner, V2 Position, V2 Velocity, Element[] Elements, float Radius, float Power, float Lifetime)
    {
        public int RockFragmentation { get; init; }
        public bool IsRockFragment { get; init; }
    }

    private sealed record SavedEffectSource(int Id, MinionKind Kind, float Power);

    private sealed record SavedWall(int Id, int Owner, V2 A, V2 B, float Health, float Lifetime, Element[] Elements);

    private sealed record SavedField(int Id, int Owner, V2 Position, float Radius, float Lifetime, float Delay,
        float TriggerRadius, float Power, Element[] Elements, string Kind)
    {
        public bool IsRockFragmentArea { get; init; }
    }

    private sealed record SavedDrop(int Id, int OwnerId, int Gold, V2 Position, DefenseDropKind Kind,
        EquipmentItem? Item, BuffKind Buff, float Remaining)
    {
        public bool SpecialReward { get; init; }
    }

    private sealed record SavedBotPlan(int Id, int TargetId, int HealTargetId, V2 Goal, float SpellTime, int SpellTurn);
}
