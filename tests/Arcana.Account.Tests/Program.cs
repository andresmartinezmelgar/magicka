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

if (args.Contains("--reward-outbox-only")) { await RewardOutboxTests.Run(); return; }
if (args.Contains("--defense-rewards-only")) { await DefenseRewardsEmulatorTests.Run(); return; }
VerifiedGoogleClaimsTests.Run();
if (args.Contains("--verified-claims-only")) return;

string emulator = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "";
if (!Uri.TryCreate("http://" + emulator, UriKind.Absolute, out var emulatorUri) || !emulatorUri.IsLoopback)
    throw new InvalidOperationException("Estas pruebas requieren FIRESTORE_EMULATOR_HOST apuntando a un emulador local; nunca usan la base real.");
var database = new FirestoreDbBuilder { ProjectId = "demo-arcana-accounts", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
var repository = new FirestoreAccountRepository(database);
await BestiaryEmulatorTests.Run(database);
string suffix = Guid.NewGuid().ToString("N");
string alice = "alice_" + suffix, bob = "bob_" + suffix;
var options = new AccountOptions("demo-arcana-accounts", "test-only-key", "demo-arcana-accounts.firebaseapp.com", "http://127.0.0.1:28339");
var accounts = new AccountService(options, new TestVerifier(), repository);
int passed = 0, failed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
}
async Task Reject<T>(Func<Task> action) where T : Exception
{ try { await action(); throw new Exception("Se aceptó una operación inválida."); } catch (T) { } }
RewardMatch Match(bool winner = true) => new(Guid.NewGuid().ToString("N"), "test-server", GameMode.Duel, ArenaMapId.Classic, DateTimeOffset.UtcNow,
    [new(alice, 1, 0, 20, winner), new(bob, 2, 1, 20, !winner)]);
async Task Seed(string uid, EquipmentItem item)
{
    var user = database.Collection("users").Document(uid);
    await database.RunTransactionAsync(async tx =>
    {
        var doc = await tx.GetSnapshotAsync(user); var header = FirestoreData.Read<AccountHeader>(doc);
        tx.Create(user.Collection("items").Document(item.Id), FirestoreData.Map(item));
        tx.Update(user, new Dictionary<string, object> { ["itemCount"] = header.ItemCount + 1 });
    });
}
async Task<LoginPoll> Login(string uid)
{
    var challenge = accounts.StartLogin(); string browser = new Uri(challenge.Url).Query[5..];
    await accounts.CompleteLogin(challenge.Id, new("verified:" + uid, browser), CancellationToken.None);
    return await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
}

await Test("Firestore: misma UID conserva la cuenta y las lecturas tienen un esquema estable", async () =>
{
    var first = await repository.Profile(alice, "Alice"); var second = await repository.Profile(alice, "Otro nombre");
    _ = await repository.Profile(bob, "Bob");
    Check(first.Uid == second.Uid && first.Gold == 0 && second.Name == "Alice" && second.Items.Length == 0 && second.SchemaVersion == 1, "Se recreó o alteró una cuenta.");
});
await Test("Firestore: premio PvP y resultado se entregan una vez, incluso con tres reintentos concurrentes", async () =>
{
    var match = Match(); await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.Award(match)));
    var profile = await repository.Profile(alice, "Alice"); var loser = await repository.Profile(bob, "Bob");
    Check(profile.Wins == 1 && profile.Items.Length == 1 && loser.Losses == 1 && loser.Items.Length == 0, "Premio repetido o entregado al perdedor.");
    Check(profile.Items[0].SourceMatchId == match.Id && EquipmentCatalogue.Valid(profile.Items[0]), "No se conservaron los valores sorteados.");
    Check((await database.Collection("matches").Document(match.Id).GetSnapshotAsync()).Exists, "Falta el registro del resultado.");
});
await Test("Firestore: guardar cuatro posiciones es atómico y rechaza objetos ajenos o mal colocados", async () =>
{
    string user = "bulk_loadout_" + suffix;
    await repository.Profile(user, "Equipo completo");
    var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(80 + (int)s), slot: s)).ToArray();
    foreach (var item in items) await Seed(user, item);
    var loadout = new EquipmentLoadout(items[0].Id, items[1].Id, items[2].Id, items[3].Id);
    await repository.SaveLoadout(user, loadout);
    Check((await repository.Profile(user, "Equipo completo")).Loadout == loadout, "Se perdió una posición en el guardado conjunto.");
    await Reject<ArgumentException>(() => repository.SaveLoadout(user, loadout with { Hat = loadout.Chest }));
    await Reject<ArgumentException>(() => repository.SaveLoadout(bob, loadout));
    Check((await repository.Profile(user, "Equipo completo")).Loadout == loadout, "Una selección inválida guardó parte de la configuración.");
    await repository.SaveLoadout(user, new EquipmentLoadout());
    Check((await repository.Profile(user, "Equipo completo")).Loadout == new EquipmentLoadout(), "No se pueden quitar las cuatro piezas juntas.");
});
await Test("Firestore: mínimo, máximo y valor actual sobreviven a una nueva instancia del repositorio", async () =>
{
    var original = (await repository.Profile(alice, "Alice")).Items[0];
    var restored = (await new FirestoreAccountRepository(database).Profile(alice, "Alice")).Items.Single(i => i.Id == original.Id);
    Check(restored.Bonuses.SequenceEqual(original.Bonuses) && restored.GenerationVersion == original.GenerationVersion, "El objeto se volvió a sortear.");
});
await Test("Firestore: equipar exige propiedad y posición correctas", async () =>
{
    var item = (await repository.Profile(alice, "Alice")).Items[0];
    await Reject<ArgumentException>(() => repository.Equip(bob, new(item.Id, item.Slot)));
    await Reject<ArgumentException>(() => repository.Equip(alice, new(item.Id, (EquipmentSlot)(((int)item.Slot + 1) % 4))));
    await repository.Equip(alice, new(item.Id, item.Slot));
    Check((await repository.Profile(alice, "Alice")).Loadout.Get(item.Slot) == item.Id, "No se guardó el equipo.");
});
await Test("Firestore: una venta simultánea paga una vez, elimina y desequipa; oro de 64 bits exacto", async () =>
{
    var item = (await repository.Profile(alice, "Alice")).Items[0];
    long original = 9007199254740993L;
    await database.Collection("users").Document(alice).UpdateAsync("gold", original);
    var payments = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.Sell(alice, item.Id)));
    var profile = await repository.Profile(alice, "Alice");
    Check(payments.Sum() == item.SaleGold && profile.Gold == original + item.SaleGold && profile.Items.Length == 0 && profile.Loadout.Get(item.Slot) == "", "Saldo duplicado, impreciso o equipo colgante.");
});
await Test("Firestore: vender y equipar al mismo tiempo no deja referencias a objetos eliminados", async () =>
{
    var item = EquipmentCatalogue.Generate(new(9)); await Seed(alice, item);
    async Task Equip() { try { await repository.Equip(alice, new(item.Id, item.Slot)); } catch (ArgumentException) { } }
    await Task.WhenAll(Equip(), repository.Sell(alice, item.Id));
    var profile = await repository.Profile(alice, "Alice");
    Check(profile.Items.All(i => i.Id != item.Id) && profile.Loadout.Get(item.Slot) != item.Id, "Se conservó una referencia eliminada.");
});
await Test("Firestore: práctica, IA y cuenta repetida no conceden objetos ni victorias", async () =>
{
    var before = await repository.Profile(alice, "Alice"); var match = Match();
    await repository.Award(match with { Map = ArenaMapId.Practice });
    await repository.Award(match with { Participants = [match.Participants[0]] });
    await repository.Award(match with { Participants = [match.Participants[0], match.Participants[1] with { Uid = alice }] });
    var after = await repository.Profile(alice, "Alice");
    Check(after.Wins == before.Wins && after.Items.Length == before.Items.Length, "Se concedió una recompensa inválida.");
});
await Test("Firestore: las cuatro piezas elegidas sobreviven a otro repositorio, nueva sesión y cambio de versión", async () =>
{
    string user = "loadout_" + suffix;
    _ = await repository.Profile(user, "Equipo persistente");
    var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(30 + (int)s),
        tier: ItemTier.Mythic, slot: s, family: EquipmentFamily.Fire)).ToArray();
    foreach (var item in items) { await Seed(user, item); await repository.Equip(user, new(item.Id, item.Slot)); }
    var original = await repository.Profile(user, "Equipo persistente");
    var restored = await new FirestoreAccountRepository(database).Profile(user, "Otra versión del cliente");
    Check(restored.Loadout == original.Loadout && restored.SchemaVersion == original.SchemaVersion &&
        restored.Items.All(i => i.Bonuses.SequenceEqual(items.Single(old => old.Id == i.Id).Bonuses)), "La nueva instancia perdió las piezas o sus valores.");
    var login = await Login(user); Check(login.Profile!.Loadout == original.Loadout, "Una nueva sesión eliminó el equipo guardado.");
    await repository.Equip(user, new("", EquipmentSlot.Hat));
    restored = await new FirestoreAccountRepository(database).Profile(user, "Equipo persistente");
    Check(restored.Loadout.Hat == "" && restored.Loadout.Boots == original.Loadout.Boots && restored.Loadout.Chest == original.Loadout.Chest &&
        restored.Loadout.Weapon == original.Loadout.Weapon, "Quitar un gorro alteró otra posición.");
    accounts.Logout(login.AccessToken);
});
await Test("Firestore: ocho familias nuevas persisten y el catálogo antiguo conserva sus definiciones", async () =>
{
    var legacyDefinition = database.Collection("catalogues").Document("1").Collection("bonusDefinitions").Document("firePower");
    await legacyDefinition.SetAsync(new Dictionary<string, object> { ["generationVersion"] = 1, ["legacyMarker"] = suffix, ["maximum"] = 0.14 });
    var updated = new FirestoreAccountRepository(database);
    foreach (var family in EquipmentCatalogue.FocusedFamilies)
    {
        string user = "element_" + family + "_" + suffix;
        _ = await updated.Profile(user, family.ToString());
        var items = Enum.GetValues<EquipmentSlot>().Select(slot => EquipmentCatalogue.Generate(new(100 + (int)slot),
            tier: ItemTier.Mythic, slot: slot, family: family)).ToArray();
        foreach (var item in items) { await Seed(user, item); await updated.Equip(user, new(item.Id, item.Slot)); }
        var original = await updated.Profile(user, family.ToString());
        var restored = await new FirestoreAccountRepository(database).Profile(user, family.ToString());
        Check(restored.Loadout == original.Loadout && restored.SchemaVersion == 1 && EquipmentModifiers.Calculate(restored).Pieces(family) == 4,
            "Se perdió el conjunto de " + family);
        Check(restored.Items.All(i => i.GenerationVersion == 2 && i.Bonuses.SequenceEqual(items.Single(old => old.Id == i.Id).Bonuses)),
            "Se volvió a sortear una pieza de " + family);
    }
    var old = await legacyDefinition.GetSnapshotAsync();
    Check(old.GetValue<string>("legacyMarker") == suffix && old.GetValue<long>("generationVersion") == 1, "Se sobrescribió el catálogo de la versión anterior.");
    var definitions = await database.Collection("catalogues").Document("2").Collection("bonusDefinitions").GetSnapshotAsync();
    Check(definitions.Count == 22 && definitions.Documents.All(d => d.GetValue<long>("generationVersion") == 2), "No se archivaron los atributos nuevos.");
});
await Test("Acceso: secretos separados para navegador y juego; token inválido y polling ajeno rechazados", async () =>
{
    var challenge = accounts.StartLogin(); var browser = new Uri(challenge.Url).Query[5..];
    await Reject<UnauthorizedAccessException>(() => accounts.Poll(new(challenge.Id, "invalid-secret"), CancellationToken.None));
    await Reject<UnauthorizedAccessException>(() => accounts.CompleteLogin(challenge.Id, new("verified:" + alice, "invalid"), CancellationToken.None));
    await Reject<UnauthorizedAccessException>(() => accounts.CompleteLogin(challenge.Id, new("forged", browser), CancellationToken.None));
    Check(!(await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None)).Ready, "Se autenticó un token falso.");
    await accounts.CompleteLogin(challenge.Id, new("verified:" + alice, browser), CancellationToken.None);
    var session = await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
    var repeated = await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
    Check(session.AccessToken == repeated.AccessToken && accounts.Authenticate(session.AccessToken)?.Uid == alice, "El reintento de acceso cambió la sesión.");
    accounts.Logout(session.AccessToken); Check(accounts.Authenticate(session.AccessToken) == null, "Cerrar sesión dejó un token activo.");
});

var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls(options.PublicUrl);
Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", Path.Combine(Path.GetTempPath(), "arcana-account-live-" + suffix));
builder.Services.ConfigureHttpJsonOptions(json =>
{ foreach (var converter in Protocol.Json.Converters) json.SerializerOptions.Converters.Add(converter); });
builder.Logging.ClearProviders(); builder.Services.AddSingleton(accounts);
builder.Services.AddSingleton(new ServerSettings { Port = 28339, DiscoveryPort = 28340, World = new() { Mode = GameMode.Duel, MaxPlayers = 2, ScoreLimit = 5 } });
builder.Services.AddSingleton<RewardService>(); builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(p => p.GetRequiredService<GameService>());
builder.Services.AddHostedService(p => p.GetRequiredService<RewardService>());
var app = builder.Build(); app.UseWebSockets(); app.MapAccounts(); app.MapItemShop(); app.Map("/play", (HttpContext context, GameService game) => game.HandleConnection(context));
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new(options.PublicUrl) };
var aliceLogin = await Login(alice);
var bobLogin = await Login(bob);
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
await Test("Red: el servidor aplica el equipo guardado y rechaza sesiones duplicadas o inventadas", async () =>
{
    var item = EquipmentCatalogue.Generate(new(11), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Shield);
    await Seed(alice, item); await repository.Equip(alice, new(item.Id, item.Slot));
    await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
    await a.ConnectAsync("127.0.0.1:28339", "A", 0, "", accountToken: aliceLogin.AccessToken);
    await b.ConnectAsync("127.0.0.1:28339", "B", 1, "", accountToken: bobLogin.AccessToken);
    Check(a.Latest!.Players.Single(p => p.Id == a.PlayerId).Equipment?.Weapon == ItemTier.Mythic, "El equipo no llegó a la partida.");
    a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "self" });
    await Task.Delay(250);
    var player = a.Latest!.Players.Single(p => p.Id == a.PlayerId);
    Check(player.ShieldMax > 420, "El arma no modificó la absorción real.");
    var visible = await b.InspectEquipmentAsync(a.PlayerId);
    var equipped = PublicEquipment.Prepare(await repository.Profile(alice, "Alice"));
    Check(visible.Name == "A" && visible.Equipment.Items.Single(i => i.Slot == EquipmentSlot.Weapon).Tier == ItemTier.Mythic &&
        visible.Equipment.Stats.SequenceEqual(equipped.View.Stats), "Otro jugador no recibe los bonos reales del equipo guardado.");
    Check(!Protocol.Serialize(visible).Contains(alice) && !Protocol.Serialize(visible).Contains(item.Id), "La ficha expone la identidad o los IDs del inventario.");
    await Reject<InvalidOperationException>(() => a.InspectEquipmentAsync(999999));
    await repository.Equip(alice, new("", EquipmentSlot.Weapon));
    await Task.Delay(125);
    var stillActive = await b.InspectEquipmentAsync(a.PlayerId);
    Check(stillActive.Equipment.Stats.SequenceEqual(visible.Equipment.Stats), "Cambiar el inventario alteró la ficha de una ronda en marcha.");
    await repository.Equip(alice, new(item.Id, item.Slot));
    await using var duplicate = new ArenaConnection();
    await Reject<InvalidOperationException>(() => duplicate.ConnectAsync("127.0.0.1:28339", "D", 0, "", accountToken: aliceLogin.AccessToken));
    await using var forged = new ArenaConnection();
    await Reject<InvalidOperationException>(() => forged.ConnectAsync("127.0.0.1:28339", "F", 0, "", accountToken: "invalid-account-session-token-123456"));
});
await Test("Bonos públicos: las fichas se limitan a la sala y llegan junto a snapshots sin perderse", async () =>
{
    var game = app.Services.GetRequiredService<GameService>();
    var one = game.CreateRoom(new() { Name = "Fichas uno", Mode = GameMode.Duel, MaxPlayers = 2 });
    var two = game.CreateRoom(new() { Name = "Fichas dos", Mode = GameMode.Duel, MaxPlayers = 2 });
    await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
    await a.ConnectAsync("127.0.0.1:28339", "Uno", 0, "", roomId: one.Room!.Id, accountToken: aliceLogin.AccessToken);
    await b.ConnectAsync("127.0.0.1:28339", "Dos", 0, "", roomId: two.Room!.Id, accountToken: bobLogin.AccessToken);
    await Task.Delay(150);
    Check(a.PlayerId == b.PlayerId, "La prueba no tiene IDs coincidentes de salas distintas.");
    long before = a.Latest!.Tick;
    var views = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => a.InspectEquipmentAsync(b.PlayerId)));
    Check(views.All(v => v.Name == "Uno"), "La consulta cruzó a otra sala.");
    await Task.Delay(100);
    Check(a.Connected && a.Latest!.Tick > before, "Las fichas interrumpieron los snapshots de la partida.");
    await Reject<InvalidOperationException>(() => b.InspectEquipmentAsync(99999));
});
await Test("PvP completo: fin de partida, diario y recompensa llegan a la cuenta sin bloquear la simulación", async () =>
{
    var before = await repository.Profile(alice, "Alice");
    await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
    await a.ConnectAsync("127.0.0.1:28339", "A", 0, "", accountToken: aliceLogin.AccessToken);
    await b.ConnectAsync("127.0.0.1:28339", "B", 1, "", accountToken: bobLogin.AccessToken);
    var start = DateTimeOffset.UtcNow;
    while ((DateTimeOffset.UtcNow - start).TotalSeconds < 18)
    {
        a.Send(new() { Type = "input", MoveX = .1f, AimX = 10 });
        b.Send(new() { Type = "input", MoveX = -.1f, AimX = -10 });
        await Task.Delay(250);
    }
    var game = app.Services.GetRequiredService<GameService>();
    var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    lock (gate) rooms["default"].World.Scores[0] = 5; // Fixture de victoria; el servidor resuelve el fin y toda la entrega.
    AccountProfile after = before;
    for (int n = 0; n < 120 && (after.Wins == before.Wins || after.Items.Length == before.Items.Length); n++)
    {
        await Task.Delay(100);
        after = await repository.Profile(alice, "Alice");
    }
    Check(a.Latest!.MatchEnded && after.Wins == before.Wins + 1 && after.Items.Length == before.Items.Length + 1,
        "El fin de la partida no entregó una única recompensa.");
    long tick = game.CurrentTick; await Task.Delay(150); Check(game.CurrentTick > tick, "La persistencia bloqueó el reloj de simulación.");
});
await Test("Defensa online: hito autenticado con bots, entrega única y sin victorias PvP", async () =>
{
    var before = await repository.Profile(alice, "Alice");
    var game = app.Services.GetRequiredService<GameService>();
    var (created, error, _) = game.CreateRoom(new CreateRoomRequest {
        Name = "Defensa autenticada", Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = 1 });
    var room = created ?? throw new InvalidOperationException(error);
    await using var player = new ArenaConnection();
    await player.ConnectAsync("127.0.0.1:28339", "Defensor", 1, "", roomId: room.Id, accountToken: aliceLogin.AccessToken);
    var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    lock (gate)
    {
        var world = rooms[room.Id].World;
        // Fixture de cinco minutos: no se introduce ningún comando de reloj en el protocolo público.
        typeof(GameWorld).GetProperty("Tick")!.SetValue(world, 18010L);
        typeof(GameWorld).GetField("_defenseElapsed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 300.0);
        var progress = (System.Collections.IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
        var data = progress[player.PlayerId]!;
        data.GetType().GetField("ActiveTicks")!.SetValue(data, 18000L);
    }
    AccountProfile after = before;
    for (int n = 0; n < 100 && after.Items.Length == before.Items.Length; n++)
    { await Task.Delay(100); after = await repository.Profile(alice, "Alice"); }
    Check(after.Items.Length == before.Items.Length + 1 && after.Wins == before.Wins && after.Losses == before.Losses,
        "No se entregó el hito o se alteraron estadísticas PvP.");
    await Task.Delay(300);
    Check((await repository.Profile(alice, "Alice")).Items.Length == after.Items.Length, "Se repitió el mismo hito en otro tick.");
    Check(player.Latest!.Defense!.Defenders.Single(p => p.PlayerId == player.PlayerId).Items.Length == 0,
        "El premio permanente entró en el inventario temporal.");
});
await Test("Defensa online: salida voluntaria y nueva entrada conservan recursos sin protección gratuita", async () =>
{
    var game = app.Services.GetRequiredService<GameService>();
    var (created, error, _) = game.CreateRoom(new CreateRoomRequest { Name = "Recursos al volver", Mode = GameMode.Defense,
        Map = ArenaMapId.Bastion, MaxPlayers = 2, Bots = 0 });
    var room = created ?? throw new InvalidOperationException(error);
    await using var ally = new ArenaConnection();
    await ally.ConnectAsync("127.0.0.1:28339", "Aliado", 0, "", roomId: room.Id, accountToken: bobLogin.AccessToken);
    var player = new ArenaConnection();
    await player.ConnectAsync("127.0.0.1:28339", "Defensor", 0, "", roomId: room.Id, accountToken: aliceLogin.AccessToken);
    var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    lock (gate)
    {
        var world = rooms[room.Id].World;
        typeof(GameWorld).GetField("_spawnClock", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10000f);
        var body = world.Players[player.PlayerId]; body.Health = 100; body.Energy = 5; body.Invulnerable = 0; body.MagickCooldowns[1] = 10;
    }
    int oldId = player.PlayerId; await player.DisposeAsync();
    bool removed = false;
    for (int n = 0; n < 100; n++)
    {
        lock (gate) removed = !rooms[room.Id].World.Players.ContainsKey(oldId);
        if (removed) break;
        await Task.Delay(20);
    }
    Check(removed, "La salida no eliminó la sesión anterior.");
    await using var returned = new ArenaConnection();
    await returned.ConnectAsync("127.0.0.1:28339", "Defensor", 0, "", roomId: room.Id, accountToken: aliceLogin.AccessToken);
    lock (gate)
    {
        var body = rooms[room.Id].World.Players[returned.PlayerId];
        Check(body.Id != oldId && body.Health is > 0 and <= 100 && body.Energy < 25 && body.MagickCooldowns[1] > 8 &&
            body.Invulnerable == 0 && body.Deaths == 0, "La nueva entrada curó, rellenó energía o concedió recarga/protección.");
    }
});
await ShopEmulatorTests.Run(database, repository, new Uri(options.PublicUrl), Test);
await app.StopAsync(); await app.DisposeAsync();

await Test("Diario: un resultado pendiente se recupera al reiniciar sin duplicar la recompensa", async () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "arcana-account-outbox-" + suffix);
    Directory.CreateDirectory(directory); Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", directory);
    var match = Match(); await File.WriteAllTextAsync(Path.Combine(directory, match.Id + ".json"), Protocol.Serialize(match));
    int wins = (await repository.Profile(alice, "Alice")).Wins;
    var worker = new RewardService(accounts, NullLogger<RewardService>.Instance); await worker.StartAsync(CancellationToken.None);
    for (int n = 0; n < 50 && Directory.EnumerateFiles(directory, "*.json").Any(); n++) await Task.Delay(100);
    await worker.StopAsync(CancellationToken.None); worker.Dispose();
    Check(!Directory.EnumerateFiles(directory, "*.json").Any(), "El diario no entregó el resultado.");
    Check((await repository.Profile(alice, "Alice")).Wins == wins + 1, "No se recuperó el resultado.");
    await repository.Award(match); Check((await repository.Profile(alice, "Alice")).Wins == wins + 1, "El reintento duplicó el premio.");
});
await Test("Defensa Firestore: cuatro hitos idempotentes, sorteo PvP y tope de premios", async () =>
{
    string uid = "defense_" + suffix, run = Guid.NewGuid().ToString("N");
    await repository.Profile(uid, "Defensa");
    for (int n = 1; n <= 4; n++)
    {
        var reward = new RewardMatch(RewardRules.DefenseReceiptId(run, uid, n), "test-server", GameMode.Defense,
            ArenaMapId.Bastion, DateTimeOffset.UtcNow, [new(uid, 1, 0, n * 300, true)], 2, run, n);
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.Award(reward)));
    }
    var profile = await repository.Profile(uid, "Defensa");
    Check(profile.Items.Length == 4 && profile.Items.All(EquipmentCatalogue.Valid) && profile.Wins == 0 && profile.Losses == 0 && profile.Gold == 0,
        "Se duplicó un hito o se mezcló con resultados PvP.");
    var invalid = new RewardMatch(RewardRules.DefenseReceiptId(run, uid, 5), "test-server", GameMode.Defense,
        ArenaMapId.Bastion, DateTimeOffset.UtcNow, [new(uid, 1, 0, 1500, true)], 2, run, 5);
    await repository.Award(invalid);
    Check((await repository.Profile(uid, "Defensa")).Items.Length == 4, "Se entregó un quinto premio.");
});
Console.WriteLine($"RESULTADO CUENTAS: {passed} correctas, {failed} fallidas.");
Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class TestVerifier : IAccountIdentityVerifier
{
    public Task<AccountIdentity> Verify(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith("verified:", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        return Task.FromResult(new AccountIdentity(token[9..], "Usuario de prueba"));
    }
}
