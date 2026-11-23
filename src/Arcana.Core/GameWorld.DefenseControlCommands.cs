namespace Arcana.Core;

public sealed partial class GameWorld
{
    private bool ConfigureDefenseEquipment(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (player.Bot || MatchEnded || command.SurvivalEquipmentSetup is not { Valid: true } setup) return false;
        progress.EquipmentSetup = setup.Copy(); progress.EquipmentSetupInitialized = true;
        progress.ManualEquipmentSlots.Clear();
        if (setup.Enabled) ApplySurvivalEquipmentSetup(player);
        return true;
    }

    private bool SetDefenseFavorite(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (command.Slot is < 0 or > 1 || !progress.Items.Any(i => i.Id == command.ItemId)) return false;
        if (command.Slot == 1) progress.Favorites.Add(command.ItemId); else progress.Favorites.Remove(command.ItemId);
        if (progress.EquipmentSetup.Enabled) ApplySurvivalEquipmentSetup(player);
        else if (player.Bot) RecalculateDefender(player);
        return true;
    }

    private bool SetDefenseAutoSell(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (progress.EquipmentSetup.Enabled || command.Slot is < 0 or > 1 || command.AutoSellStyle is not { Valid: true } style) return false;
        if (command.Slot == 1)
        {
            if (style.Tier == ItemTier.Silver || !progress.Items.Any(i => i.Id == command.ItemId && EquipmentStyle.Of(i) == style)) return false;
            progress.AutoSellStyles.RemoveWhere(s => s.SameSlot(style));
            progress.AutoSellStyles.Add(style);
        }
        else progress.AutoSellStyles.RemoveWhere(s => s.SameSlot(style));
        return true;
    }

    private bool ReportDefensePerformance(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (player.Bot || !float.IsFinite(command.FramesPerSecond) || command.FramesPerSecond is < 1 or > 240 ||
            Time - progress.LastPerformanceReport < 1.5) return false;
        progress.LastPerformanceReport = Time;
        ObserveStatisticsPerformance(player.Id, command.FramesPerSecond, command.FrameTime95);
        // El cliente puede mentir sobre sus FPS. Online solo decide el coste
        // medido por el servidor; la calidad visual sigue siendo local.
        if (!_statistics.Network)
        {
            progress.Performance.Observe(command.FramesPerSecond, command.FrameTime95);
            TrimDistantDefenseMinions();
        }
        return true;
    }

    private bool ApplyDefenseLoadout(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (command.Loadout == null) return false;
        if (progress.EquipmentSetup.Enabled && (!player.Alive || MatchEnded)) return command.Loadout == progress.Loadout;
        var inventory = new AccountProfile("", player.Name, progress.Gold, 0, 0, progress.Loadout, progress.Items.ToArray());
        try
        {
            // Valida las seis ranuras antes de cambiar ninguna. También permite quitar piezas.
            var updated = AccountInventory.ApplyLoadout(inventory, command.Loadout);
            SetManualDefenseLoadout(player, progress, updated.Loadout);
            return true;
        }
        catch (ArgumentException) { return false; }
    }

    private bool SetDefenseShopOpen(Wizard player, DefenseProgress progress, ClientMessage command)
    {
        if (player.Bot || command.Slot is < 0 or > 1) return false;
        bool open = command.Slot == 1;
        if (progress.ShopOpen != open)
        {
            CancelCast(player); player.Input.MoveTo = false; player.Input.MoveX = player.Input.MoveY = 0;
            progress.AssistanceCooldown = 0;
            progress.AssistanceNavigationCooldown = progress.AssistanceAttackSeconds = 0;
            progress.AssistanceGoal = player.Position;
        }
        progress.ShopOpen = open;
        return true;
    }
}
