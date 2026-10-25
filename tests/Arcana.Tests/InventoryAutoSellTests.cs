using Arcana.Core;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class InventoryAutoSellTests
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static EquipmentItem Item(int seed = 901) => EquipmentCatalogue.Generate(new(seed), tier: ItemTier.Gold, slot: EquipmentSlot.Boots);
    private static EquipmentItem New(EquipmentItem item) => item with { Id = Guid.NewGuid().ToString("N") };
    private static readonly MethodInfo Grant = typeof(GameWorld).GetMethod("GrantDefenseItem", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Give(GameWorld world, Wizard player, EquipmentItem item) => Grant.Invoke(world, [player, item]);
    private static DefensePlayerSnapshot State(GameWorld world, Wizard player) => world.Snapshot().Defense!.Defenders.Single(p => p.PlayerId == player.Id);
    private static bool Send(GameWorld world, Wizard player, ClientMessage command)
    { command.Sequence = player.LastSequence + 1; return world.ApplyMessage(player.Id, Protocol.Deserialize<ClientMessage>(Protocol.Serialize(command))!); }

    public static void Run(Action<string, Action> test)
    {
        test("Venta automática: vende solo nuevos inferiores de la misma ranura y familia; protege actuales, favoritos y equipo", () =>
        {
            var a = Item(); var old = New(a) with { Tier = ItemTier.Silver };
            var b = New(old); var c = New(old); var favorite = New(old);
            var profile = new AccountProfile("owner", "Mago", 0, 0, 0, new(), [a, old]);
            var prefs = new InventoryPreferences(); var style = EquipmentStyle.Of(a);
            prefs.SetAutoSell(style, true, profile);
            prefs = JsonSerializer.Deserialize<InventoryPreferences>(JsonSerializer.Serialize(prefs))!; prefs.Normalize();
            prefs.Favorites.Add(favorite.Id);
            profile = profile with { Loadout = new(b.Id), Items = [a, old, b, c, favorite, New(a),
                New(a) with { Tier = ItemTier.Platinum }, New(old) with { Slot = EquipmentSlot.Hat },
                New(old) with { Family = EquipmentFamily.Fire }] };
            Check(prefs.AutomaticSales(profile).Single().Id == c.Id, "Se vende una pieza anterior, favorita, equipada o de otro estilo.");
            Check(prefs.Sellable(profile, profile.Items.Select(i => i.Id)).All(i => i.Id != b.Id && i.Id != favorite.Id), "La venta manual permite favoritos.");
            prefs.SetAutoSell(style, false, profile); Check(prefs.AutomaticSales(profile).Length == 0, "Desactivar no conserva los objetos.");
            prefs.SetAutoSell(style, true, profile); Check(prefs.AutomaticSales(profile).Length == 0, "Reactivar vende objetos que ya existían.");
            var higherLimit = style with { Tier = ItemTier.Platinum };
            prefs.SetAutoSell(higherLimit, true, profile);
            Check(prefs.AutoSellRules.Count == 1 && !prefs.AutoSells(style) && prefs.AutoSells(higherLimit) && prefs.AutomaticSales(profile).Length == 0,
                "Cambiar el límite acumula filtros o vende objetos que ya existían.");
            var fresh = New(a);
            Check(prefs.AutomaticSales(profile with { Items = [.. profile.Items, fresh, New(a) with { Tier = ItemTier.Platinum }] }).Single().Id == fresh.Id,
                "El nuevo límite vende la calidad de referencia o ignora una inferior.");
        });
        test("Venta automática: cada calidad usa un límite estricto y las reglas antiguas se desactivan al migrar", () =>
        {
            var item = Item();
            foreach (var limit in Enum.GetValues<ItemTier>())
            foreach (var tier in Enum.GetValues<ItemTier>())
                Check((EquipmentStyle.Of(item) with { Tier = limit }).MatchesLowerQuality(item with { Tier = tier }) == (tier < limit),
                    "La frontera de calidad incluye iguales, superiores o excluye inferiores.");
            var prefs = new InventoryPreferences { AutoSellRules = [new(EquipmentStyle.Of(item), [])] };
            Check(prefs.AutomaticSales(new("owner", "Mago", 0, 0, 0, new(), [item])).Length == 0, "Se aplica una regla antigua antes de migrarla.");
            prefs.Normalize(); Check(prefs.AutoSellRules.Count == 0, "La migración amplía una regla antigua.");
            try { prefs.SetAutoSell(EquipmentStyle.Of(item) with { Tier = ItemTier.Silver }, true, new("owner", "Mago", 0, 0, 0, new(), [item]));
                throw new Exception("Se activa un límite inferior a Plata."); } catch (ArgumentException) { }
        });
        test("Supervivencia: favoritos autoritativos, filtros privados y oro exacto solo para objetos nuevos", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Seed = 902 });
            var p = w.AddPlayer("Mago")!; var other = w.AddPlayer("Aliado")!;
            var a = Item(); var b = New(a); Give(w, p, a); Give(w, p, b);
            Check(!Send(w, other, new() { Type = "defense_favorite", ItemId = b.Id, Slot = 1 }), "Se marca un objeto de otro jugador.");
            Check(Send(w, p, new() { Type = "defense_favorite", ItemId = b.Id, Slot = 1 }) &&
                !Send(w, p, new() { Type = "defense_sell", ItemId = b.Id }), "El servidor vende el favorito sin equipar.");
            var style = EquipmentStyle.Of(a);
            Check(!Send(w, other, new() { Type = "defense_auto_sell", AutoSellStyle = style, ItemId = a.Id, Slot = 1 }) &&
                !Send(w, p, new() { Type = "defense_auto_sell", AutoSellStyle = style, Slot = 1 }), "Activa un límite sin el objeto propio que lo origina.");
            Check(Send(w, p, new() { Type = "defense_auto_sell", AutoSellStyle = style, ItemId = a.Id, Slot = 1 }), "No acepta el estilo serializado.");
            var before = State(w, p); var fresh = New(a) with { Tier = ItemTier.Silver }; Give(w, p, fresh); var after = State(w, p);
            Check(after.Items.Length == before.Items.Length && after.Items.All(i => i.Item.Id != fresh.Id) &&
                after.Gold == before.Gold + fresh.SaleGold && after.AutoSold == before.AutoSold + 1 && after.Favorites.Contains(b.Id), "Pierde piezas anteriores o calcula mal el oro.");
            Give(w, p, New(a)); Give(w, p, New(a) with { Tier = ItemTier.Platinum });
            Check(State(w, p).Items.Length == after.Items.Length + 2 && State(w, p).AutoSold == after.AutoSold,
                "Supervivencia vende objetos de calidad igual o superior.");
            var privateState = DefenseVisibility.ForPlayer(w.Snapshot(), other.Id);
            var hidden = privateState.Defense!.Defenders.Single(d => d.PlayerId == p.Id);
            Check(hidden.Items.Length == 0 && hidden.Favorites.Length == 0 && hidden.AutoSellStyles.Length == 0, "Filtra preferencias privadas.");
            SnapshotValidation.Check(privateState);
            Check(Send(w, p, new() { Type = "defense_auto_sell", AutoSellStyle = style, Slot = 0 }), "No desactiva el filtro.");
            Give(w, p, New(fresh)); Check(State(w, p).Items.Length == after.Items.Length + 3, "Sigue vendiendo tras desactivar.");
        });
        test("Supervivencia: favoritos y filtros sobreviven al guardado, y los guardados antiguos siguen abriendo", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 1, Seed = 903 });
            var p = w.AddPlayer("Mago")!; var a = Item(); Give(w, p, a);
            Send(w, p, new() { Type = "defense_favorite", ItemId = a.Id, Slot = 1 });
            Send(w, p, new() { Type = "defense_auto_sell", AutoSellStyle = EquipmentStyle.Of(a), ItemId = a.Id, Slot = 1 });
            var save = w.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save); var player = restored.Players[p.Id]; var before = State(restored, player);
            Give(restored, player, New(a) with { Tier = ItemTier.Silver }); var after = State(restored, player);
            Check(after.Favorites.Contains(a.Id) && after.Items.Length == before.Items.Length && after.Gold == before.Gold + a.SaleGold,
                "El guardado pierde la protección o el filtro.");
            var legacy = JsonNode.Parse(save.State.GetRawText())!;
            var progress = legacy["Defenders"]![0]!["Progress"]!.AsObject();
            progress.Remove("AutoSellRuleVersion");
            var migrated = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) });
            Check(State(migrated, migrated.Players[p.Id]).AutoSellStyles.Length == 0 && State(migrated, migrated.Players[p.Id]).Favorites.Contains(a.Id),
                "Un guardado antiguo pierde favoritos o amplía el alcance de sus reglas.");
            progress.Remove("Favorites"); progress.Remove("AutoSellStyles"); progress.Remove("AutoSold");
            var old = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(legacy) });
            Check(State(old, old.Players[p.Id]).AutoSellStyles.Length == 0, "Un guardado antiguo activa filtros nuevos.");
        });
        test("Venta automática: una respuesta perdida exige leer y no duplica el oro al reanudar", () => RunLostResponse().GetAwaiter().GetResult());
        test("Venta automática: reevalúa favoritos y detiene ventas al abrir la interfaz o cambiar la cuenta", () => RunGuards().GetAwaiter().GetResult());
    }

    private static async Task Connect(AccountClient client)
    { var challenge = await client.BeginLogin("https://autosales.example", CancellationToken.None); Check(await client.PollLogin(challenge, CancellationToken.None), "Falla la sesión de prueba."); }
    private static InventoryPreferences Rules(Handler handler)
    {
        var prefs = new InventoryPreferences(); prefs.SetAutoSell(EquipmentStyle.Of(handler.Profile.Items[0]) with { Tier = ItemTier.Platinum },
            true, handler.Profile with { Items = [] }); return prefs;
    }
    private static async Task RunLostResponse()
    {
        var handler = new Handler { LoseResponse = true }; using var http = new HttpClient(handler); using var account = new AccountClient(http);
        await Connect(account); var prefs = Rules(handler); var seller = new InventoryAutoSeller();
        long expected = handler.Profile.Gold + handler.Profile.Items.Sum(i => i.SaleGold);
        try { await seller.Run(account, prefs, handler.Profile.Uid, () => true); throw new Exception("Oculta el fallo de venta."); } catch (HttpRequestException) { }
        Check(account.InventoryNeedsRefresh && handler.Writes == 1 && !seller.Running, "Una respuesta perdida no bloquea las ventas.");
        Check(await seller.Run(account, prefs, handler.Profile.Uid, () => true) == 0 && handler.Writes == 1, "Reenvía sin sincronizar.");
        await account.Refresh(); await seller.Run(account, prefs, handler.Profile.Uid, () => true);
        Check(handler.Writes == 3 && account.Profile!.Items.Length == 0 && account.Profile.Gold == expected, "Duplica una venta o pierde oro.");
    }
    private static async Task RunGuards()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var account = new AccountClient(http);
        await Connect(account); var prefs = Rules(handler); var seller = new InventoryAutoSeller();
        string favorite = handler.Profile.Items[1].Id;
        handler.AfterSale = () => prefs.Favorites.Add(favorite);
        Check(await seller.Run(account, prefs, handler.Profile.Uid, () => handler.Writes == 0) == 1 && handler.Writes == 1, "No se detiene al abrir la interfaz.");
        handler.AfterSale = null;
        Check(await seller.Run(account, prefs, handler.Profile.Uid, () => true) == 1 && account.Profile!.Items.Single().Id == favorite,
            "No reevalúa el favorito entre ventas.");
        handler.Profile = handler.Profile with { Uid = "different-owner" }; await Connect(account);
        int writes = handler.Writes; prefs.Favorites.Clear();
        Check(await seller.Run(account, prefs, "auto-owner", () => true) == 0 && handler.Writes == writes, "Aplica las reglas a otra cuenta.");
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _challenge = Guid.NewGuid().ToString("N");
        public AccountProfile Profile;
        public int Writes;
        public bool LoseResponse;
        public Action? AfterSale;
        public Handler() { var a = Item(); Profile = new("auto-owner", "Mago", 10, 0, 0, new(), [a, New(a), New(a)]); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/account/login/start": result = new LoginChallenge(_challenge, new('p', 64), "https://autosales.example/account/login/" + _challenge + "?key=" + new string('k', 64), DateTimeOffset.UtcNow.AddMinutes(2)); break;
                case "/account/login/poll": result = new LoginPoll(true, new('s', 64), DateTimeOffset.UtcNow.AddHours(1), Profile); break;
                case "/account/profile": result = Profile; break;
                case "/account/sell":
                    var sale = Protocol.Deserialize<InventoryRequest>(await request.Content!.ReadAsStringAsync(token))!;
                    Profile = AccountInventory.Sell(Profile, sale.ItemId); Writes++; AfterSale?.Invoke();
                    if (LoseResponse) { LoseResponse = false; throw new HttpRequestException("Respuesta perdida."); }
                    result = new InventoryResult(Profile, "Vendido"); break;
                default: throw new Exception("Petición inesperada.");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(Protocol.Serialize(result)) };
        }
    }
}
