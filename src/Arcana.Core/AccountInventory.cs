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
        var loadout = profile.Loadout.Get(item.Slot) == itemId ? profile.Loadout.With(item.Slot, "") : profile.Loadout;
        return profile with { Gold = checked(profile.Gold + item.SaleGold), Loadout = loadout,
            Items = profile.Items.Where(i => i.Id != itemId).ToArray() };
    }
}

public sealed record MatchParticipant(string Uid, int PlayerId, int Team, float ActiveSeconds, bool Winner);
public sealed record RewardMatch(string Id, string ServerId, GameMode Mode, ArenaMapId Map, DateTimeOffset FinishedAt,
    MatchParticipant[] Participants, int RulesVersion = 1);
public static class RewardRules
{
    public const float MinimumActiveSeconds = 15;
    public static MatchParticipant[] Eligible(RewardMatch match)
    {
        if (!Guid.TryParseExact(match.Id, "N", out _) || ArenaMap.Get(match.Map).IsPractice) return [];
        var players = match.Participants.Where(p => p.Uid.Length > 0 && p.ActiveSeconds >= MinimumActiveSeconds &&
            float.IsFinite(p.ActiveSeconds) && p.Team is 0 or 1).GroupBy(p => p.Uid, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
        return players.Where(p => players.Any(q => q.Uid != p.Uid && (match.Mode == GameMode.Arena || q.Team != p.Team))).ToArray();
    }
}
