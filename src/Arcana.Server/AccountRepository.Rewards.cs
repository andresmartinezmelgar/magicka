using System.Text.Json;
using Arcana.Core;
using Google.Cloud.Firestore;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    public async Task Award(RewardMatch match, CancellationToken token = default)
    {
        var eligible = RewardRules.Eligible(match);
        if (eligible.Length == 0) return;
        var matchRef = database.Collection("matches").Document(match.Id);
        await database.RunTransactionAsync(async transaction =>
        {
            var existing = await transaction.GetSnapshotAsync(matchRef, token);
            if (!existing.Exists) transaction.Create(matchRef, FirestoreData.Map(match with
                { Participants = match.Participants.Select(p => p with { Uid = "" }).ToArray() }));
        }, cancellationToken: token);
        foreach (var player in eligible)
        {
            // La tirada se mantiene durante todos los reintentos de esta transacción.
            var item = player.Winner ? EquipmentCatalogue.Generate(_loot, match.Id) : null;
            var user = User(player.Uid); var receipt = user.Collection("rewards").Document(match.Id);
            await database.RunTransactionAsync(async transaction =>
            {
                var deleted = await transaction.GetSnapshotAsync(Deletion(player.Uid), token);
                if (deleted.Exists) return;
                var previous = await transaction.GetSnapshotAsync(receipt, token);
                var account = await transaction.GetSnapshotAsync(user, token);
                if (previous.Exists) return;
                if (!account.Exists) throw new InvalidDataException("Falta la cuenta del participante.");
                var h = Header(account);
                bool full = h.ItemCount >= EquipmentCatalogue.InventoryLimit;
                int gold = RewardRules.Gold(match, player, item != null && full ? item.SaleGold : 0);
                if (item != null && !full) transaction.Create(user.Collection("items").Document(item.Id), FirestoreData.Map(item));
                transaction.Update(user, new Dictionary<string, object>
                {
                    ["inventoryUpdatedAt"] = FieldValue.ServerTimestamp,
                    ["wins"] = checked(h.Wins + (match.Mode != GameMode.Defense && player.Winner ? 1 : 0)), ["losses"] = checked(h.Losses + (match.Mode != GameMode.Defense && !player.Winner ? 1 : 0)),
                    ["itemCount"] = h.ItemCount + (item != null && !full ? 1 : 0), ["gold"] = checked(h.Gold + gold)
                });
                transaction.Create(receipt, new Dictionary<string, object>
                { ["winner"] = player.Winner, ["itemId"] = item != null && !full ? item.Id : "", ["gold"] = gold, ["grantedAt"] = Timestamp.GetCurrentTimestamp() });
            }, cancellationToken: token);
        }
    }
}
