using Arcana.Core;
using System.Net;
using System.Text.Json;

internal static class AccountLoadoutTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(Protocol.Serialize(value)) };
    private static AccountProfile Profile()
    {
        var items = Enum.GetValues<EquipmentSlot>().Select(slot => EquipmentCatalogue.Generate(new(40 + (int)slot), slot: slot)).ToArray();
        return new("inventory-test", "Mago", 0, 0, 0, new(), items);
    }
    private static EquipmentLoadout Loadout(AccountProfile profile) => new(
        profile.Items.Single(i => i.Slot == EquipmentSlot.Boots).Id, profile.Items.Single(i => i.Slot == EquipmentSlot.Hat).Id,
        profile.Items.Single(i => i.Slot == EquipmentSlot.Chest).Id, profile.Items.Single(i => i.Slot == EquipmentSlot.Weapon).Id);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Run(Action<string, Action> test)
    {
        test("Inventario: el equipo completo se valida sin alterar el perfil original", () =>
        {
            var profile = Profile(); var loadout = Loadout(profile);
            var updated = AccountInventory.ApplyLoadout(profile, loadout);
            Check(updated.Loadout == loadout && profile.Loadout == new EquipmentLoadout(), "La selección modificó el perfil de partida.");
            foreach (var invalid in new[] { loadout with { Hat = loadout.Chest }, loadout with { Weapon = Guid.NewGuid().ToString("N") }, loadout with { Boots = null! } })
            {
                try { AccountInventory.ApplyLoadout(profile, invalid); throw new Exception("Se aceptó un equipo ajeno o incompleto."); }
                catch (ArgumentException) { }
                Check(profile.Loadout == new EquipmentLoadout(), "La validación aplicó parte de un equipo inválido.");
            }
            using var client = new AccountClient(); client.RestoreOfflineProfile(updated); client.EquipOffline(new EquipmentLoadout());
            Check(client.Profile!.Loadout == new EquipmentLoadout() && client.Profile.Items.Length == 4, "Desequipar las cuatro piezas perdió el inventario.");
        });
        test("Inventario Firebase: cuatro ranuras se guardan con un único PATCH y el fallo conserva el perfil", () =>
        {
            var profile = Profile(); var loadout = Loadout(profile);
            int patches = 0; bool fail = false;
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                Check(request.Headers.Authorization?.Parameter == "test-token", "La petición perdió la sesión.");
                if (request.Method == HttpMethod.Patch)
                {
                    patches++;
                    Check(request.RequestUri!.AbsolutePath.EndsWith("/users/inventory-test/loadouts/active"), "Se escribió fuera del equipamiento.");
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    var fields = body.RootElement.GetProperty("fields");
                    Check(fields.EnumerateObject().Count() == 4 && fields.GetProperty("boots").GetProperty("stringValue").GetString() == loadout.Boots &&
                        fields.GetProperty("hat").GetProperty("stringValue").GetString() == loadout.Hat &&
                        fields.GetProperty("chest").GetProperty("stringValue").GetString() == loadout.Chest &&
                        fields.GetProperty("weapon").GetProperty("stringValue").GetString() == loadout.Weapon, "La escritura no contiene las cuatro posiciones finales.");
                    return fail ? new(HttpStatusCode.ServiceUnavailable) : Json(new { });
                }
                Check(request.Method == HttpMethod.Get, "Seleccionar o leer creó una escritura inesperada.");
                if (request.RequestUri!.AbsolutePath.EndsWith("/loadouts/active")) return new(HttpStatusCode.NotFound);
                if (request.RequestUri.AbsolutePath.EndsWith("/items")) return Json(new { });
                return new(HttpStatusCode.OK) { Content = new StringContent("""
                    {"fields":{"uid":{"stringValue":"inventory-test"},"name":{"stringValue":"Mago"},"gold":{"integerValue":"0"},"wins":{"integerValue":"0"},"losses":{"integerValue":"0"},"itemCount":{"integerValue":"0"},"schemaVersion":{"integerValue":"1"}}}
                    """) };
            }));
            using var client = new AccountClient(http);
            client.SetFirebaseSession("demo-inventory", profile.Uid, profile.Name, "test-token", DateTimeOffset.UtcNow.AddHours(1)).GetAwaiter().GetResult();
            client.RestoreOfflineProfile(profile);
            Check(patches == 0, "La apertura del inventario guardó el equipamiento.");
            fail = true;
            try { client.SaveLoadout(loadout).GetAwaiter().GetResult(); throw new Exception("Se ocultó un fallo al guardar."); }
            catch (HttpRequestException) { }
            Check(patches == 1 && client.Profile!.Loadout == profile.Loadout, "El fallo publicó una configuración sin guardar o reintentó automáticamente.");
            patches = 0; fail = false;
            client.SaveLoadout(loadout).GetAwaiter().GetResult();
            Check(patches == 1 && client.Profile!.Loadout == loadout, "Guardar las cuatro posiciones necesitó más de una escritura.");
            try { client.SaveLoadout(loadout with { Hat = loadout.Chest }).GetAwaiter().GetResult(); throw new Exception("Se guardó una posición ajena."); }
            catch (ArgumentException) { }
            Check(patches == 1, "Un equipo inválido llegó a Firebase.");
        });
        test("Inventario heredado: el cliente envía una sola operación con las cuatro posiciones", () =>
        {
            var profile = Profile(); var loadout = Loadout(profile); int writes = 0;
            string challengeId = Guid.NewGuid().ToString("N");
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                string path = request.RequestUri!.AbsolutePath;
                if (path == "/account/login/start") return Json(new LoginChallenge(challengeId, new('p', 64),
                    "https://accounts.example/account/login/" + challengeId + "?key=" + new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(2)));
                if (path == "/account/login/poll") return Json(new LoginPoll(true, new('s', 64), DateTimeOffset.UtcNow.AddHours(1), profile));
                Check(path == "/account/loadout" && request.Method == HttpMethod.Post, "Se usó una operación por pieza.");
                var received = Protocol.Deserialize<EquipmentLoadout>(await request.Content!.ReadAsStringAsync(token))!;
                writes++; Check(received == loadout, "El servidor no recibió la selección final.");
                return Json(new InventoryResult(AccountInventory.ApplyLoadout(profile, received), "Guardado"));
            }));
            using var client = new AccountClient(http);
            var challenge = client.BeginLogin("wss://accounts.example", CancellationToken.None).GetAwaiter().GetResult();
            Check(client.PollLogin(challenge, CancellationToken.None).GetAwaiter().GetResult(), "Falló la sesión de prueba.");
            client.SaveLoadout(loadout).GetAwaiter().GetResult();
            Check(writes == 1 && client.Profile!.Loadout == loadout, "El guardado completo no se aplicó una sola vez.");
        });
    }
}
