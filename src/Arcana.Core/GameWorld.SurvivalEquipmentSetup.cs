namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool ApplySurvivalEquipmentSetup(Wizard player, EquipmentItem? incoming = null)
    {
        var progress = _defenders[player.Id];
        EquipmentItem[] candidates = incoming == null ? progress.Items.ToArray() : [.. progress.Items, incoming];
        var chosen = SurvivalEquipmentSelection.Select(candidates, progress.EquipmentSetup, progress.Loadout, progress.Upgrades);
        var equipped = Enum.GetValues<EquipmentSlot>().Select(chosen.Get).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        bool Retain(EquipmentItem item) => item.Tier == ItemTier.Mythic || equipped.Contains(item.Id) || progress.Favorites.Contains(item.Id);
        var retained = candidates.Where(Retain).ToArray();
        if (retained.Length > DefenseRules.InventoryLimit && incoming != null)
        {
            // El botín Mítico permanece en el suelo hasta que haya espacio; no modifica el equipo ni el oro.
            if (incoming.Tier == ItemTier.Mythic) return false;
            SellSetupDiscard(progress, player.Id, incoming);
            Emit("defense_item", player.Position, [], "Selección automática: pieza vendida; el inventario está ocupado por Míticos, favoritos y equipo.");
            return true;
        }
        int before = progress.Gold, sold = 0;
        foreach (var item in candidates)
            if (!Retain(item))
            { SellSetupDiscard(progress, player.Id, item); progress.Upgrades.Remove(item.Id); sold++; }
        progress.Items.Clear(); progress.Items.AddRange(retained); progress.Loadout = chosen;
        RecalculateDefender(player);
        if (sold > 0) Emit("defense_item", player.Position, [], $"Selección automática: {sold} pieza(s) vendidas · +{progress.Gold - before} oro temporal.");
        return true;
    }
    private void SellSetupDiscard(DefenseProgress progress, int playerId, EquipmentItem item)
    {
        int earned = Math.Min(100000000 - progress.Gold, item.SaleGold);
        progress.Gold += earned; StatisticsEconomy(playerId, StatisticMeasure.GoldEarned, earned);
        if (progress.AutoSold < int.MaxValue) progress.AutoSold++;
    }
}
