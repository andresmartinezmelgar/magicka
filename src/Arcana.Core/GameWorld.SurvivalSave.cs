using System.Text.Json;

namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class SavedSurvival
    {
        public WorldOptions Options = new();
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public MatchStatisticsReport? Statistics = null; // Solo lectura de puntos antiguos; nunca se escribe telemetría nueva.
        public long Tick, NextEvent;
        public int NextMinion, NextEntity, Kills, Bosses, PartySize;
        public ulong RandomState;
        public double Elapsed;
        public float SpawnClock, AuraClock, BossClock, SurgeTime, SurgeCooldown, SurgeClock, BotClock;
        public int Reinforcements;
        public SavedDefender[] Defenders = [];
        public SavedMinion[] Minions = [];
        public SavedProjectile[] Projectiles = [];
        public SavedWall[] Walls = [];
        public SavedField[] Fields = [];
        public SavedDrop[] Drops = [];
        public DefenseEnemySnapshot[] Bestiary = [];
        public SavedBotPlan[] BotPlans = [];
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
        public EquipmentStyle[] AutoSellStyles = [];
        public int AutoSold;
        public EquipmentLoadout Loadout = new();
        public Dictionary<string, int> Upgrades = [];
    }
    private sealed record SavedMinion(SurvivalWizard Body, MinionKind Kind, float Strength, float Attack,
        float ChannelTime, float SpeedBoost, float SpeedClock, float NavigationClock, V2 Navigation,
        float Windup, float SupportCooldown, float ShieldCooldown, int SpellTurn)
    {
        public int FocusTargetId { get; init; }
        public float FocusTime { get; init; }
        public float AuraRepairCooldown { get; init; }
    }
    private sealed record SavedProjectile(int Id, int Owner, V2 Position, V2 Velocity, Element[] Elements, float Radius, float Power, float Lifetime);
    private sealed record SavedWall(int Id, int Owner, V2 A, V2 B, float Health, float Lifetime, Element[] Elements);
    private sealed record SavedField(int Id, int Owner, V2 Position, float Radius, float Lifetime, float Delay,
        float TriggerRadius, float Power, Element[] Elements, string Kind);
    private sealed record SavedDrop(int Id, int OwnerId, int Gold, V2 Position, DefenseDropKind Kind,
        EquipmentItem? Item, BuffKind Buff, float Remaining);
    private sealed record SavedBotPlan(int Id, int TargetId, int HealTargetId, V2 Goal, float SpellTime, int SpellTurn);

    /// <summary>Solo una supervivencia local, con un humano y sus aliados IA, puede continuar en otro arranque.</summary>
    public SurvivalSave SaveSurvival(int localPlayerId, string runId, string bestiaryUid = "")
    {
        if (!IsDefense || MatchEnded || _players.Values.Count(p => !p.Bot) != 1 ||
            !_players.TryGetValue(localPlayerId, out var local) || local.Bot || local.Deaths >= DefenseRules.PlayerLives)
            throw new InvalidOperationException("Solo se puede guardar una supervivencia local en curso.");
        var state = new SavedSurvival
        {
            Options = Options, Tick = Tick, NextEvent = _nextEvent, NextMinion = _nextMinion, NextEntity = _nextEntity,
            RandomState = ((SurvivalRandom)_random).State, Kills = _defenseKills, Bosses = _bossesKilled,
            Elapsed = _defenseElapsed, PartySize = _defensePartySize, SpawnClock = _spawnClock, AuraClock = _auraClock,
            BossClock = _bossClock, SurgeTime = _commanderSurgeTime, SurgeCooldown = _commanderSurgeCooldown,
            SurgeClock = _commanderSpawnClock, Reinforcements = _commanderReinforcements, BotClock = _defenseBotPlanClock,
            Defenders = _players.Values.Select(p =>
            {
                var s = _defenders[p.Id];
                return new SavedDefender(SurvivalWizard.Capture(p), new()
                {
                    Level = s.Level, Experience = s.Experience, Gold = s.Gold, ActiveTicks = s.ActiveTicks,
                    GoldCoinRemainder = s.GoldCoinRemainder, GoldPercentRemainder = s.GoldPercentRemainder,
                    GoldCoinRemainderScale = GoldBonuses.CoinFractionScale,
                    BestiaryKills = new(s.BestiaryKills, StringComparer.Ordinal), BerserkerRemaining = s.BerserkerRemaining,
                    Passives = s.Passives.ToArray(), PassiveCooldowns = s.PassiveCooldowns.ToArray(), LastActivity = s.LastActivity,
                    Items = s.Items.ToArray(), Loadout = s.Loadout, Upgrades = new(s.Upgrades), Auras = s.Auras.ToArray(),
                    Favorites = s.Favorites.ToArray(), AutoSellStyles = s.AutoSellStyles.ToArray(), AutoSold = s.AutoSold
                });
            }).ToArray(),
            Minions = _minions.Values.Where(m => m.Body.Alive).Select(m => new SavedMinion(SurvivalWizard.Capture(m.Body),
                m.Kind, m.Strength, m.Attack, m.ChannelTime, m.SpeedBoost, m.SpeedClock, m.NavigationClock, m.Navigation,
                m.Windup, m.SupportCooldown, m.ShieldCooldown, m.SpellTurn)
                { FocusTargetId = _players.ContainsKey(m.FocusTargetId) ? m.FocusTargetId : 0,
                    FocusTime = m.FocusTime, AuraRepairCooldown = m.AuraRepairCooldown }).ToArray(),
            Projectiles = _projectiles.Select(p => new SavedProjectile(p.Id, p.Owner, p.Position, p.Velocity,
                p.Recipe.Elements, p.Radius, p.Power, p.Lifetime)).ToArray(),
            Walls = _walls.Select(w => new SavedWall(w.Id, w.Owner, w.A, w.B, w.Health, w.Lifetime, w.Recipe.Elements)).ToArray(),
            Fields = _fields.Select(f => new SavedField(f.Id, f.Owner, f.Position, f.Radius, f.Lifetime, f.Delay,
                f.TriggerRadius, f.Power, f.Recipe.Elements, f.Kind)).ToArray(),
            Drops = _defenseDrops.Select(d => new SavedDrop(d.Id, d.OwnerId, d.Gold, d.Position, d.Kind, d.Item, d.Buff, d.Remaining)).ToArray(),
            Bestiary = _defenseBestiary.Values.ToArray(),
            BotPlans = _defenseBotPlans.Select(p => new SavedBotPlan(p.Key, p.Value.TargetId, p.Value.HealTargetId,
                p.Value.Goal, p.Value.SpellTime, p.Value.SpellTurn)).ToArray()
        };
        // JsonElement realiza una copia profunda: seguir jugando no modifica el punto guardado.
        var save = new SurvivalSave(SurvivalSave.CurrentVersion, DateTimeOffset.UtcNow, runId, bestiaryUid,
            localPlayerId, JsonSerializer.SerializeToElement(state, SurvivalSave.Json));
        ValidateSurvivalSave(save);
        return save;
    }

    public static GameWorld RestoreSurvival(SurvivalSave save)
    {
        var s = ReadSurvivalSave(save);
        // Los aliados se restauran explícitamente, sin crear bots adicionales en el constructor.
        var world = new GameWorld(new()
        {
            Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = s.Options.MaxPlayers,
            FriendlyFire = s.Options.FriendlyFire, DefenseFlow = s.Options.DefenseFlow, Seed = s.Options.Seed,
            ScoreLimit = s.Options.ScoreLimit, Tickets = s.Options.Tickets
        });
        world.Tick = s.Tick; world._nextEvent = s.NextEvent; world._nextMinion = s.NextMinion; world._nextEntity = s.NextEntity;
        world._defenseElapsed = s.Elapsed; world._defenseDifficulty = DefenseRules.TimedDifficulty(s.Elapsed);
        world._defensePartySize = s.PartySize; world._defenseKills = s.Kills; world._bossesKilled = s.Bosses;
        world._spawnClock = s.SpawnClock; world._auraClock = s.AuraClock; world._bossClock = s.BossClock;
        world._commanderSurgeTime = s.SurgeTime; world._commanderSurgeCooldown = s.SurgeCooldown;
        world._commanderSpawnClock = s.SurgeClock; world._commanderReinforcements = s.Reinforcements;
        foreach (var entry in s.Defenders)
        {
            var p = entry.Body.Restore(false, world.Time); var d = entry.Progress;
            var progress = new DefenseProgress
            {
                Level = d.Level, Experience = d.Experience, Gold = d.Gold, ActiveTicks = d.ActiveTicks,
                GoldCoinRemainder = d.GoldCoinRemainderScale == 0
                    ? d.GoldCoinRemainder * (GoldBonuses.CoinFractionScale / 2) : d.GoldCoinRemainder,
                GoldPercentRemainder = d.GoldPercentRemainder,
                Kills = p.Kills, Deaths = p.Deaths, BerserkerRemaining = d.BerserkerRemaining,
                LastActivity = d.LastActivity, Loadout = d.Loadout, CombatState = p
            };
            d.Passives.CopyTo(progress.Passives, 0); d.PassiveCooldowns.CopyTo(progress.PassiveCooldowns, 0);
            d.Auras.CopyTo(progress.Auras, 0); p.BuffStacks.CopyTo(progress.Buffs, 0);
            progress.Items.AddRange(d.Items);
            progress.Favorites.UnionWith(d.Favorites); progress.AutoSellStyles.UnionWith(d.AutoSellStyles); progress.AutoSold = d.AutoSold;
            foreach (var pair in d.Upgrades) progress.Upgrades.Add(pair.Key, pair.Value);
            foreach (var pair in d.BestiaryKills) progress.BestiaryKills.Add(pair.Key, pair.Value);
            world._players.Add(p.Id, p); world._combatants.Add(p.Id, p); world._orderedPlayers.Add(p);
            world._defenders.Add(p.Id, progress);
            // Derivar los bonos del inventario; la salud y el escudo conservan su daño exacto.
            world.RecalculateDefender(p);
            p.Health = entry.Body.Health; p.Shield = entry.Body.Shield; p.ShieldMax = entry.Body.ShieldMax;
        }
        foreach (var m in s.Minions)
        {
            var p = m.Body.Restore(true, world.Time);
            world._minions.Add(p.Id, new(p, m.Kind, m.Strength)
            {
                Attack = m.Attack, ChannelTime = m.ChannelTime, SpeedBoost = m.SpeedBoost, SpeedClock = m.SpeedClock,
                NavigationClock = m.NavigationClock, Navigation = m.Navigation, Windup = m.Windup,
                SupportCooldown = m.SupportCooldown, ShieldCooldown = m.ShieldCooldown, SpellTurn = m.SpellTurn,
                FocusTargetId = m.FocusTargetId, FocusTime = m.FocusTime, AuraRepairCooldown = m.AuraRepairCooldown
            });
            world._combatants.Add(p.Id, p); world._orderedPlayers.Add(p);
        }
        foreach (var p in s.Projectiles) world._projectiles.Add(new() { Id = p.Id, Owner = p.Owner, Position = p.Position,
            Velocity = p.Velocity, Recipe = new(p.Elements), Radius = p.Radius, Power = p.Power, Lifetime = p.Lifetime });
        foreach (var w in s.Walls) world._walls.Add(new() { Id = w.Id, Owner = w.Owner, A = w.A, B = w.B,
            Health = w.Health, Lifetime = w.Lifetime, Recipe = new(w.Elements) });
        foreach (var f in s.Fields) world._fields.Add(new() { Id = f.Id, Owner = f.Owner, Position = f.Position,
            Radius = f.Radius, Lifetime = f.Lifetime, Delay = f.Delay, TriggerRadius = f.TriggerRadius,
            Power = f.Power, Recipe = new(f.Elements), Kind = f.Kind });
        foreach (var d in s.Drops) world._defenseDrops.Add(new() { Id = d.Id, OwnerId = d.OwnerId, Gold = d.Gold,
            Position = d.Position, Kind = d.Kind, Item = d.Item, Buff = d.Buff, Remaining = d.Remaining });
        foreach (var b in s.Bestiary) world._defenseBestiary.Add(b.Kind, b);
        foreach (var b in s.BotPlans) world._defenseBotPlans.Add(b.Id, new()
            { TargetId = b.TargetId, HealTargetId = b.HealTargetId, Goal = b.Goal, SpellTime = b.SpellTime, SpellTurn = b.SpellTurn });
        world._defenseBotPlanClock = s.BotClock;
        ((SurvivalRandom)world._random).State = s.RandomState;
        world.RestoreStatistics(null);
        world.BuildDefenseCells();
        return world;
    }

    internal static void ValidateSurvivalSave(SurvivalSave save) => ReadSurvivalSave(save);

    private static SavedSurvival ReadSurvivalSave(SurvivalSave save)
    {
        static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid)
        { if (!valid) throw new InvalidDataException("Partida de supervivencia incompleta o incompatible."); }
        Require(save != null && save.Version == SurvivalSave.CurrentVersion && Guid.TryParseExact(save.RunId, "N", out _) &&
            save.BestiaryUid is { Length: <= 128 } && save.State.ValueKind == JsonValueKind.Object);
        var s = save!.State.Deserialize<SavedSurvival>(SurvivalSave.Json);
        Require(s != null && s.Options != null && s.Options.Mode == GameMode.Defense && s.Options.Map == ArenaMapId.Bastion &&
            s.Options.MaxPlayers is >= 1 and <= DefenseRules.MaxDefenders && Enum.IsDefined(s.Options.DefenseFlow) &&
            s.Defenders is { Length: >= 1 and <= DefenseRules.MaxDefenders } && s.Defenders.Length <= s.Options.MaxPlayers &&
            s.Minions is { Length: <= DefenseRules.MaxMinions } && s.Drops is { Length: <= DefenseRules.MaxDrops } &&
            s.Projectiles is { Length: <= 2048 } && s.Walls is { Length: <= 2048 } && s.Fields is { Length: <= 2048 } &&
            s.Bestiary != null && s.Bestiary.Length <= DefenseRules.MinionTypeCount && s.BotPlans is { Length: <= 3 });
        Require(s!.Tick is >= 0 and <= 1892160000 && double.IsFinite(s.Elapsed) && s.Elapsed is >= 0 and <= 31536000 &&
            s.Kills >= 0 && s.Bosses >= 0 && s.PartySize is >= 1 and <= 4 && s.RandomState != 0 && s.NextEvent > 0 &&
            new[] { s.SpawnClock, s.AuraClock, s.BossClock, s.SurgeTime, s.SurgeCooldown, s.SurgeClock, s.BotClock }.All(float.IsFinite));
        s.Statistics?.Validate();
        Require(s.Statistics == null || s.Statistics.Mode == GameMode.Defense && s.Statistics.Map == s.Options.Map && !s.Statistics.Complete);
        var ids = new HashSet<int>();
        foreach (var entry in s.Defenders)
        {
            Require(entry?.Body != null && entry.Body.Valid() && entry.Body.VulnerableElement == null && ids.Add(entry.Body.Id) && entry.Progress != null);
            var d = entry!.Progress;
            Require(d.Level is >= 1 and <= 100 && d.Experience >= 0 && d.Gold is >= 0 and <= 100000000 &&
                (d.GoldCoinRemainderScale == 0 ? d.GoldCoinRemainder is >= 0 and <= 1 :
                    d.GoldCoinRemainderScale == GoldBonuses.CoinFractionScale && d.GoldCoinRemainder is >= 0 and < GoldBonuses.CoinFractionScale) &&
                d.GoldPercentRemainder is >= 0 and <= 99 &&
                d.ActiveTicks >= 0 && d.ActiveTicks <= s.Tick && double.IsFinite(d.LastActivity) && float.IsFinite(d.BerserkerRemaining) &&
                d.Passives is { Length: 4 } && d.Passives.All(n => n is >= 0 and <= DefenseRules.MaxPassiveLevel) &&
                d.PassiveCooldowns is { Length: 4 } && d.PassiveCooldowns.All(float.IsFinite) &&
                d.Auras is { Length: 3 } && d.Auras.All(n => n is >= 0 and <= DefenseRules.MaxAuraLevel) &&
                d.Items is { Length: <= DefenseRules.InventoryLimit } && d.Items.All(EquipmentCatalogue.Valid) &&
                d.Favorites is { Length: <= DefenseRules.InventoryLimit } && d.Favorites.Distinct().Count() == d.Favorites.Length &&
                d.Favorites.All(id => d.Items.Any(i => i.Id == id)) && d.AutoSold >= 0 &&
                d.AutoSellStyles != null && d.AutoSellStyles.Length <= EquipmentStyle.Limit && d.AutoSellStyles.All(s => s.Valid) &&
                d.AutoSellStyles.Distinct().Count() == d.AutoSellStyles.Length &&
                d.Items.Select(i => i.Id).Distinct().Count() == d.Items.Length && d.Loadout != null && d.Upgrades != null &&
                d.Upgrades.All(p => d.Items.Any(i => i.Id == p.Key) && p.Value is >= 0 and <= DefenseRules.ItemUpgradeLimit) &&
                d.BestiaryKills != null && d.BestiaryKills.All(p => Enum.GetValues<MinionKind>().Any(k => BestiaryCatalogue.Key(k) == p.Key) && p.Value >= 0));
            // Reutilizar la validación de ranuras y pertenencia del inventario normal.
            AccountInventory.ApplyLoadout(new("", entry.Body.Name, d.Gold, 0, 0, d.Loadout, d.Items), d.Loadout);
        }
        Require(s.Defenders.Count(d => !d.Body.Bot) == 1 && s.Defenders.Any(d =>
            d.Body.Id == save.LocalPlayerId && !d.Body.Bot && d.Body.Deaths < DefenseRules.PlayerLives));
        foreach (var m in s.Minions)
            Require(m?.Body != null && m.Body.Valid() && m.Body.Bot && ids.Add(m.Body.Id) && Enum.IsDefined(m.Kind) &&
                m.Navigation.IsFinite && m.Strength > 0 &&
                (m.Kind == MinionKind.Prism ? m.Body.VulnerableElement.HasValue : !m.Body.VulnerableElement.HasValue) &&
                (m.FocusTargetId == 0 || s.Defenders.Any(d => d.Body.Id == m.FocusTargetId)) &&
                float.IsFinite(m.FocusTime) && m.FocusTime is >= 0 and <= DefenseRules.PrismFocusSeconds &&
                float.IsFinite(m.AuraRepairCooldown) && m.AuraRepairCooldown is >= 0 and <= DefenseRules.OracleShieldInterval &&
                new[] { m.Strength, m.Attack, m.ChannelTime, m.SpeedBoost,
                    m.SpeedClock, m.NavigationClock, m.Windup, m.SupportCooldown, m.ShieldCooldown }.All(float.IsFinite));
        Require(s.NextMinion > ids.Max() && s.NextMinion < int.MaxValue - 1);
        var entities = new HashSet<int>();
        bool Entity(int id) => id > 0 && entities.Add(id);
        bool Elements(Element[] e) => SurvivalWizard.ValidElements(e);
        foreach (var p in s.Projectiles) Require(p != null && Entity(p.Id) && p.Position.IsFinite && p.Velocity.IsFinite &&
            Elements(p.Elements) && float.IsFinite(p.Radius) && p.Radius > 0 && float.IsFinite(p.Power) && float.IsFinite(p.Lifetime));
        foreach (var w in s.Walls) Require(w != null && Entity(w.Id) && w.A.IsFinite && w.B.IsFinite && Elements(w.Elements) &&
            float.IsFinite(w.Health) && float.IsFinite(w.Lifetime));
        foreach (var f in s.Fields) Require(f != null && Entity(f.Id) && f.Position.IsFinite && Elements(f.Elements) && f.Kind is { Length: > 0 and < 48 } &&
            new[] { f.Radius, f.Lifetime, f.Delay, f.TriggerRadius, f.Power }.All(float.IsFinite));
        foreach (var d in s.Drops) Require(d != null && Entity(d.Id) && d.Position.IsFinite && Enum.IsDefined(d.Kind) && Enum.IsDefined(d.Buff) &&
            float.IsFinite(d.Remaining) && (d.Kind != DefenseDropKind.Equipment || d.Item != null && EquipmentCatalogue.Valid(d.Item)));
        Require(s.NextEntity > entities.DefaultIfEmpty(0).Max() && s.NextEntity < int.MaxValue - 1);
        Require(s.Bestiary.All(b => b != null && Enum.IsDefined(b.Kind)) && s.Bestiary.Select(b => b.Kind).Distinct().Count() == s.Bestiary.Length &&
            s.Minions.All(m => s.Bestiary.Any(b => b.Kind == m.Kind)) && s.BotPlans.All(b => b != null &&
                s.Defenders.Any(d => d.Body.Bot && d.Body.Id == b.Id) && b.Goal.IsFinite && float.IsFinite(b.SpellTime)) &&
            s.BotPlans.Select(b => b.Id).Distinct().Count() == s.BotPlans.Length);
        return s;
    }
}
