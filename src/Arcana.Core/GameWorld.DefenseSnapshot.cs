namespace Arcana.Core;

public sealed partial class GameWorld
{
    private DefenseSnapshot? DefenseState()
    {
        if (!IsDefense) return null;
        return new(_defenseElapsed, _defenseDifficulty, _defenseKills,
            _minions.Values.Where(m => m.Body.Alive).Select(m => new MinionSnapshot(m.Body.Id, m.Kind, m.Body.Position, m.Body.Facing,
                m.Body.Health, m.Body.MaxHealth, m.Body.Wet, m.Body.Burning, m.Body.Chill, m.Body.Frozen,
                m.Body.Channel != null, Copy(m.Body.Channel?.Elements ?? []), Empowered(m))
                { SpeedBoostSeconds = m.SpeedBoost, AttackWarningSeconds = m.Windup,
                    Shield = m.Body.Shield, ShieldMax = m.Body.ShieldMax, VulnerableElement = m.Body.VulnerableElement,
                    ArcaneMarked = m.Body.ArcaneMarkRank > 0,
                    Stunned = m.Body.StunTime > 0, WaterSlowed = WaterSlowMultiplier(m.Body) < 1,
                    FocusTargetId = _players.TryGetValue(m.FocusTargetId, out var focused) && focused.Alive ? focused.Id : 0 }).ToArray(),
            _defenseDrops.Select(d => new DefenseDropSnapshot(d.Id, d.OwnerId, d.Position, d.Kind, d.Gold, d.Buff, d.Item?.Slot, d.Item?.Tier)).ToArray(),
            _players.Values.Select(p =>
            {
                var s = _defenders[p.Id];
                return new DefensePlayerSnapshot(p.Id, s.Level, s.Experience, DefenseRules.ExperienceForLevel(s.Level), s.Gold,
                    s.ActiveTicks / 60.0, p.Kills, p.Deaths, Copy(s.Auras), s.Items.Select(i => new DefenseItemSnapshot(i with { Bonuses = Copy(i.Bonuses) },
                        s.Upgrades.GetValueOrDefault(i.Id), s.Loadout.Get(i.Slot) == i.Id)).ToArray(), p.Equipment,
                    DamagePower(p.Id), RelicBuffs.Incoming(p) * p.Equipment.Incoming,
                    ActiveShieldRegeneration(p) + RelicBuffs.Regeneration(p) + s.Auras[2] * 10 * HealingPower(p.Id),
                    RelicBuffs.Speed(p) * p.Equipment.Speed * DefenseSpeed(p), Copy(s.Passives), Copy(s.PassiveCooldowns), s.BerserkerRemaining)
                    { Favorites = s.Favorites.ToArray(), AutoSellStyles = s.AutoSellStyles.ToArray(), AutoSold = s.AutoSold,
                        EquipmentSetup = s.EquipmentSetup.Copy(), EquipmentSetupInitialized = s.EquipmentSetupInitialized,
                        ManualEquipmentSlots = p.Bot ? [] : s.ManualEquipmentSlots.Order().ToArray(), BotInventoryAvailable = p.Bot };
            }).ToArray(), DefensePaused, _players.Values.Count(p => !p.Bot))
            { Bestiary = _defenseBestiary.Values.OrderBy(e => e.Kind).ToArray(), NextBossIn = _bossClock, BossesKilled = _bossesKilled,
                ColossusMomentumSeconds = (int)Math.Ceiling(_colossusMomentumRemaining),
                MinionLimit = DefenseMinionLimit, PerformanceLimit = DefensePerformanceLimit };
    }
}
