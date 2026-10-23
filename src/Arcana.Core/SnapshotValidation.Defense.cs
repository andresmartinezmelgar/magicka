namespace Arcana.Core;

public static partial class SnapshotValidation
{
    private static void CheckDefense(WorldSnapshot state)
    {
        if (state.Mode != GameMode.Defense) { if (state.Defense != null) Invalid(); return; }
        var d = state.Defense;
        if (d == null || state.Map != ArenaMapId.Bastion || !double.IsFinite(d.Elapsed) ||
            d.Elapsed < 0 || d.Elapsed > state.Time + 0.1 || d.Difficulty < 1 ||
            !Size(d.NextBossIn, 180.01f) || d.BossesKilled < 0 || d.BossesKilled > d.TotalKills ||
            d.HumanCount != state.Players.Count(p => !p.Bot) || d.Paused && (d.HumanCount != 1 || state.MatchEnded) || d.TotalKills < 0 ||
            d.Minions is not { Length: <= DefenseRules.MaxMinions } || d.Drops is not { Length: <= DefenseRules.MaxDrops } ||
            d.Bestiary == null || d.Bestiary.Length > DefenseRules.MinionTypeCount ||
            d.MinionLimit is < 1 or > DefenseRules.MaxMinions || d.PerformanceLimit is < DefensePerformanceBudget.MinimumLimit or > DefenseRules.MaxMinions ||
            d.Defenders == null || d.Defenders.Length != state.Players.Length || state.Players.Length > DefenseRules.MaxDefenders ||
            state.Players.Any(p => p.Team != 0)) Invalid();
        var map = ArenaMap.Get(state.Map);
        var kinds = new HashSet<MinionKind>();
        long discoveredKills = 0;
        foreach (var enemy in d!.Bestiary!)
        {
            if (enemy == null || !Enum.IsDefined(enemy.Kind) || !kinds.Add(enemy.Kind) || enemy.Difficulty < 1 ||
                enemy.Difficulty > d.Difficulty || enemy.Spawned < 1 || enemy.Killed < 0 || enemy.Killed > enemy.Spawned ||
                !NonNegative([enemy.MaxHealth, enemy.Attack, enemy.AttackMaximum, enemy.AttackInterval, enemy.Speed, enemy.Range]) ||
                enemy.MaxHealth <= 0 || enemy.AttackMaximum < enemy.Attack || enemy.AttackInterval <= 0 || enemy.Speed <= 0 || enemy.Range <= 0) Invalid();
            discoveredKills += enemy!.Killed;
        }
        if (discoveredKills != d.TotalKills || d.Minions!.Any(m => m == null || !kinds.Contains(m.Kind))) Invalid();
        var ids = new HashSet<int>();
        foreach (var m in d!.Minions!)
            if (m == null || m.Id < 1000 || !ids.Add(m.Id) || !Enum.IsDefined(m.Kind) ||
                !Position(map, m.Position) || !Direction(m.Facing) || !NonNegative([m.Health, m.MaxHealth, m.Wet, m.Burning, m.Chill, m.Frozen, m.Shield, m.ShieldMax]) ||
                m.Shield > m.ShieldMax || m.FocusTargetId != 0 && !state.Players.Any(p => p.Id == m.FocusTargetId) ||
                (m.Kind == MinionKind.Prism ? m.VulnerableElement == null || !DefenseRules.ValidPrismElement(m.VulnerableElement.Value) : m.VulnerableElement != null) ||
                !Size(m.SpeedBoostSeconds, 4.01f) || !Size(m.AttackWarningSeconds, DefenseRules.AttackWindup(m.Kind) + 0.01f) ||
                m.Health <= 0 || m.Health > m.MaxHealth || !Elements(m.Elements)) Invalid();
        ids.Clear();
        foreach (var p in d.Defenders!)
        {
            if (p == null || !ids.Add(p.PlayerId) || !state.Players.Any(w => w.Id == p.PlayerId) || p.Level is < 1 or > 100 ||
                p.Experience < 0 || p.Experience >= DefenseRules.ExperienceForLevel(p.Level) || p.NextLevelExperience != DefenseRules.ExperienceForLevel(p.Level) ||
                p.Gold is < 0 or > 100000000 || p.Kills < 0 || p.Deaths is < 0 or > DefenseRules.PlayerLives || !double.IsFinite(p.ActiveSeconds) ||
                p.ActiveSeconds < 0 || p.ActiveSeconds > state.Time + 0.1 || p.Auras is not { Length: 3 } ||
                p.Auras.Any(a => a is < 0 or > DefenseRules.MaxAuraLevel) || p.Items is not { Length: <= DefenseRules.InventoryLimit } ||
                p.Passives is not { Length: 4 } || p.Passives.Any(a => a is < 0 or > DefenseRules.MaxPassiveLevel) ||
                p.PassiveCooldowns is not { Length: 4 } || p.PassiveCooldowns.Any(a => !Size(a, 16.01f)) ||
                !Size(p.BerserkerRemaining, DefenseRules.BerserkerDuration(DefenseRules.MaxPassiveLevel) + 0.01f) ||
                p.Modifiers == null || !GoldBonuses.Valid(p.Modifiers.ExtraGoldEvery, p.Modifiers.GoldBonusPercent) ||
                !NonNegative([p.DamageMultiplier, p.IncomingMultiplier, p.Regeneration, p.SpeedMultiplier]) ||
                !NonNegative([p.Modifiers.Power, p.Modifiers.Healing, p.Modifiers.Range, p.Modifiers.Incoming, p.Modifiers.Speed])) Invalid();
            var items = new HashSet<string>(); var slots = new HashSet<EquipmentSlot>();
            foreach (var item in p!.Items!)
                if (item == null || !EquipmentCatalogue.Valid(item.Item) || !items.Add(item.Item.Id) ||
                    item.Upgrades is < 0 or > 10 || item.Equipped && !slots.Add(item.Item.Slot)) Invalid();
        }
        ids.Clear();
        foreach (var drop in d.Drops!)
            if (drop == null || drop.Id <= 0 || !ids.Add(drop.Id) || !Position(map, drop.Position) || !Enum.IsDefined(drop.Kind) ||
                !Enum.IsDefined(drop.Buff) || drop.OwnerId != 0 && !state.Players.Any(p => p.Id == drop.OwnerId) ||
                drop.Gold is < 0 or > 208 || !Tier(drop.Tier) || drop.Slot.HasValue && !Enum.IsDefined(drop.Slot.Value) ||
                drop.Kind == DefenseDropKind.Equipment && (!drop.Slot.HasValue || !drop.Tier.HasValue)) Invalid();
    }
}
