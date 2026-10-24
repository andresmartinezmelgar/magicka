namespace Arcana.Core;

public static class AccountInventory
{
    // Frontera de confianza para cachés y respuestas de cuenta: también se
    // validan los objetos sin equipar antes de publicar el perfil en la interfaz.
    internal static void CheckProfile(AccountProfile? profile)
    {
        if (profile == null || profile.SchemaVersion != 1 || profile.Uid is not { Length: > 0 and <= 128 } ||
            string.IsNullOrWhiteSpace(profile.Uid) || profile.Uid.Any(c => c == '/' || char.IsControl(c)) ||
            profile.Name is not { Length: > 0 and <= 64 } || profile.Name.Any(char.IsControl) ||
            profile.Gold < 0 || profile.Wins < 0 || profile.Losses < 0 || profile.Loadout == null ||
            profile.Bestiary == null || profile.Bestiary.Values.Any(n => n < 0) ||
            profile.Items is not { Length: <= EquipmentCatalogue.InventoryLimit } ||
            profile.Items.Any(i => !EquipmentCatalogue.Valid(i)) ||
            profile.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != profile.Items.Length)
            throw new InvalidDataException("Datos de cuenta o inventario inválidos.");
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (profile.Loadout.Get(slot) is not { Length: <= 32 })
                throw new InvalidDataException("Equipamiento de cuenta inválido.");
        _ = EquipmentModifiers.Calculate(profile);
    }

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
    public static AccountProfile UpgradeRing(AccountProfile profile, RingUpgradeRequest request)
    {
        if (!Guid.TryParseExact(request.ItemId, "N", out _) || request.ExpectedLevel is < 0 or >= 10)
            throw new ArgumentException("Mejora de anillo inválida.");
        var item = profile.Items.SingleOrDefault(i => i.Id == request.ItemId)
            ?? throw new ArgumentException("El anillo ya no está en tu inventario.");
        if (!EquipmentCatalogue.Valid(item) || !RingEffects.IsRing(item.Slot))
            throw new ArgumentException("Esta mejora requiere un anillo propio.");
        // Una respuesta perdida puede repetirse con el mismo nivel esperado.
        if (item.UpgradeLevel > request.ExpectedLevel) return profile;
        if (item.UpgradeLevel != request.ExpectedLevel) throw new ArgumentException("Actualiza el inventario antes de mejorar.");
        int cost = DefenseRules.UpgradeCost(item.Tier, item.UpgradeLevel);
        if (profile.Gold < cost) throw new ArgumentException("No tienes suficiente oro para mejorar el anillo.");
        var upgraded = item with { UpgradeLevel = item.UpgradeLevel + 1,
            Bonuses = item.Bonuses.Select(b => b with { Current = Math.Min(b.Maximum, MathF.Round(b.Current + DefenseRules.ItemUpgradeStep, 4)) }).ToArray() };
        return profile with { Gold = profile.Gold - cost, Items = profile.Items.Select(i => i.Id == item.Id ? upgraded : i).ToArray() };
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
    public int ExtraGoldEvery { get; init; }
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
        if (match.Mode == GameMode.Defense || match.RulesVersion is not (3 or 4)) return convertedItemGold;
        int coins = 0, percentage = 0;
        return GoldBonuses.Apply((player.Winner ? PvpWinGold : PvpLossGold) + convertedItemGold,
            match.RulesVersion == 3 ? player.ExtraGoldPerTwo > 0 ? 2 : 0 : player.ExtraGoldEvery,
            player.GoldBonusPercent, ref coins, ref percentage);
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
            match.RulesVersion is not (1 or 3 or 4) || match.Participants.Any(p =>
                !GoldBonuses.Valid(p.ExtraGoldEvery, p.GoldBonusPercent) || p.ExtraGoldPerTwo is < 0 or > 1)) return [];
        var players = match.Participants.Where(p => p.Uid.Length > 0 && p.ActiveSeconds >= MinimumActiveSeconds &&
            float.IsFinite(p.ActiveSeconds) && p.Team is 0 or 1).GroupBy(p => p.Uid, StringComparer.Ordinal).Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();
        return players.Where(p => players.Any(q => q.Uid != p.Uid && (match.Mode == GameMode.Arena || q.Team != p.Team))).ToArray();
    }
}
