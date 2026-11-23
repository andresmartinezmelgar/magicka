using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public sealed partial class GameWorld
{
    /// <summary>Solo una supervivencia local, con un humano y sus aliados IA, puede continuar en otro arranque.</summary>
    public SurvivalSave SaveSurvival(int localPlayerId, string runId, string bestiaryUid = "", bool validate = true)
    {
        if (!IsDefense || MatchEnded || _players.Values.Count(p => !p.Bot) != 1 ||
            !_players.TryGetValue(localPlayerId, out var local) || local.Bot || local.Deaths >= DefenseRules.PlayerLives)
            throw new InvalidOperationException("Solo se puede guardar una supervivencia local en curso.");
        var state = new SavedSurvival
        {
            Options = Options, SurvivalMap = Map.SurvivalMap, Tick = Tick, NextEvent = _nextEvent, NextMinion = _nextMinion, NextEntity = _nextEntity,
            ArcaneBursts = _arcaneBursts.ToArray(),
            RandomState = ((SurvivalRandom)_random).State, Kills = _defenseKills, Bosses = _bossesKilled,
            Elapsed = _defenseElapsed, PartySize = _defensePartySize, SpawnClock = _spawnClock, AuraClock = _auraClock,
            OracleSpawnCooldown = _oracleSpawnCooldown, PrismSpawnCooldown = _prismSpawnCooldown,
            ColossusMomentumRemaining = _colossusMomentumRemaining,
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
                    Favorites = s.Favorites.ToArray(), AutoSellStyles = s.AutoSellStyles.ToArray(), AutoSold = s.AutoSold,
                    EquippedItemHistory = s.EquippedItemHistory.Order(StringComparer.Ordinal).ToArray(),
                    EquipmentSetup = s.EquipmentSetup.Copy(), EquipmentSetupInitialized = s.EquipmentSetupInitialized,
                    ManualEquipmentSlots = s.ManualEquipmentSlots.Order().ToArray(),
                    AutoSellRuleVersion = InventoryPreferences.CurrentAutoSellRuleVersion
                });
            }).ToArray(),
            Minions = _minions.Values.Where(m => m.Body.Alive).Select(m => new SavedMinion(SurvivalWizard.Capture(m.Body),
                m.Kind, m.Strength, m.Attack, m.ChannelTime, m.SpeedBoost, m.SpeedClock, m.NavigationClock, m.Navigation,
                m.Windup, m.SupportCooldown, m.ShieldCooldown, m.SpellTurn)
                { PartySize = m.PartySize, FocusTargetId = _players.ContainsKey(m.FocusTargetId) ? m.FocusTargetId : 0,
                    FocusTime = m.FocusTime, AuraRepairCooldown = m.AuraRepairCooldown }).ToArray(),
            EffectSources = _lingeringSources.Select(s => new SavedEffectSource(s.Key, s.Value.Kind, s.Value.Body.Equipment.Power)).ToArray(),
            Projectiles = _projectiles.Select(p => new SavedProjectile(p.Id, p.Owner, p.Position, p.Velocity,
                p.Recipe.Elements, p.Radius, p.Power, p.Lifetime)
                { RockFragmentation = p.RockFragmentation, IsRockFragment = p.IsRockFragment }).ToArray(),
            Walls = _walls.Select(w => new SavedWall(w.Id, w.Owner, w.A, w.B, w.Health, w.Lifetime, w.Recipe.Elements)).ToArray(),
            Fields = _fields.Select(f => new SavedField(f.Id, f.Owner, f.Position, f.Radius, f.Lifetime, f.Delay,
                f.TriggerRadius, f.Power, f.Recipe.Elements, f.Kind) { IsRockFragmentArea = f.IsRockFragmentArea }).ToArray(),
            Drops = _defenseDrops.Select(d => new SavedDrop(d.Id, d.OwnerId, d.Gold, d.Position, d.Kind, d.Item, d.Buff, d.Remaining)).ToArray(),
            Bestiary = _defenseBestiary.Values.ToArray(),
            BotPlans = _defenseBotPlans.Select(p => new SavedBotPlan(p.Key, p.Value.TargetId, p.Value.HealTargetId,
                p.Value.Goal, p.Value.SpellTime, p.Value.SpellTurn)).ToArray()
        };
        // JsonElement realiza una copia profunda: seguir jugando no modifica el punto guardado.
        var save = new SurvivalSave(SurvivalSave.CurrentVersion, DateTimeOffset.UtcNow, runId, bestiaryUid,
            localPlayerId, JsonSerializer.SerializeToElement(state, SurvivalSave.Json));
        if (validate) ValidateSurvivalSave(save);
        return save;
    }

}
