namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void AddDefenseDrop(V2 at, int ownerId, DefenseDropKind kind)
    {
        if (_defenseDrops.Count >= DefenseRules.MaxDrops) _defenseDrops.RemoveAt(0);
        var offset = new V2((float)_random.NextDouble() * 1.2f - 0.6f, (float)_random.NextDouble() * 1.2f - 0.6f);
        var position = Map.Walkable(at + offset) ? at + offset : at;
        _defenseDrops.Add(new() { Id = _nextEntity++, OwnerId = ownerId, Position = position, Kind = kind,
            Gold = 8 + Math.Min(100, _defenseDifficulty) * 2,
            Item = kind == DefenseDropKind.Equipment ? EquipmentCatalogue.Generate(_random) : null,
            Buff = (BuffKind)_random.Next(RelicBuffs.Count) });
    }

    private bool GrantDefenseItem(Wizard player, EquipmentItem item)
    {
        var progress = _defenders[player.Id];
        // Evita repetir la búsqueda de equipo cada tick si las 24 piezas son intocables.
        if (item.Tier == ItemTier.Mythic && progress.Items.Count >= DefenseRules.InventoryLimit &&
            (!progress.EquipmentSetup.Enabled || progress.Items.All(i => i.Tier == ItemTier.Mythic ||
                progress.Favorites.Contains(i.Id) || progress.EquippedItemHistory.Contains(i.Id))))
            return false;
        if (progress.EquipmentSetup.Enabled)
        {
            var candidate = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                ? b with { Maximum = MathF.Round(b.Current + DefenseRules.ItemUpgradeLimit * DefenseRules.ItemUpgradeStep, 4) } : b).ToArray() };
            if (!ApplySurvivalEquipmentSetup(player, candidate)) return false;
            RecordDefenseItemCollection(player.Id, item);
            return true;
        }
        RecordDefenseItemCollection(player.Id, item);
        if (item.Tier != ItemTier.Mythic && !progress.Favorites.Contains(item.Id) && progress.Loadout.Get(item.Slot) != item.Id &&
            progress.AutoSellStyles.Any(s => s.MatchesLowerQuality(item)))
        {
            int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
            StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned;
            if (progress.AutoSold < int.MaxValue) progress.AutoSold++;
            Emit("defense_item", player.Position, [], player.Name + $": venta automática · +{earned} oro", actorId: player.Id);
            return true;
        }
        if (progress.Items.Count >= DefenseRules.InventoryLimit)
        {
            int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
            StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned;
            Emit("defense_item", player.Position, [], player.Name + ": inventario lleno · objeto " +
                EquipmentCatalogue.TierName(item.Tier) + $" convertido en +{earned} oro", actorId: player.Id);
            return true;
        }
        else
        {
            // Solo el equipo temporal: cada tirada dispone de diez incrementos
            // propios, incluso cuando ya salió en el máximo del catálogo.
            item = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                ? b with { Maximum = MathF.Round(b.Current + DefenseRules.ItemUpgradeLimit * DefenseRules.ItemUpgradeStep, 4) } : b).ToArray() };
            progress.Items.Add(item);
            if (progress.Loadout.Get(item.Slot).Length == 0 && !progress.ManualEquipmentSlots.Contains(item.Slot))
                progress.Loadout = progress.Loadout.With(item.Slot, item.Id);
            RecalculateDefender(player);
        }
        Emit("defense_item", player.Position, [], player.Name + ": objeto temporal " + EquipmentCatalogue.TierName(item.Tier), actorId: player.Id);
        return true;
    }

    private void RecordDefenseItemCollection(int playerId, EquipmentItem item)
    {
        _statistics.Add(StatisticGroup.Equipment, (int)item.Tier, StatisticMeasure.Collected, 1);
        StatisticsEconomy(playerId, StatisticMeasure.Collected, 1);
    }

    private void TickDefenseDrops(float dt)
    {
        foreach (var drop in _defenseDrops)
        {
            drop.Remaining -= dt;
            if (drop.Remaining <= 0) continue;
            Wizard? receiver = null; float nearest = 1.6f;
            foreach (var player in _players.Values)
            {
                if (!player.Alive || drop.OwnerId != 0 && player.Id != drop.OwnerId) continue;
                float distance = V2.Distance(player.Position, drop.Position);
                if (distance < nearest) { receiver = player; nearest = distance; }
            }
            if (receiver == null) continue;
            if (drop.Kind == DefenseDropKind.Gold)
                foreach (var pair in _defenders)
                {
                    var progress = pair.Value;
                    var gear = _players[pair.Key].Equipment;
                    int total = GoldBonuses.Apply(drop.Gold, gear.ExtraGoldEvery, gear.GoldBonusPercent,
                        ref progress.GoldCoinRemainder, ref progress.GoldPercentRemainder);
                    int earned = Math.Min(100000000 - progress.Gold, total);
                    progress.Gold += earned; StatisticsEconomy(pair.Key, StatisticMeasure.GoldEarned, earned);
                }
            else if (drop.Kind == DefenseDropKind.Equipment && drop.Item != null)
            { if (!GrantDefenseItem(receiver, drop.Item)) continue; }
            else
            {
                _statistics.Add(StatisticGroup.Relic, (int)drop.Buff, StatisticMeasure.Collected, 1);
                RelicBuffs.Add(receiver, drop.Buff);
                Array.Copy(receiver.BuffStacks, _defenders[receiver.Id].Buffs, RelicBuffs.Count);
                Emit("pickup", receiver.Position, [], receiver.Name + ": " + RelicBuffs.Name(drop.Buff));
            }
            drop.Remaining = 0;
        }
        _defenseDrops.RemoveAll(d => d.Remaining <= 0);
    }
}
