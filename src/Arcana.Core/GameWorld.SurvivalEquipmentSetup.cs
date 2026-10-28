namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void ApplySurvivalEquipmentSetup(Wizard player, EquipmentItem? incoming = null)
    {
        var progress = _defenders[player.Id];
        EquipmentItem[] candidates = incoming == null ? progress.Items.ToArray() : [.. progress.Items, incoming];
        var chosen = SurvivalEquipmentSelection.Select(candidates, progress.EquipmentSetup, progress.Loadout, progress.Upgrades);
        var equipped = Enum.GetValues<EquipmentSlot>().Select(chosen.Get).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        var retained = candidates.Where(i => equipped.Contains(i.Id) || progress.Favorites.Contains(i.Id)).ToArray();
        if (retained.Length > DefenseRules.InventoryLimit && incoming != null)
        {
            SellSetupDiscard(progress, player.Id, incoming);
            Emit("defense_item", player.Position, [], "Selección automática: pieza vendida; el inventario está ocupado por favoritos y equipo.");
            return;
        }
        int before = progress.Gold, sold = 0;
        foreach (var item in candidates)
            if (!equipped.Contains(item.Id) && !progress.Favorites.Contains(item.Id))
            { SellSetupDiscard(progress, player.Id, item); progress.Upgrades.Remove(item.Id); sold++; }
        progress.Items.Clear(); progress.Items.AddRange(retained); progress.Loadout = chosen;
        RecalculateDefender(player);
        if (sold > 0) Emit("defense_item", player.Position, [], $"Selección automática: {sold} pieza(s) vendidas · +{progress.Gold - before} oro temporal.");
    }
    private void SellSetupDiscard(DefenseProgress progress, int playerId, EquipmentItem item)
    {
        int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
        progress.Gold += earned; StatisticsEconomy(playerId, StatisticMeasure.GoldEarned, earned);
        if (progress.AutoSold < int.MaxValue) progress.AutoSold++;
    }
}
