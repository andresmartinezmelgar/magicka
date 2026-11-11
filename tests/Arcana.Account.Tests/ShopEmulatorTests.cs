using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

internal static class ShopEmulatorTests
{
    private sealed class ShopClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public static async Task Run(FirestoreDb db, FirestoreAccountRepository repository, Uri address, Func<string, Func<Task>, Task> test)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Reject<T>(Func<Task> action) where T : Exception
        { try { await action(); throw new Exception("Se aceptó una operación inválida."); } catch (T) { } }
        async Task<string> User(long gold = 100000)
        {
            string uid = "shop_" + Guid.NewGuid().ToString("N"); await repository.Profile(uid, "Comprador");
            await db.Collection("users").Document(uid).UpdateAsync("gold", gold); return uid;
        }
        await test("Tienda Firestore: seis ofertas persisten al cerrar y reiniciar el repositorio", async () =>
        {
            string uid = await User(); var first = await repository.Shop(uid, "Comprador");
            var reopened = await new FirestoreAccountRepository(db).Shop(uid, "Comprador");
            Check(ShopRules.Valid(first) && Protocol.Serialize(first) == Protocol.Serialize(reopened), "Reabrir o reiniciar volvió a sortear las ofertas.");
        });
        await test("Tienda diaria Firestore: varios dispositivos renuevan una vez y conservan compras y saldo", async () =>
        {
            string uid = await User();
            var clock = new ShopClock(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
            var firstDevice = new FirestoreAccountRepository(db, clock);
            var secondDevice = new FirestoreAccountRepository(db, clock);
            var initial = await firstDevice.Shop(uid, "Comprador");
            var purchase = new ShopPurchaseRequest(initial.Id, initial.Offers[0].Item.Id);
            await firstDevice.Buy(uid, purchase);
            var sameDay = await secondDevice.Shop(uid, "Comprador");
            Check(sameDay.Id == initial.Id && sameDay.Offers[0].Sold && sameDay.RefreshAt == initial.RefreshAt,
                "Otro dispositivo renovó las ofertas antes de medianoche o borró una compra.");
            clock.Now = initial.RefreshAt!.Value;
            var renewed = await Task.WhenAll(firstDevice.Shop(uid, "Comprador"), secondDevice.Shop(uid, "Comprador"),
                firstDevice.Shop(uid, "Comprador"));
            var current = renewed[0]; var profile = await firstDevice.Profile(uid, "Comprador");
            Check(renewed.Select(s => s.Id).Distinct().Count() == 1 && current.Id != initial.Id && current.Revision == 2 &&
                current.RefreshAt == ShopRules.NextRefreshAt(clock.Now) && current.Offers.All(o => !o.Sold && initial.Offers.All(old => old.Item.Id != o.Item.Id)) &&
                profile.Gold == 100000 - initial.Offers[0].Price && profile.Items.Single().Id == purchase.ItemId,
                "La rotación se duplicó, no cambió las seis ofertas o modificó el inventario y el oro.");
            Check(!await firstDevice.Buy(uid, purchase), "Reintentar una compra del día anterior volvió a cobrarla.");
            clock.Now = current.RefreshAt!.Value.AddDays(3);
            var afterAbsence = await secondDevice.Shop(uid, "Comprador");
            Check(afterAbsence.Revision == 3 && afterAbsence.RefreshAt == ShopRules.NextRefreshAt(clock.Now) &&
                (await new FirestoreAccountRepository(db, clock).Shop(uid, "Comprador")).Id == afterAbsence.Id,
                "Los días sin entrar generan rondas adicionales o el reinicio vuelve a renovar la tienda.");
        });
        await test("Tienda diaria Firestore: una compra del día anterior se rechaza sin esperar a consultar", async () =>
        {
            string uid = await User();
            var clock = new ShopClock(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
            var daily = new FirestoreAccountRepository(db, clock);
            var shop = await daily.Shop(uid, "Comprador"); clock.Now = shop.RefreshAt!.Value;
            await Reject<InvalidOperationException>(() => daily.Buy(uid, new(shop.Id, shop.Offers[0].Item.Id)));
            var profile = await daily.Profile(uid, "Comprador");
            Check(profile.Gold == 100000 && profile.Items.Length == 0,
                "Se cobró o entregó una oferta que ya había caducado.");
            var renewed = await daily.Shop(uid, "Comprador");
            await Reject<InvalidOperationException>(() => daily.Buy(uid, new(shop.Id, shop.Offers[1].Item.Id)));
            Check(renewed.Id != shop.Id && renewed.Offers.All(o => !o.Sold), "La consulta no generó el catálogo del nuevo día.");
        });
        await test("Tienda diaria Firestore: un catálogo sin fecha se migra una vez y persiste", async () =>
        {
            string uid = await User();
            var clock = new ShopClock(DateTimeOffset.Parse("2026-11-11T12:00:00Z"));
            var daily = new FirestoreAccountRepository(db, clock);
            var shop = await daily.Shop(uid, "Comprador");
            await db.Collection("users").Document(uid).Collection("shop").Document("current").UpdateAsync("refreshAt", FieldValue.Delete);
            var migrated = await daily.Shop(uid, "Comprador");
            var reopened = await new FirestoreAccountRepository(db, clock).Shop(uid, "Comprador");
            Check(migrated.Id != shop.Id && migrated.Revision == shop.Revision + 1 && migrated.RefreshAt == ShopRules.NextRefreshAt(clock.Now) &&
                Protocol.Serialize(migrated) == Protocol.Serialize(reopened), "La migración falta o se repite al consultar de nuevo.");
        });
        await test("Tienda Firestore: tres compras simultáneas cobran y entregan exactamente una vez", async () =>
        {
            string uid = await User(); var shop = await repository.Shop(uid, "Comprador"); var offer = shop.Offers[0];
            var request = new ShopPurchaseRequest(shop.Id, offer.Item.Id);
            var bought = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.Buy(uid, request)));
            var profile = await repository.Profile(uid, "Comprador");
            Check(bought.Count(v => v) == 1 && profile.Gold == 100000 - offer.Price && profile.Items.Length == 1 &&
                profile.Items[0].Bonuses.SequenceEqual(offer.Item.Bonuses) && profile.Loadout == new EquipmentLoadout() &&
                (await repository.Shop(uid, "Comprador")).Offers[0].Sold, "Se duplicó el cobro, se volvió a tirar el objeto o se equipó automáticamente.");
        });
        await test("Tienda Firestore: vender y reintentar una compra no vuelve a entregar el objeto", async () =>
        {
            string uid = await User(); var shop = await repository.Shop(uid, "Comprador"); var item = shop.Offers[0].Item;
            var request = new ShopPurchaseRequest(shop.Id, item.Id);
            await repository.Buy(uid, request); await repository.Sell(uid, item.Id);
            var before = await repository.Profile(uid, "Comprador");
            Check(!await repository.Buy(uid, request), "El recibo de compra dejó de ser idempotente tras vender.");
            var after = await repository.Profile(uid, "Comprador");
            Check(after.Gold == before.Gold && after.Items.Length == 0, "Se cobró o entregó otra vez la pieza vendida.");
        });
        await test("Tienda Firestore: compras simultáneas de ofertas distintas no gastan dos veces el mismo saldo", async () =>
        {
            string uid = await User(); var shop = await repository.Shop(uid, "Comprador");
            var offers = shop.Offers.Take(2).ToArray(); long budget = offers.Max(o => o.Price);
            await db.Collection("users").Document(uid).UpdateAsync("gold", budget);
            async Task<bool> Buy(ShopOffer offer)
            {
                try { return await repository.Buy(uid, new(shop.Id, offer.Item.Id)); }
                catch (InvalidOperationException) { return false; }
            }
            var results = await Task.WhenAll(offers.Select(Buy)); var profile = await repository.Profile(uid, "Comprador");
            var purchased = offers.Single(o => profile.Items.Any(i => i.Id == o.Item.Id));
            Check(results.Count(v => v) == 1 && profile.Items.Length == 1 && profile.Gold == budget - purchased.Price && profile.Gold >= 0,
                "Se aceptaron dos compras con saldo para una sola.");
        });
        await test("Tienda Firestore: saldo insuficiente, inventario lleno y oferta ajena no alteran nada", async () =>
        {
            string uid = await User(0), other = await User(); var shop = await repository.Shop(uid, "Comprador"); var offer = shop.Offers[0];
            await Reject<InvalidOperationException>(() => repository.Buy(uid, new(shop.Id, offer.Item.Id)));
            await repository.Shop(other, "Otro"); await Reject<InvalidOperationException>(() => repository.Buy(other, new(shop.Id, offer.Item.Id)));
            await db.Collection("users").Document(uid).UpdateAsync(new Dictionary<string, object> { ["gold"] = 100000L, ["itemCount"] = EquipmentCatalogue.InventoryLimit });
            await Reject<InvalidOperationException>(() => repository.Buy(uid, new(shop.Id, offer.Item.Id)));
            var header = await db.Collection("users").Document(uid).GetSnapshotAsync();
            Check(header.GetValue<long>("gold") == 100000 && !(await repository.Shop(uid, "Comprador")).Offers[0].Sold,
                "Una compra rechazada cobró oro o retiró la oferta.");
            await header.Reference.UpdateAsync("itemCount", 0);
        });
        await test("Tienda Firestore: anuncio simulado exige espera, pertenece a la cuenta y renueva una vez", async () =>
        {
            string uid = await User(), other = await User(); var shop = await repository.Shop(uid, "Comprador"); await repository.Shop(other, "Otro");
            var ticket = await repository.StartShopAd(uid, new(shop.Id));
            Check(ticket.Id == (await repository.StartShopAd(uid, new(shop.Id))).Id, "Reintentar el inicio creó anuncios adicionales.");
            var request = new ShopAdCompleteRequest(shop.Id, ticket.Id);
            await Reject<InvalidOperationException>(() => repository.CompleteShopAd(uid, request));
            await Reject<InvalidOperationException>(() => repository.CompleteShopAd(other, request));
            await Task.Delay(ShopRules.SimulatedAdSeconds * 1000 + 100);
            var completed = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.CompleteShopAd(uid, request)));
            Check(completed.Select(s => s.Id).Distinct().Count() == 1 && completed[0].Id != shop.Id && completed[0].Revision == 2 &&
                completed[0].Offers.All(o => shop.Offers.All(old => old.Item.Id != o.Item.Id)) && (await repository.Profile(uid, "Comprador")).Gold == 100000,
                "El anuncio renovó varias veces, cobró oro o mantuvo las piezas anteriores.");
            await Reject<InvalidOperationException>(() => repository.Buy(uid, new(shop.Id, shop.Offers[0].Item.Id)));
            Check((await new FirestoreAccountRepository(db).Shop(uid, "Comprador")).Id == completed[0].Id, "El catálogo renovado no persistió.");
        });
        await test("Tienda HTTP: exige sesión y el cliente compra y actualiza saldo e inventario", async () =>
        {
            string uid = await User();
            using var http = new HttpClient { BaseAddress = address };
            Check((await http.GetAsync("account/shop")).StatusCode == HttpStatusCode.Unauthorized, "La tienda acepta consultas anónimas.");
            using var client = new AccountClient();
            var challenge = await client.BeginLogin(address.AbsoluteUri, CancellationToken.None);
            // El inicio de sesión usa la verificación de prueba del servidor HTTP real.
            using var completion = await http.PostAsJsonAsync("account/login/" + challenge.Id + "/complete",
                new LoginCompleteRequest("verified:" + uid, new Uri(challenge.Url).Query[5..]), Protocol.Json);
            completion.EnsureSuccessStatusCode();
            await client.PollLogin(challenge, CancellationToken.None);
            var first = await client.Shop(); var offer = first.Shop.Offers[0];
            var purchase = await client.BuyShopItem(new(first.Shop.Id, offer.Item.Id));
            Check(client.Profile!.Gold == 100000 - offer.Price && purchase.Profile.Items.Single().Id == offer.Item.Id && purchase.Shop.Offers[0].Sold,
                "El cliente no actualizó el oro, el inventario o la oferta comprada.");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "verified:" + uid);
            var fake = await http.PostAsJsonAsync("account/shop/buy", new ShopPurchaseRequest(first.Shop.Id, Guid.NewGuid().ToString("N")), Protocol.Json);
            Check(fake.StatusCode == HttpStatusCode.Conflict, "El servidor aceptó una oferta inventada.");
            foreach (string action in new[] { "start", "complete" })
            {
                var ad = await http.PostAsJsonAsync("account/shop/ad/" + action,
                    new ShopAdCompleteRequest(first.Shop.Id, Guid.NewGuid().ToString("N")), Protocol.Json);
                Check(ad.StatusCode == HttpStatusCode.ServiceUnavailable, "La API permite renovar por anuncios desactivados.");
            }
            var unchanged = await client.Shop();
            Check(unchanged.Shop.Id == first.Shop.Id && unchanged.Profile.Gold == purchase.Profile.Gold && unchanged.Profile.Items.Length == 1,
                "Los anuncios desactivados renovaron el catálogo, cobraron oro o entregaron objetos.");
        });
    }
}
