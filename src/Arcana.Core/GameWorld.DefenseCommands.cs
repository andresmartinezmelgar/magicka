namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool DefenseCommand(Wizard player, ClientMessage command)
    {
        if (!IsDefense || !_defenders.TryGetValue(player.Id, out var progress)) return false;
        if (command.Type == "defense_equipment_setup")
        { return ConfigureDefenseEquipment(player, progress, command); }
        if (command.Type == "defense_favorite")
        { return SetDefenseFavorite(player, progress, command); }
        if (command.Type == "defense_auto_sell")
        { return SetDefenseAutoSell(player, progress, command); }
        if (command.Type == "defense_performance")
        { return ReportDefensePerformance(player, progress, command); }
        if (command.Type == "defense_loadout")
        { return ApplyDefenseLoadout(player, progress, command); }
        if (command.Type == "defense_shop")
        { return SetDefenseShopOpen(player, progress, command); }
        if (!player.Alive || MatchEnded) return false;
        // Las acciones dirigidas a bots tienen sus propios mensajes para que
        // un servidor anterior no pueda aplicarlas a la cartera del solicitante.
        string action = command.Type switch
        {
            "defense_bot_aura" => "defense_aura",
            "defense_bot_passive" => "defense_passive",
            "defense_bot_sell" => "defense_sell",
            "defense_bot_upgrade" => "defense_upgrade",
            _ => command.Type
        };
        bool botAction = action != command.Type || action == "defense_bot_equip";
        if (botAction)
        {
            if (player.Bot || !_players.TryGetValue(command.DefenseBotId, out var bot) || !bot.Bot || !bot.Alive || !Allied(player, bot) ||
                bot.Deaths >= DefenseRules.PlayerLives || !_defenders.TryGetValue(bot.Id, out var botProgress)) return false;
            player = bot;
            progress = botProgress;
        }

        switch (action)
        {
            case "defense_aura":
                if (command.Slot < 0 || command.Slot >= DefenseRules.AuraCount) return false;
                int level = progress.Auras[command.Slot], cost = DefenseRules.AuraCost(level);
                if (level >= DefenseRules.MaxAuraLevel || progress.Gold < cost ||
                    botAction && command.DefenseUpgradeFromLevel != level) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, cost);
                progress.Gold -= cost; progress.Auras[command.Slot]++;
                Emit("defense_shop", player.Position, [], DefenseRules.AuraName((DefenseAura)command.Slot) + $" · nivel {level + 1}");
                return true;
            case "defense_passive":
                if (command.Slot is < 0 or > 3) return false;
                int passiveLevel = progress.Passives[command.Slot], passiveCost = DefenseRules.PassiveCost(passiveLevel);
                if (passiveLevel >= DefenseRules.MaxPassiveLevel || progress.Gold < passiveCost ||
                    botAction && command.DefenseUpgradeFromLevel != passiveLevel) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, passiveCost);
                progress.Gold -= passiveCost; progress.Passives[command.Slot]++;
                if (passiveLevel == 0) progress.PassiveCooldowns[command.Slot] = command.Slot switch { 0 => 3, 1 => 16, 2 => 5, _ => 4 };
                Emit("defense_shop", player.Position, [], DefenseRules.PassiveName((DefensePassive)command.Slot) + $" · nivel {passiveLevel + 1}");
                return true;
            case "defense_supply":
                if (progress.Gold < DefenseRules.SupplyCost || progress.Items.Count >= DefenseRules.InventoryLimit) return false;
                StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, DefenseRules.SupplyCost);
                progress.Gold -= DefenseRules.SupplyCost;
                GrantDefenseItem(player, EquipmentCatalogue.Generate(_random));
                return true;
            case "defense_equip":
                EquipmentSlot selectedSlot;
                if (command.ItemId == "")
                {
                    selectedSlot = (EquipmentSlot)command.Slot;
                    if (!Enum.IsDefined(selectedSlot)) return false;
                }
                else
                {
                    var selected = progress.Items.FirstOrDefault(i => i.Id == command.ItemId);
                    if (selected == null) return false;
                    selectedSlot = selected.Slot;
                }
                SetManualDefenseLoadout(player, progress, progress.Loadout.With(selectedSlot, command.ItemId), selectedSlot);
                return true;
            case "defense_bot_equip":
                if (command.ItemId == null) return false;
                var botSlot = (EquipmentSlot)command.Slot;
                if (!Enum.IsDefined(botSlot) || command.ItemId.Length > 0 &&
                    !progress.Items.Any(i => i.Id == command.ItemId && i.Slot == botSlot)) return false;
                SetManualDefenseLoadout(player, progress, progress.Loadout.With(botSlot, command.ItemId), botSlot);
                return true;
            case "defense_upgrade":
            case "defense_sell":
                if (command.ItemId == null || command.ItemId.Length != 32) return false;
                var item = progress.Items.FirstOrDefault(i => i.Id == command.ItemId);
                if (item == null) return false;
                if (action == "defense_sell")
                {
                    if (progress.Loadout.Get(item.Slot) == item.Id || progress.Favorites.Contains(item.Id)) return false;
                    progress.Items.Remove(item); progress.Upgrades.Remove(item.Id);
                    progress.EquippedItemHistory.Remove(item.Id);
                    { int earned = Math.Min(100000000 - progress.Gold, item.SaleGold); StatisticsEconomy(player.Id, StatisticMeasure.GoldEarned, earned); progress.Gold += earned; }
                }
                else
                {
                    int upgrades = progress.Upgrades.GetValueOrDefault(item.Id);
                    if (!ItemAffixes.CanUpgrade(item, upgrades) || botAction && command.DefenseUpgradeFromLevel != upgrades) return false;
                    int price = DefenseRules.SurvivalUpgradeCost(item.Tier, upgrades);
                    if (progress.Gold < price) return false;
                    var improved = item with { Bonuses = item.Bonuses.Select(b => EquipmentCatalogue.BonusActive(item.Slot, b.Stat)
                        ? b with { Current = MathF.Round(Math.Min(b.Maximum, b.Current + DefenseRules.ItemUpgradeStep), 4) } : b).ToArray() };
                    StatisticsEconomy(player.Id, StatisticMeasure.GoldSpent, price);
                    progress.Gold -= price; progress.Upgrades[item.Id] = upgrades + 1;
                    progress.Items[progress.Items.IndexOf(item)] = improved;
                }
                if (progress.EquipmentSetup.Enabled) ApplySurvivalEquipmentSetup(player); else RecalculateDefender(player);
                return true;
        }
        return false;
    }
}
