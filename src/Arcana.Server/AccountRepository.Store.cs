using Arcana.Core;
using Google.Cloud.Firestore;
using System.Security.Cryptography;
using System.Text;

namespace Arcana.Server;

public sealed partial class FirestoreAccountRepository
{
    internal static string PlayReceiptId(string purchaseToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(purchaseToken)));

    internal Task CreditPlayGold(string uid, StoreProduct product, string purchaseToken, bool consumed, CancellationToken token) =>
        database.RunTransactionAsync(async tx =>
        {
            if (!StoreCatalogue.Products().Any(p => p == product && p.Gold > 0)) throw new ArgumentException("Paquete de oro inválido.");
            if ((await tx.GetSnapshotAsync(Deletion(uid), token)).Exists) throw new UnauthorizedAccessException();
            var doc = database.Collection("playPurchases").Document(PlayReceiptId(purchaseToken));
            var receipt = await tx.GetSnapshotAsync(doc, token);
            if (receipt.Exists)
            {
                if (receipt.GetValue<string>("uid") != uid || receipt.GetValue<string>("product") != product.ProductId ||
                    receipt.GetValue<long>("gold") != product.Gold)
                    throw new InvalidOperationException("El recibo ya está asignado a otra compra o cuenta.");
                return;
            }
            if (consumed) throw new InvalidOperationException("Este recibo ya fue consumido. Consulta el saldo de la cuenta original.");
            var user = User(uid); var current = await tx.GetSnapshotAsync(user, token);
            if (!current.Exists) throw new InvalidOperationException("Carga el perfil antes de confirmar una compra.");
            var header = Header(current);
            tx.Update(user, new Dictionary<string, object> { ["gold"] = checked(header.Gold + product.Gold) });
            tx.Create(doc, new Dictionary<string, object> { ["uid"] = uid, ["product"] = product.ProductId,
                ["gold"] = product.Gold, ["grantedAt"] = FieldValue.ServerTimestamp });
        }, cancellationToken: token);

    internal async Task<PlayPassReceipt?> PlayPassFor(string uid, CancellationToken token)
    {
        var doc = await User(uid).Collection("billing").Document("noAdsPass").GetSnapshotAsync(token);
        return doc.Exists ? FirestoreData.Read<PlayPassReceipt>(doc) : null;
    }

    internal Task<bool> StorePlayPass(string uid, PlayPassReceipt? expected, PlayPassReceipt? next, CancellationToken token) =>
        database.RunTransactionAsync(async tx =>
        {
            if ((await tx.GetSnapshotAsync(Deletion(uid), token)).Exists) throw new UnauthorizedAccessException();
            var doc = User(uid).Collection("billing").Document("noAdsPass");
            var current = await tx.GetSnapshotAsync(doc, token);
            if ((current.Exists ? FirestoreData.Read<PlayPassReceipt>(current) : null) != expected) return false;
            if (next == null) tx.Delete(doc); else tx.Set(doc, FirestoreData.Map(next));
            long lease = next == null ? 0 : Math.Min(next.ExpiresAt,
                DateTimeOffset.FromUnixTimeMilliseconds(next.CheckedAt).AddHours(24).ToUnixTimeSeconds());
            tx.Set(AccountConfigurationDocument(uid), new Dictionary<string, object>
                { ["playPassValidUntil"] = lease, ["playPassExpiresAt"] = next?.ExpiresAt ?? 0 }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: token);
}
