using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;
using System.Net;
using System.Net.Http.Json;
using System.Security;

internal static class AdvertisingEmulatorTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-22T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static async Task Run(FirestoreDb db, FirestoreAccountRepository repository, Uri address, Func<string, Func<Task>, Task> test)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task Reject<T>(Func<Task> action) where T : Exception
        { try { await action(); throw new Exception("Se aceptó una operación inválida."); } catch (T) { } }
        string User() => "advertising_" + Guid.NewGuid().ToString("N");
        DocumentReference Access(string uid) => db.Collection("users").Document(uid).Collection("configuration").Document("account");
        var document = db.Collection("gameConfig").Document("advertising");

        await test("Anuncios globales: solo el rol admin puede escribir y las revisiones evitan sobrescrituras", async () =>
        {
            await document.SetAsync(new Dictionary<string, object> { ["enabled"] = true, ["revision"] = 0L });
            string ordinary = User(), paid = User(), admin = User(), secondAdmin = User();
            await Access(paid).SetAsync(new Dictionary<string, object> { ["adsRemoved"] = true, ["previewUpdates"] = true, ["matchStatistics"] = true });
            await Access(admin).SetAsync(new Dictionary<string, object> { ["isAdmin"] = true });
            await Access(secondAdmin).SetAsync(new Dictionary<string, object> { ["isAdmin"] = true });
            await Reject<SecurityException>(() => repository.UpdateAdvertising(ordinary, new(false, 0)));
            await Reject<SecurityException>(() => repository.UpdateAdvertising(paid, new(false, 0)));
            var off = await repository.UpdateAdvertising(admin, new(false, 0));
            Check(!off.Enabled && off.Revision == 1 && (await document.GetSnapshotAsync()).GetValue<string>("updatedBy") == admin,
                "El cambio global no persistió con su autor y revisión.");
            await Reject<InvalidOperationException>(() => repository.UpdateAdvertising(secondAdmin, new(true, 0)));
            var on = await repository.UpdateAdvertising(secondAdmin, new(true, off.Revision));
            Check(on.Enabled && on.Revision == 2 && (await new FirestoreAccountRepository(db).Advertising()) == on,
                "La política no persistió tras reiniciar.");
            await Access(admin).UpdateAsync("isAdmin", false);
            await Reject<SecurityException>(() => repository.UpdateAdvertising(admin, new(false, on.Revision)));
            await Reject<ArgumentException>(() => repository.UpdateAdvertising(secondAdmin, new()));
            Check((await Access(paid).GetSnapshotAsync()).GetValue<bool>("adsRemoved"), "El interruptor global borró una compra.");
        });

        await test("Anuncios desactivados: renovación diaria compartida entre dispositivos sin borrar compras", async () =>
        {
            await document.SetAsync(new Dictionary<string, object> { ["enabled"] = false, ["revision"] = 0L });
            try
            {
                string uid = User(); var clock = new Clock();
                var first = new FirestoreAccountRepository(db, clock); var second = new FirestoreAccountRepository(db, clock);
                var initial = await first.Shop(uid, "Jugador");
                await db.Collection("users").Document(uid).UpdateAsync("gold", 100000L);
                await first.Buy(uid, new(initial.Id, initial.Offers[0].Item.Id));
                await Reject<InvalidOperationException>(() => first.StartVerifiedShopAd(uid, new(initial.Id)));
                var before = await first.Profile(uid, "Jugador");
                var renewed = await Task.WhenAll(first.RefreshShopWithoutAd(uid, new(initial.Id)),
                    second.RefreshShopWithoutAd(uid, new(initial.Id)), second.RefreshShopWithoutAd(uid, new(initial.Id)));
                var current = renewed[0];
                Check(renewed.All(s => s.Id == current.Id) && current.Revision == initial.Revision + 1 &&
                    current.RewardedRefreshAvailableAt == ShopRules.NextRefreshAt(clock.Now), "Se renovó más de una vez con los anuncios desactivados.");
                await Reject<InvalidOperationException>(() => first.RefreshShopWithoutAd(uid, new(current.Id)));
                await document.UpdateAsync("enabled", true);
                await Reject<InvalidOperationException>(() => second.RefreshShopWithoutAd(uid, new(initial.Id)));
                clock.Now = current.RefreshAt!.Value;
                var tomorrow = await first.Shop(uid, "Jugador");
                await Reject<InvalidOperationException>(() => first.RefreshShopWithoutAd(uid, new(tomorrow.Id)));
                var ticket = await first.StartVerifiedShopAd(uid, new(tomorrow.Id));
                await first.ConfirmVerifiedShopAd(new(uid, ticket.Id, Guid.NewGuid().ToString("N"), clock.Now));
                await document.UpdateAsync("enabled", false);
                var afterAd = await first.Shop(uid, "Jugador");
                await Reject<InvalidOperationException>(() => second.RefreshShopWithoutAd(uid, new(afterAd.Id)));
                var after = await first.Profile(uid, "Jugador");
                Check(before.Gold == after.Gold && before.Items.Select(i => i.Id).SequenceEqual(after.Items.Select(i => i.Id)) && !after.Access.AdsRemoved && !after.Access.IsAdmin,
                    "Desactivar anuncios alteró el inventario, el saldo, las compras o los roles.");
            }
            finally { await document.SetAsync(new Dictionary<string, object> { ["enabled"] = true, ["revision"] = 0L }); }
        });

        await test("Anuncios HTTP: sesión obligatoria, comprador sin control y admin con cambio global", async () =>
        {
            using var http = new HttpClient { BaseAddress = address };
            Check((await http.GetAsync("account/advertising")).StatusCode == HttpStatusCode.Unauthorized,
                "La configuración de anuncios admite peticiones anónimas.");
            Check((await http.PostAsJsonAsync("account/advertising", new AdvertisingUpdate(false, 0), Protocol.Json)).StatusCode == HttpStatusCode.Unauthorized,
                "El interruptor global admite escrituras anónimas.");
            string uid = User();
            await Access(uid).SetAsync(new Dictionary<string, object> { ["adsRemoved"] = true });
            using var client = new AccountClient();
            var challenge = await client.BeginLogin(address.AbsoluteUri, CancellationToken.None);
            using var completion = await http.PostAsJsonAsync("account/login/" + challenge.Id + "/complete",
                new LoginCompleteRequest("verified:" + uid, new Uri(challenge.Url).Query[5..]), Protocol.Json);
            completion.EnsureSuccessStatusCode(); await client.PollLogin(challenge, CancellationToken.None);
            var initial = await client.Advertising();
            http.DefaultRequestHeaders.Authorization = new("Bearer", "verified:" + uid);
            using var denied = await http.PostAsJsonAsync("account/advertising", new AdvertisingUpdate(false, initial.Revision), Protocol.Json);
            Check(denied.StatusCode == HttpStatusCode.Forbidden, "El comprador pudo cambiar anuncios o no recibió 403.");
            await Reject<InvalidOperationException>(() => client.UpdateAdvertising(new(false, initial.Revision)));
            Check(client.SignedIn, "Denegar el rol cerró la sesión del comprador.");
            await Access(uid).UpdateAsync("isAdmin", true);
            try
            {
                var off = await client.UpdateAdvertising(new(false, initial.Revision));
                var shop = await client.Shop();
                Check(!off.Enabled && !shop.AdsEnabled && shop.RewardedAds == null && shop.Profile.Access is { IsAdmin: true, AdsRemoved: true },
                    "La tienda no recibió el estado global o perdió el derecho sin anuncios.");
                await Access(uid).UpdateAsync("isAdmin", false);
                using var revoked = await http.PostAsJsonAsync("account/advertising", new AdvertisingUpdate(true, off.Revision), Protocol.Json);
                Check(revoked.StatusCode == HttpStatusCode.Forbidden, "El rol revocado pudo cambiar anuncios o no recibió 403.");
            }
            finally { await document.SetAsync(new Dictionary<string, object> { ["enabled"] = true, ["revision"] = 0L }); }
        });
    }
}
