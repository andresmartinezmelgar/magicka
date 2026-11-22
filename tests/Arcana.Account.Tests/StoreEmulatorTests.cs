using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;

internal static class StoreEmulatorTests
{
    public static async Task Run(FirestoreDb db, FirestoreAccountRepository repository, Func<string, Func<Task>, Task> test)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Reject<T>(Func<Task> action) where T : Exception
        { try { await action(); throw new Exception("Se aceptó una compra inválida."); } catch (T) { } }
        async Task<string> User()
        { string uid = "store_" + Guid.NewGuid().ToString("N"); await repository.Profile(uid, "Tienda"); return uid; }
        await test("Oro de Play: un recibo concede saldo una sola vez entre dispositivos y tras reiniciar", async () =>
        {
            string uid = await User(), other = await User(), receipt = Guid.NewGuid().ToString("N");
            var pack = StoreCatalogue.Products().First(p => p.Gold == 5500);
            var before = await repository.Profile(uid, "Tienda");
            await Task.WhenAll(repository.CreditPlayGold(uid, pack, receipt, false, default),
                new FirestoreAccountRepository(db).CreditPlayGold(uid, pack, receipt, false, default),
                repository.CreditPlayGold(uid, pack, receipt, false, default));
            await new FirestoreAccountRepository(db).CreditPlayGold(uid, pack, receipt, true, default);
            var after = await repository.Profile(uid, "Tienda");
            Check(after.Gold == before.Gold + pack.Gold && after.Items.Length == before.Items.Length && after.Access == before.Access,
                "El recibo duplica oro o altera objetos y permisos.");
            await Reject<InvalidOperationException>(() => repository.CreditPlayGold(other, pack, receipt, false, default));
            await Reject<InvalidOperationException>(() => repository.CreditPlayGold(uid, pack, Guid.NewGuid().ToString("N"), true, default));
            await Reject<ArgumentException>(() => repository.CreditPlayGold(uid, pack with { Gold = 999999 }, receipt, false, default));
            await repository.RequestDeletion(uid);
            await Reject<UnauthorizedAccessException>(() => repository.CreditPlayGold(uid, pack, Guid.NewGuid().ToString("N"), false, default));
            await repository.DeleteAccountData(uid, default);
            var retained = await db.Collection("playPurchases").Document(FirestoreAccountRepository.PlayReceiptId(receipt)).GetSnapshotAsync();
            Check(retained.Exists && retained.GetValue<string>("uid") == "", "El borrado no anonimiza el recibo antirrepetición.");
        });
        await test("Pases de Play: vencimiento remoto, límite de verificación, restauración y revocación independientes", async () =>
        {
            string uid = await User(); var now = DateTimeOffset.UtcNow;
            var config = db.Collection("users").Document(uid).Collection("configuration").Document("account");
            await config.SetAsync(new Dictionary<string, object> { ["isAdmin"] = false, ["adsRemoved"] = false });
            var receipt = new PlayPassReceipt("fixture-pass", StoreCatalogue.PassProduct, "six-months-prepaid", now.AddMonths(6).ToUnixTimeSeconds(), now.ToUnixTimeMilliseconds(), false);
            Check(await repository.StorePlayPass(uid, null, receipt, default), "No se guardó el pase.");
            var access = (await repository.Profile(uid, "Tienda")).Access;
            Check(access.WithoutAds(now) && !access.WithoutAds(now.AddHours(25)) && !access.IsAdmin && !access.AdsRemoved &&
                access.PlayPassExpiresAt == receipt.ExpiresAt, "El pase concede roles o no limita la verificación a 24 horas.");
            var acknowledged = receipt with { Acknowledged = true };
            Check(await new FirestoreAccountRepository(db).StorePlayPass(uid, receipt, acknowledged, default) &&
                !await repository.StorePlayPass(uid, receipt, null, default), "Una lectura antigua revoca una confirmación más reciente.");
            await config.UpdateAsync(new Dictionary<string, object> { ["isAdmin"] = true, ["adsRemoved"] = true });
            Check(await repository.StorePlayPass(uid, acknowledged, null, default), "No se revocó el pase reembolsado.");
            access = (await repository.Profile(uid, "Tienda")).Access;
            Check(access is { IsAdmin: true, AdsRemoved: true, PlayPassValidUntil: 0, PlayPassExpiresAt: 0 } && access.WithoutAds(now),
                "Revocar el pase borra compras manuales o permisos administrativos.");
        });
    }
}
