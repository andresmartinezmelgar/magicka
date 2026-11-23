using Arcana.Core;
using Arcana.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;

internal sealed partial class AccountIntegrationSuite
{
    public async Task RunHttp(HttpClient http, LoginPoll aliceLogin, LoginPoll bobLogin)
    {
        await Test("Firebase: configuración externa, variables prioritarias y URL HTTPS obligatoria", async () =>
        {
            string path = Path.Combine(Path.GetTempPath(), "arcana-firebase-config-" + suffix + ".json");
            try
            {
                await File.WriteAllTextAsync(path, Protocol.Serialize(options));
                var values = new Dictionary<string, string> { ["ARCANA_FIREBASE_CONFIG"] = path };
                string? Read(string key) => values.GetValueOrDefault(key);
                Check(AccountOptions.Read(Read) == options, "No se cargó la configuración Firebase del archivo.");
                values["FIREBASE_PROJECT_ID"] = " "; Check(AccountOptions.Read(Read) == options, "Una variable vacía anuló la configuración del archivo.");
                values["ARCANA_PUBLIC_URL"] = "https://arcana.example/arena/";
                Check(AccountOptions.Read(Read).PublicUrl == "https://arcana.example/arena", "Las variables no prevalecen sobre el archivo.");
                values["ARCANA_PUBLIC_URL"] = "http://arcana.example";
                await Reject<ArgumentException>(() => Task.FromResult(AccountOptions.Read(Read)));
                values["ARCANA_PUBLIC_URL"] = "https://arcana.example"; values["FIREBASE_AUTH_DOMAIN"] = "bad/domain";
                await Reject<ArgumentException>(() => Task.FromResult(AccountOptions.Read(Read)));
                Check(!AccountOptions.Read(_ => null).Enabled, "Se activó Firebase sin configuración.");
            }
            finally { File.Delete(path); }
        });
        await Test("Google: cliente, página, secretos, verificación, perfil y cierre completan el flujo HTTP", async () =>
        {
            using var client = new AccountClient();
            Check((await client.Configuration("127.0.0.1:28339", CancellationToken.None)).Enabled, "No se anuncia el acceso Google.");
            var challenge = await client.BeginLogin("127.0.0.1:28339", CancellationToken.None);
            using var page = await http.GetAsync(challenge.Url);
            Check(page.IsSuccessStatusCode && page.Headers.CacheControl?.NoStore == true &&
                page.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "La página no protege el secreto de acceso.");
            Check((int)(await http.GetAsync("/account/login/" + challenge.Id + "?key=invalid")).StatusCode == 401, "Se sirve un acceso sin el secreto del navegador.");
            Check(!await client.PollLogin(challenge, CancellationToken.None), "El cliente entró antes de verificar Google.");
            string browser = new Uri(challenge.Url).Query[5..];
            using var forged = await http.PostAsJsonAsync("/account/login/" + challenge.Id + "/complete", new LoginCompleteRequest("forged", browser), Protocol.Json);
            Check((int)forged.StatusCode == 401, "Se admitió una credencial falsa.");
            using var verified = await http.PostAsJsonAsync("/account/login/" + challenge.Id + "/complete", new LoginCompleteRequest("verified:" + alice, browser), Protocol.Json);
            Check(verified.IsSuccessStatusCode && await client.PollLogin(challenge, CancellationToken.None) && client.Profile?.Uid == alice, "No se recuperó la cuenta desde el juego.");
            Check(client.TokenFor("127.0.0.1:28339").Length > 0 && client.TokenFor("otro.example:28339") == "", "Se comparte la sesión con otro servidor.");
            await client.SignOut(); Check(!client.SignedIn && client.Profile == null, "Cerrar sesión dejó credenciales en el cliente.");
        });
        await Test("HTTP: inventario exige sesión y no permite vender objetos de otro usuario", async () =>
        {
            var noAuth = await http.GetAsync("/account/profile"); Check((int)noAuth.StatusCode == 401, "Inventario público.");
            var item = EquipmentCatalogue.Generate(new(10)); await Seed(alice, item);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/account/sell") { Content = JsonContent.Create(new InventoryRequest(item.Id), options: Protocol.Json) };
            request.Headers.Authorization = new("Bearer", bobLogin.AccessToken);
            var response = await http.SendAsync(request); Check((int)response.StatusCode == 409, "Se vendió el objeto de otro usuario.");
            Check((await repository.Profile(alice, "Alice")).Items.Any(i => i.Id == item.Id), "Se borró el objeto ajeno.");
            using var equip = new HttpRequestMessage(HttpMethod.Post, "/account/equip") { Content = JsonContent.Create(new InventoryRequest(item.Id, item.Slot), options: Protocol.Json) };
            equip.Headers.Authorization = new("Bearer", aliceLogin.AccessToken);
            var equipped = await http.SendAsync(equip); Check(equipped.IsSuccessStatusCode, "La API rechazó una posición serializada por el cliente.");
            var saved = Protocol.Deserialize<InventoryResult>(await equipped.Content.ReadAsStringAsync())!;
            Check(saved.Profile.Loadout.Get(item.Slot) == item.Id, "El equipamiento HTTP no se guardó.");
            using var bulk = new HttpRequestMessage(HttpMethod.Post, "/account/loadout") { Content = JsonContent.Create(saved.Profile.Loadout, options: Protocol.Json) };
            bulk.Headers.Authorization = new("Bearer", aliceLogin.AccessToken);
            using var bulkResponse = await http.SendAsync(bulk);
            Check(bulkResponse.IsSuccessStatusCode && Protocol.Deserialize<InventoryResult>(await bulkResponse.Content.ReadAsStringAsync())!.Profile.Loadout == saved.Profile.Loadout,
                "La API rechazó el guardado conjunto del inventario.");
            using var noAuthBulk = await http.PostAsJsonAsync("/account/loadout", saved.Profile.Loadout, Protocol.Json);
            Check((int)noAuthBulk.StatusCode == 401, "El guardado conjunto no exige sesión.");
        });
        await Test("HTTP: mejorar un anillo exige sesión y propietario; repetir el nivel esperado no cobra dos veces", async () =>
        {
            var ring = EquipmentCatalogue.Generate(new(917), tier: ItemTier.Silver, slot: EquipmentSlot.MagicRing, family: EquipmentFamily.Fire);
            await Seed(alice, ring);
            await database.Collection("users").Document(alice).UpdateAsync(new Dictionary<string, object> { ["gold"] = 100000L });
            async Task<HttpResponseMessage> Upgrade(string token)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/account/ring/upgrade")
                    { Content = JsonContent.Create(new RingUpgradeRequest(ring.Id, 0), options: Protocol.Json) };
                if (token.Length > 0) request.Headers.Authorization = new("Bearer", token);
                return await http.SendAsync(request);
            }
            using var denied = await Upgrade(""); Check((int)denied.StatusCode == 401, "La mejora es pública.");
            using var foreign = await Upgrade(bobLogin.AccessToken); Check((int)foreign.StatusCode == 409, "La mejora acepta un anillo ajeno.");
            using var first = await Upgrade(aliceLogin.AccessToken); Check(first.IsSuccessStatusCode, "La API rechaza el anillo del propietario.");
            var before = await repository.Profile(alice, "Alice");
            using var again = await Upgrade(aliceLogin.AccessToken); Check(again.IsSuccessStatusCode, "El reintento seguro no se reconoce.");
            var after = await repository.Profile(alice, "Alice");
            Check(after.Gold == before.Gold && after.Items.Single(i => i.Id == ring.Id).UpgradeLevel == 1, "El reintento HTTP vuelve a cobrar la mejora.");
        });
    }
}
