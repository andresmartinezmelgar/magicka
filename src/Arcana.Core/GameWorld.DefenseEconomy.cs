namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void RecalculateDefender(Wizard player)
    {
        var progress = _defenders[player.Id];
        // Solo ante cambios de inventario, equipo, nivel o carga de una partida; nunca por tick.
        if (player.Bot)
        {
            progress.Loadout = DefenseBotEquipment.Select(progress.Items, player.WizardType, progress.Loadout,
                progress.Upgrades, progress.ManualEquipmentSlots);
            SellUnusedDefenseBotEquipment(player, progress);
        }
        var profile = new AccountProfile("", player.Name, 0, 0, 0, progress.Loadout, progress.Items.ToArray());
        var prepared = PublicEquipment.Prepare(profile, progress.Upgrades);
        float previousCapacity = player.Equipment.ShieldCapacity;
        player.Equipment = prepared.Modifiers with { Power = prepared.Modifiers.Power * (1 + (progress.Level - 1) * 0.08f),
            Healing = prepared.Modifiers.Healing * (1 + (progress.Level - 1) * 0.04f),
            ShieldCapacity = prepared.Modifiers.ShieldCapacity * DefenseRules.ShieldLevelMultiplier(progress.Level) };
        CheckAutomaticRockEquipment(player);
        // Preserve damage already taken while increasing the capacity of an active ward.
        // Recalculating equipment multiple times must never stack the level bonus.
        if (player.ShieldMax > 0 && previousCapacity > 0)
        {
            float ratio = player.Equipment.ShieldCapacity / previousCapacity;
            player.ShieldMax *= ratio;
            player.Shield = Math.Min(player.ShieldMax, player.Shield * ratio);
        }
        player.PublicEquipment = prepared.View;
        float health = Wizard.BaseMaxHealth + (progress.Level - 1) * 50 + player.BuffStacks[(int)BuffKind.Vitality] * 100;
        // Subir el máximo o recalcular equipo no es una fuente de curación.
        if (player.Alive) player.Health = Math.Min(player.Health, health);
        player.MaxHealth = health;
    }

    private void SellUnusedDefenseBotEquipment(Wizard player, DefenseProgress progress)
    {
        for (int i = progress.Items.Count - 1; i >= 0; i--)
        {
            var item = progress.Items[i];
            if (progress.Loadout.Get(item.Slot) == item.Id || item.Tier == ItemTier.Mythic ||
                progress.Favorites.Contains(item.Id) || progress.EquippedItemHistory.Contains(item.Id)) continue;
            SellSetupDiscard(progress, player.Id, item);
            progress.Upgrades.Remove(item.Id);
            progress.Items.RemoveAt(i);
        }
    }

}
