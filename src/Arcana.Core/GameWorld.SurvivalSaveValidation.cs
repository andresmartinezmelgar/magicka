using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcana.Core;

public sealed partial class GameWorld
{
    internal static void ValidateSurvivalSave(SurvivalSave save) => ReadSurvivalSave(save);

    private static SavedSurvival ReadSurvivalSave(SurvivalSave save)
    {
        static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid)
        { if (!valid) throw new InvalidDataException("Partida de supervivencia incompleta o incompatible."); }
        Require(save != null && (save.Version is 1 or SurvivalSave.CurrentVersion) && Guid.TryParseExact(save.RunId, "N", out _) &&
            save.BestiaryUid is { Length: <= 128 } && save.State.ValueKind == JsonValueKind.Object);
        var s = save!.State.Deserialize<SavedSurvival>(SurvivalSave.Json);
        Require(s != null && s.Options != null && s.Options.Mode == GameMode.Defense && s.Options.Map == ArenaMapId.Bastion &&
            s.Options.MaxPlayers is >= 1 and <= DefenseRules.MaxDefenders && Enum.IsDefined(s.Options.DefenseFlow) &&
            s.Defenders is { Length: >= 1 and <= DefenseRules.MaxDefenders } && s.Defenders.Length <= s.Options.MaxPlayers &&
            s.Minions is { Length: <= DefenseRules.MaxMinions } && s.EffectSources is { Length: <= 512 } && s.Drops is { Length: <= DefenseRules.MaxDrops } &&
            s.Projectiles is { Length: <= 2048 } && s.Walls is { Length: <= 2048 } && s.Fields is { Length: <= 2048 } &&
            s.Bestiary != null && s.Bestiary.Length <= DefenseRules.MinionTypeCount && s.BotPlans is { Length: <= 3 });
        Require((s!.SurvivalMap == null || s.SurvivalMap.Valid) &&
            (s.Options.SurvivalMap == null || s.Options.SurvivalMap.Valid));
        Require(save.Version < 2 || s.SurvivalMap != null);
        Require(save.Version >= 2 || (s.SurvivalMap?.Version ?? 0) == 0 && (s.Options.SurvivalMap?.Version ?? 0) == 0);
        Require(s.Tick is >= 0 and <= 1892160000 && double.IsFinite(s.Elapsed) && s.Elapsed is >= 0 and <= 31536000 &&
            s.Kills >= 0 && s.Bosses >= 0 && s.PartySize is >= 1 and <= 4 && s.RandomState != 0 && s.NextEvent > 0 &&
            new[] { s.SpawnClock, s.AuraClock, s.BossClock, s.SurgeTime, s.SurgeCooldown, s.SurgeClock, s.BotClock }.All(float.IsFinite));
        Require(float.IsFinite(s.OracleSpawnCooldown) && s.OracleSpawnCooldown >= 0 &&
            s.OracleSpawnCooldown <= DefenseRules.OracleMinimumSpawnGap + DefenseRules.OracleSpawnGapVariation);
        Require(float.IsFinite(s.ColossusMomentumRemaining) && s.ColossusMomentumRemaining is >= 0 and <= DefenseRules.ColossusMomentumDuration);
        Require(float.IsFinite(s.PrismSpawnCooldown) && s.PrismSpawnCooldown >= 0 &&
            s.PrismSpawnCooldown <= DefenseRules.PrismMinimumSpawnGap + DefenseRules.PrismSpawnGapVariation);
        s.Statistics?.Validate();
        Require(s.Statistics == null || s.Statistics.Mode == GameMode.Defense && s.Statistics.Map == s.Options.Map && !s.Statistics.Complete);
        var ids = new HashSet<int>();
        foreach (var entry in s.Defenders)
        {
            Require(entry?.Body != null && entry.Body.Valid() && entry.Body.VulnerableElement == null && ids.Add(entry.Body.Id) && entry.Progress != null);
            var d = entry!.Progress;
            Require(d.Level is >= 1 and <= 100 && d.Experience >= 0 && d.Gold is >= 0 and <= 100000000 &&
                d.EquipmentSetup is { Valid: true } && (!d.EquipmentSetup.Enabled || d.EquipmentSetupInitialized) &&
                d.ManualEquipmentSlots is { Length: <= 6 } && d.ManualEquipmentSlots.All(s => Enum.IsDefined(s)) &&
                d.ManualEquipmentSlots.Distinct().Count() == d.ManualEquipmentSlots.Length &&
                (entry.Body.Bot || d.EquipmentSetup.Enabled || d.ManualEquipmentSlots.Length == 0) &&
                (d.GoldCoinRemainderScale == 0 ? d.GoldCoinRemainder is >= 0 and <= 1 :
                    d.GoldCoinRemainderScale == GoldBonuses.CoinFractionScale && d.GoldCoinRemainder is >= 0 and < GoldBonuses.CoinFractionScale) &&
                d.GoldPercentRemainder is >= 0 and <= 99 &&
                d.ActiveTicks >= 0 && d.ActiveTicks <= s.Tick && double.IsFinite(d.LastActivity) && float.IsFinite(d.BerserkerRemaining) &&
                d.Passives is { Length: 4 } && d.Passives.All(n => n is >= 0 and <= DefenseRules.MaxPassiveLevel) &&
                d.PassiveCooldowns is { Length: 4 } && d.PassiveCooldowns.All(float.IsFinite) &&
                d.Auras != null && (d.Auras.Length == 3 || d.Auras.Length == DefenseRules.AuraCount) &&
                d.Auras.All(n => n is >= 0 and <= DefenseRules.MaxAuraLevel) &&
                d.Items is { Length: <= DefenseRules.InventoryLimit } && d.Items.All(EquipmentCatalogue.Valid) &&
                d.Favorites is { Length: <= DefenseRules.InventoryLimit } && d.Favorites.Distinct().Count() == d.Favorites.Length &&
                d.Favorites.All(id => d.Items.Any(i => i.Id == id)) && d.AutoSold >= 0 &&
                d.EquippedItemHistory is { Length: <= DefenseRules.InventoryLimit } &&
                d.EquippedItemHistory.Distinct().Count() == d.EquippedItemHistory.Length &&
                d.EquippedItemHistory.All(id => d.Items.Any(i => i.Id == id)) &&
                d.AutoSellStyles != null && d.AutoSellStyles.Length <= EquipmentStyle.Limit && d.AutoSellStyles.All(s => s.Valid) &&
                d.AutoSellStyles.Distinct().Count() == d.AutoSellStyles.Length &&
                d.AutoSellRuleVersion is >= 0 and <= InventoryPreferences.CurrentAutoSellRuleVersion &&
                (d.AutoSellRuleVersion == 0 || d.AutoSellStyles.All(s => s.Tier > ItemTier.Silver) &&
                    d.AutoSellStyles.DistinctBy(s => (s.Slot, s.Family)).Count() == d.AutoSellStyles.Length) &&
                (d.AutoSellRuleVersion < 2 || d.AutoSellStyles.DistinctBy(s => s.Slot).Count() == d.AutoSellStyles.Length) &&
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
                m.Navigation.IsFinite && m.Strength > 0 && m.PartySize is >= 0 and <= DefenseRules.MaxDefenders &&
                (m.Kind == MinionKind.Prism ? m.Body.VulnerableElement.HasValue : !m.Body.VulnerableElement.HasValue) &&
                (m.FocusTargetId == 0 || s.Defenders.Any(d => d.Body.Id == m.FocusTargetId)) &&
                float.IsFinite(m.FocusTime) && m.FocusTime is >= 0 and <= DefenseRules.PrismFocusSeconds &&
                float.IsFinite(m.AuraRepairCooldown) && m.AuraRepairCooldown is >= 0 and <= DefenseRules.OracleShieldInterval &&
                new[] { m.Strength, m.Attack, m.ChannelTime, m.SpeedBoost,
                    m.SpeedClock, m.NavigationClock, m.Windup, m.SupportCooldown, m.ShieldCooldown }.All(float.IsFinite));
        foreach (var source in s.EffectSources)
            Require(source != null && source.Id >= 1000 && ids.Add(source.Id) && Enum.IsDefined(source.Kind) &&
                float.IsFinite(source.Power) && source.Power is > 0 and <= 1350.01f);
        Require(s.NextMinion > ids.Max() && s.NextMinion < int.MaxValue - 1);
        foreach (var body in s.Defenders.Select(d => d.Body).Concat(s.Minions.Select(m => m.Body)))
            Require(RecentDamageWindow.Valid(body.RecentDamage, s.Tick) &&
                (body.ArcaneMarkOwner == 0 || s.Defenders.Any(d => d.Body.Id == body.ArcaneMarkOwner)));
        Require(s.ArcaneBursts is { Length: <= CombatAffixes.ArcaneBurstLimit });
        foreach (var burst in s.ArcaneBursts)
            Require(burst != null && s.Defenders.Any(d => d.Body.Id == burst.Owner) && burst.Position.IsFinite &&
                Math.Abs(burst.Position.X) <= 1000 && Math.Abs(burst.Position.Y) <= 1000 && burst.Rank is >= 1 and <= 2 &&
                double.IsFinite(burst.Damage) && burst.Damage is > 0 and <= 1e18);
        var entities = new HashSet<int>();
        bool Entity(int id) => id > 0 && entities.Add(id);
        bool Elements(Element[] e) => SurvivalWizard.ValidElements(e);
        foreach (var p in s.Projectiles) Require(p != null && Entity(p.Id) && p.Position.IsFinite && p.Velocity.IsFinite &&
            Elements(p.Elements) && float.IsFinite(p.Radius) && p.Radius > 0 && float.IsFinite(p.Power) && float.IsFinite(p.Lifetime) &&
            p.RockFragmentation is >= 0 and <= 2 &&
            (p.RockFragmentation == 0 || !p.IsRockFragment && new SpellRecipe(p.Elements).Shape == SpellShape.Rock) &&
            (!p.IsRockFragment || new SpellRecipe(p.Elements).Shape == SpellShape.Rock && p.RockFragmentation == 0 &&
                p.Radius == CombatAffixes.RockFragmentRadius && p.Lifetime is > 0 and <= CombatAffixes.RockFragmentLifetime &&
                p.Velocity.Length <= CombatAffixes.RockFragmentSpeed + .001f));
        foreach (var w in s.Walls) Require(w != null && Entity(w.Id) && w.A.IsFinite && w.B.IsFinite && Elements(w.Elements) &&
            float.IsFinite(w.Health) && float.IsFinite(w.Lifetime));
        foreach (var f in s.Fields) Require(f != null && Entity(f.Id) && f.Position.IsFinite && Elements(f.Elements) && f.Kind is { Length: > 0 and < 48 } &&
            new[] { f.Radius, f.Lifetime, f.Delay, f.TriggerRadius, f.Power }.All(float.IsFinite) &&
            (!f.IsRockFragmentArea || (f.Kind is "burning_ground" or "frost_ground" or "water_pool") &&
                f.Radius == CombatAffixes.RockFragmentGroundRadius && f.Lifetime is > 0 and <= CombatAffixes.RockFragmentGroundSeconds &&
                !f.Elements.Contains(Element.Earth)));
        foreach (var d in s.Drops) Require(d != null && Entity(d.Id) && d.Position.IsFinite && Enum.IsDefined(d.Kind) && Enum.IsDefined(d.Buff) &&
            d.Gold is >= 0 and <= 208 && float.IsFinite(d.Remaining) &&
            (d.Kind != DefenseDropKind.Equipment || d.Item != null && EquipmentCatalogue.Valid(d.Item)));
        Require(s.NextEntity > entities.DefaultIfEmpty(0).Max() && s.NextEntity < int.MaxValue - 1);
        Require(s.Bestiary.All(b => b != null && Enum.IsDefined(b.Kind)) && s.Bestiary.Select(b => b.Kind).Distinct().Count() == s.Bestiary.Length &&
            s.Minions.All(m => s.Bestiary.Any(b => b.Kind == m.Kind)) && s.BotPlans.All(b => b != null &&
                s.Defenders.Any(d => d.Body.Bot && d.Body.Id == b.Id) && b.Goal.IsFinite && float.IsFinite(b.SpellTime)) &&
            s.BotPlans.Select(b => b.Id).Distinct().Count() == s.BotPlans.Length);
        return s;
    }
}
