namespace Arcana.Core;

public static class AccountInventory
{
    public static AccountProfile ApplyLoadout(AccountProfile profile, EquipmentLoadout loadout)
    {
        ArgumentNullException.ThrowIfNull(loadout);
        var updated = profile;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            string id = loadout.Get(slot);
            if (id == null || id.Length > 32) throw new ArgumentException("Selección inválida.");
            updated = Equip(updated, new InventoryRequest(id, slot));
        }
        _ = EquipmentModifiers.Calculate(updated);
        return updated;
    }

    public static AccountProfile Equip(AccountProfile profile, InventoryRequest request)
    {
        if (!Enum.IsDefined(request.Slot)) throw new ArgumentException("Posición desconocida.");
        if (request.ItemId.Length > 0)
        {
            var item = profile.Items.SingleOrDefault(i => i.Id == request.ItemId);
            if (item == null || item.Slot != request.Slot || !EquipmentCatalogue.Valid(item)) throw new ArgumentException("El objeto no pertenece a esta posición o a tu inventario.");
        }
        return profile with { Loadout = profile.Loadout.With(request.Slot, request.ItemId) };
    }
    public static AccountProfile Sell(AccountProfile profile, string itemId)
    {
        var item = profile.Items.SingleOrDefault(i => i.Id == itemId) ?? throw new ArgumentException("El objeto ya no está en tu inventario.");
        if (!EquipmentCatalogue.Valid(item)) throw new InvalidDataException("Objeto inválido.");
        if (profile.Loadout.Get(item.Slot) == itemId) throw new ArgumentException("Desequipa el objeto antes de venderlo.");
        return profile with { Gold = checked(profile.Gold + item.SaleGold),
            Items = profile.Items.Where(i => i.Id != itemId).ToArray() };
    }
}

public sealed record MatchParticipant(string Uid, int PlayerId, int Team, float ActiveSeconds, bool Winner)
{
    public int ExtraGoldPerTwo { get; init; }
    public int GoldBonusPercent { get; init; }
}
public sealed record RewardMatch(string Id, string ServerId, GameMode Mode, ArenaMapId Map, DateTimeOffset FinishedAt,
    MatchParticipant[] Participants, int RulesVersion = 1, string DefenseRunId = "", int DefenseCheckpoint = 0);
public static class RewardRules
{
    public const float MinimumActiveSeconds = 15;
    public const int PvpWinGold = 50, PvpLossGold = 25;
    public static int Gold(RewardMatch match, MatchParticipant player, int convertedItemGold = 0)
    {
        if (convertedItemGold is < 0 or > 100000) throw new ArgumentException("Oro de conversión inválido.");
        if (match.Mode == GameMode.Defense || match.RulesVersion != 3) return convertedItemGold;
        int coins = 0, percentage = 0;
        return GoldBonuses.Apply((player.Winner ? PvpWinGold : PvpLossGold) + convertedItemGold,
            player.ExtraGoldPerTwo, player.GoldBonusPercent, ref coins, ref percentage);
    }
    public static string DefenseReceiptId(string runId, string uid, int checkpoint)
    {
        byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"defense:{runId}:{uid}:{checkpoint}"));
        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }
    public static MatchParticipant[] Eligible(RewardMatch match)
    {
        if (!Guid.TryParseExact(match.Id, "N", out _) || ArenaMap.Get(match.Map).IsPractice) return [];
        if (match.Mode == GameMode.Defense)
        {
            if (match.Map != ArenaMapId.Bastion || match.RulesVersion != 2 || match.DefenseCheckpoint is < 1 or > DefenseRules.RewardLimit ||
                !Guid.TryParseExact(match.DefenseRunId, "N", out _) || match.Participants is not { Length: 1 }) return [];
            var p = match.Participants[0];
            return p.Uid.Length > 0 && p.Team == 0 && p.Winner && float.IsFinite(p.ActiveSeconds) &&
                p.ActiveSeconds >= match.DefenseCheckpoint * DefenseRules.RewardInterval &&
                match.Id == DefenseReceiptId(match.DefenseRunId, p.Uid, match.DefenseCheckpoint) ? [p] : [];
        }
        if (match.DefenseCheckpoint != 0 || match.DefenseRunId.Length > 0 || match.Map == ArenaMapId.Bastion ||
            match.RulesVersion is not (1 or 3) || match.Participants.Any(p => !GoldBonuses.Valid(p.ExtraGoldPerTwo, p.GoldBonusPercent))) return [];
        var players = match.Participants.Where(p => p.Uid.Length > 0 && p.ActiveSeconds >= MinimumActiveSeconds &&
            float.IsFinite(p.ActiveSeconds) && p.Team is 0 or 1).GroupBy(p => p.Uid, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
        return players.Where(p => players.Any(q => q.Uid != p.Uid && (match.Mode == GameMode.Arena || q.Team != p.Team))).ToArray();
    }
}
