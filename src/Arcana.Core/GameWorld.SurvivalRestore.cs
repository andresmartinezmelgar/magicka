using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public sealed partial class GameWorld
{
    public static GameWorld RestoreSurvival(SurvivalSave save)
    {
        var s = ReadSurvivalSave(save);
        // Los aliados se restauran explícitamente, sin crear bots adicionales en el constructor.
        var world = new GameWorld(new()
        {
            Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = s.Options.MaxPlayers,
            SurvivalMap = s.SurvivalMap ?? s.Options.SurvivalMap ?? SurvivalMapSpec.Legacy,
            FriendlyFire = s.Options.FriendlyFire, DefenseFlow = s.Options.DefenseFlow, Seed = s.Options.Seed,
            ScoreLimit = s.Options.ScoreLimit, Tickets = s.Options.Tickets
        });
        world.Tick = s.Tick; world._nextEvent = s.NextEvent; world._nextMinion = s.NextMinion; world._nextEntity = s.NextEntity;
        world._defenseElapsed = s.Elapsed; world._defenseDifficulty = DefenseRules.TimedDifficulty(s.Elapsed);
        world._defensePartySize = s.PartySize; world._defenseKills = s.Kills; world._bossesKilled = s.Bosses;
        world._spawnClock = s.SpawnClock; world._auraClock = s.AuraClock; world._bossClock = s.BossClock;
        world._oracleSpawnCooldown = s.OracleSpawnCooldown;
        world._colossusMomentumRemaining = s.ColossusMomentumRemaining;
        world._prismSpawnCooldown = s.PrismSpawnCooldown;
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
                LastActivity = d.LastActivity, Loadout = d.Loadout, CombatState = p,
                EquipmentSetup = d.EquipmentSetup.Copy(), EquipmentSetupInitialized = d.EquipmentSetupInitialized
            };
            d.Passives.CopyTo(progress.Passives, 0); d.PassiveCooldowns.CopyTo(progress.PassiveCooldowns, 0);
            d.Auras.CopyTo(progress.Auras, 0); p.BuffStacks.CopyTo(progress.Buffs, 0);
            progress.Items.AddRange(d.Items);
            progress.Favorites.UnionWith(d.Favorites);
            progress.EquippedItemHistory.UnionWith(d.EquippedItemHistory);
            // Los guardados sin historial conservan el equipo cargado. Un historial vacío ya es válido.
            if (!d.HasEquippedItemHistory)
            {
                foreach (var item in progress.Items)
                    if (progress.Loadout.Get(item.Slot) == item.Id) progress.EquippedItemHistory.Add(item.Id);
            }
            progress.ManualEquipmentSlots.UnionWith(d.ManualEquipmentSlots);
            if (d.AutoSellRuleVersion is 1 or InventoryPreferences.CurrentAutoSellRuleVersion)
                progress.AutoSellStyles.UnionWith(EquipmentStyle.SlotLimits(d.AutoSellStyles));
            progress.AutoSold = d.AutoSold;
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
            var minion = new DefenseMinion(p, m.Kind, m.Strength)
            {
                PartySize = m.PartySize == 0 ? s.PartySize : m.PartySize,
                Attack = m.Attack, ChannelTime = m.ChannelTime, SpeedBoost = m.SpeedBoost, SpeedClock = m.SpeedClock,
                NavigationClock = m.NavigationClock, Navigation = m.Navigation, Windup = m.Windup,
                SupportCooldown = m.SupportCooldown, ShieldCooldown = m.ShieldCooldown, SpellTurn = m.SpellTurn,
                FocusTargetId = m.FocusTargetId, FocusTime = m.FocusTime, AuraRepairCooldown = m.AuraRepairCooldown
            };
            if (m.PartySize == 0)
            {
                // Guardados anteriores: su daño solo incluía la dificultad temporal
                // y la vida ordinaria crecía un 10 % por plaza. Migrar conservando el porcentaje herido.
                float oldHealthMultiplier = 1 + (s.PartySize - 1) * (m.Kind == MinionKind.Boss ? .4f : .1f);
                ScaleDefenseEnemy(minion, DefenseRules.PartyHealthMultiplier(m.Kind, s.PartySize) / oldHealthMultiplier,
                    DefenseRules.PartyDamageMultiplier(s.PartySize));
            }
            world._minions.Add(p.Id, minion);
            world._combatants.Add(p.Id, p); world._orderedPlayers.Add(p);
        }
        foreach (var source in s.EffectSources) world.RememberEffectSource(source.Id, source.Kind, source.Power);
        foreach (var p in s.Projectiles) world._projectiles.Add(new() { Id = p.Id, Owner = p.Owner, Position = p.Position,
            Velocity = p.Velocity, Recipe = new(p.Elements), Radius = p.Radius, Power = p.Power, Lifetime = p.Lifetime,
            RockFragmentation = p.RockFragmentation, IsRockFragment = p.IsRockFragment });
        foreach (var w in s.Walls) world._walls.Add(new() { Id = w.Id, Owner = w.Owner, A = w.A, B = w.B,
            Health = w.Health, Lifetime = w.Lifetime, Recipe = new(w.Elements) });
        foreach (var f in s.Fields) world._fields.Add(new() { Id = f.Id, Owner = f.Owner, Position = f.Position,
            Radius = f.Radius, Lifetime = f.Lifetime, Delay = f.Delay, TriggerRadius = f.TriggerRadius,
            Power = f.Power, Recipe = new(f.Elements), Kind = f.Kind, IsRockFragmentArea = f.IsRockFragmentArea });
        foreach (var d in s.Drops) world._defenseDrops.Add(new() { Id = d.Id, OwnerId = d.OwnerId, Gold = d.Gold,
            Position = d.Position, Kind = d.Kind, Item = d.Item, Buff = d.Buff, Remaining = d.Remaining });
        foreach (var b in s.Bestiary) world._defenseBestiary.Add(b.Kind, b);
        foreach (var b in s.BotPlans) world._defenseBotPlans.Add(b.Id, new()
            { TargetId = b.TargetId, HealTargetId = b.HealTargetId, Goal = b.Goal, SpellTime = b.SpellTime, SpellTurn = b.SpellTurn });
        world._defenseBotPlanClock = s.BotClock;
        foreach (var burst in s.ArcaneBursts) world._arcaneBursts.Enqueue(burst);
        ((SurvivalRandom)world._random).State = s.RandomState;
        world.RestoreStatistics(null);
        world.UpdateDefensePartyScaling();
        foreach (var legacy in s.Minions.Where(m => m.PartySize == 0))
            if (world._minions.TryGetValue(legacy.Body.Id, out var enemy)) world.UpdateDefenseEnemyDetails(enemy, spawned: false);
        foreach (var source in world._lingeringSources.Values)
        { world._statistics.Bind(source.Body, source.Kind, spawn: false); world._statistics.Retire(source.Body.Id); }
        world.BuildDefenseCells();
        return world;
    }
}
