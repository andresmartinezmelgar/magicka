using Arcana.Core;
using Arcana.Server;
using System.Net;
using System.Text.Json;

internal static class AuditRegressionTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("OAuth: tipos incorrectos se rechazan sin excepciones", OAuth);
        test("Servidor: Defensa arranca con valores por defecto y admite un único defensor", DefenseConfiguration);
        test("Táctil: un lanzamiento rechazado se reconcilia sin perder pulsaciones pendientes", Selection);
        foreach (string mutation in new[] { "buy", "sell", "loadout", "equip" })
            test($"Inventario: Refresh anterior no sobrescribe {mutation} confirmado", () => StaleRefresh(mutation).GetAwaiter().GetResult());
        test("Tienda: cargar ofertas anteriores no revierte una compra", () => StaleShop().GetAwaiter().GetResult());
    }
    private static void OAuth()
    {
        foreach (string invalid in new[] { "[]", "null", "1", "{}", "{\"installed\":[]}", "{\"installed\":null}",
            "{\"installed\":{\"client_id\":123}}", "{\"installed\":{\"client_id\":null}}", "{\"installed\":{\"client_id\":{}}}" })
            Check(!DesktopOAuthConfiguration.TryParse(invalid, out _, out _), "Se aceptó " + invalid);
        string id = "123456789012-arcana.apps.googleusercontent.com";
        foreach (string field in new[] { "client_secret", "project_id" })
            foreach (object? invalid in new object?[] { 123, null, true, new { }, Array.Empty<string>() })
                Check(!DesktopOAuthConfiguration.TryParse(JsonSerializer.Serialize(new { installed = new Dictionary<string, object?> { ["client_id"] = id, [field] = invalid } }), out _, out _), "Tipo incorrecto en " + field);
        Check(DesktopOAuthConfiguration.TryParse(JsonSerializer.Serialize(new { installed = new { client_id = id, project_id = "arcana-arena-f2997" } }), out var valid, out _) && valid!.ClientId == id, "Se rechazó un JSON válido sin secreto opcional.");
        Check(!DesktopOAuthConfiguration.TryParse(JsonSerializer.Serialize(new { installed = new { client_id = id, project_id = "otro-proyecto" } }), out _, out _), "Se admitió otro proyecto.");
    }
    private static void DefenseConfiguration()
    {
        string[] keys = ["GAME_MODE", "ARENA_MAP", "MAX_PLAYERS", "BOTS"];
        var previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("GAME_MODE", "Defense"); Environment.SetEnvironmentVariable("ARENA_MAP", null);
            foreach (string? capacity in new[] { null, "", "1", "4", "8", "-5" })
            {
                Environment.SetEnvironmentVariable("MAX_PLAYERS", capacity); Environment.SetEnvironmentVariable("BOTS", "99");
                var settings = ServerSettings.FromEnvironment(); var world = new GameWorld(settings.World);
                Check(world.IsDefense && settings.World.Map == ArenaMapId.Bastion && settings.World.MaxPlayers == (capacity == "1" || capacity == "-5" ? 1 : 4) &&
                    settings.World.Bots == settings.World.MaxPlayers - 1 && !settings.World.FriendlyFire, "Configuración Defensa inválida: " + capacity);
            }
            Check(LobbyValidation.IsValid(new ServerInfo("Solo", 27015, 27016, GameMode.Defense, 0, 1, Protocol.Version, false)), "El directorio rechaza Defensa de una plaza.");
        }
        finally { foreach (string key in keys) Environment.SetEnvironmentVariable(key, previous[key]); }
    }
    private static void Selection()
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena }); var player = world.AddPlayer("Táctil")!;
        var prediction = new PredictedElementSelection();
        void Send(string type, Element element = default)
        {
            var message = new ClientMessage { Type = type, Element = element, Sequence = player.LastSequence + 1 };
            prediction.Predict(message); world.ApplyMessage(player.Id, message); prediction.Track(message);
        }
        Send("self"); // Pausa de acción real: el siguiente cast no puede consumir Escudo.
        Send("element", Element.Shield); Send("cast");
        var acknowledged = world.Snapshot().Players.Single();
        Send("element", Element.Earth); // Pulsación aún no incluida en el snapshot recibido.
        prediction.Confirm(acknowledged.Selected, acknowledged.SelectionSequence);
        Check(prediction.Pending && prediction.Elements.SequenceEqual(new[] { Element.Shield, Element.Earth }), "Se perdió la selección retenida o Tierra pendiente.");
        var latest = world.Snapshot().Players.Single(); prediction.Confirm(latest.Selected, latest.SelectionSequence);
        Check(!prediction.Pending && prediction.Elements.SequenceEqual(player.Selection.Elements), "La selección no se reconcilió al confirmar la acción rechazada.");
        var beforeInput = latest.SelectionSequence;
        world.ApplyMessage(player.Id, new() { Type = "input", Sequence = player.LastSequence + 1 });
        Check(world.Snapshot().Players.Single().SelectionSequence == beforeInput, "Los paquetes de movimiento cambian el ACK de elementos.");
        for (int n = 0; n < 70; n++) world.Step();
        Send("cast"); latest = world.Snapshot().Players.Single(); prediction.Confirm(latest.Selected, latest.SelectionSequence);
        Check(world.Snapshot().Walls.Length == 1 && prediction.Elements.Count == 0 && !prediction.Pending, "La combinación confirmada no lanzó la barrera.");
        var roundTrip = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
        Check(roundTrip.Players.Single().SelectionSequence == latest.SelectionSequence, "El protocolo perdió la confirmación.");
        prediction.Reset(); Check(!prediction.Pending && prediction.Elements.Count == 0, "Reset conservó acciones de otra partida.");
    }
    private static async Task Connect(AccountClient client)
    {
        await client.SetFirebaseSession("demo-audit", "audit-user", "Audit", "test-token", DateTimeOffset.UtcNow.AddHours(1));
        client.SetProtectedActionsServer("https://accounts.example");
    }
    private static async Task StaleRefresh(string mutation)
    {
        var handler = new ProfileHandler { SeedItem = mutation != "buy" }; using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client); handler.DelayItems = true;
        var refresh = client.Refresh(); await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (mutation == "buy") await client.BuyShopItem(new(handler.Shop.Id, handler.Item.Id));
        else if (mutation == "sell") await client.Sell(handler.Item.Id);
        else if (mutation == "equip") await client.Equip(handler.Item.Slot, handler.Item.Id);
        else await client.SaveLoadout(new EquipmentLoadout().With(handler.Item.Slot, handler.Item.Id));
        var confirmed = client.Profile; handler.Release.SetResult();
        try { await refresh; throw new Exception("Se publicó la lectura obsoleta."); } catch (OperationCanceledException) { }
        Check(ReferenceEquals(confirmed, client.Profile) && !client.InventoryNeedsRefresh, "La lectura obsoleta alteró el perfil confirmado.");
    }
    private static async Task StaleShop()
    {
        var handler = new ProfileHandler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client); handler.DelayShop = true; var shop = client.Shop(); await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.BuyShopItem(new(handler.Shop.Id, handler.Item.Id)); var confirmed = client.Profile; handler.Release.SetResult();
        try { await shop; throw new Exception("Se publicó la tienda obsoleta."); } catch (OperationCanceledException) { }
        Check(ReferenceEquals(confirmed, client.Profile), "La tienda anterior revirtió la compra.");
    }
    private sealed class ProfileHandler : HttpMessageHandler
    {
        public readonly ItemShop Shop = ShopRules.Generate(new(44));
        public EquipmentItem Item => Shop.Offers[0].Item;
        public bool SeedItem, DelayItems, DelayShop;
        private bool _mutated;
        public readonly TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private AccountProfile Profile => new("audit-user", "Audit", _mutated ? 9750 : 10000, 0, 0, new(), (SeedItem ^ _mutated) ? [Item] : []);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/account/shop/buy" || path == "/account/sell")
            {
                _mutated = true;
                return path.EndsWith("buy", StringComparison.Ordinal) ? Json(new ShopResult(Profile, Shop, "Comprado")) : Json(new InventoryResult(Profile, "Vendido"));
            }
            if (path == "/account/shop")
            {
                var old = Profile; if (DelayShop) { DelayShop = false; Waiting.SetResult(); await Release.Task.WaitAsync(token); }
                return Json(new ShopResult(old, Shop, "Ofertas"));
            }
            if (path.EndsWith("/items", StringComparison.Ordinal))
            {
                var items = Profile.Items; if (DelayItems) { DelayItems = false; Waiting.SetResult(); await Release.Task.WaitAsync(token); }
                return Json(new { documents = items.Select(Document).ToArray() });
            }
            if (path.EndsWith("/loadouts/active", StringComparison.Ordinal)) return request.Method == HttpMethod.Patch ? Json(new { }) : new(HttpStatusCode.NotFound);
            return Json(Document(new { uid = "audit-user", name = "Audit", gold = Profile.Gold, wins = 0, losses = 0, itemCount = Profile.Items.Length, schemaVersion = 1 }));
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Protocol.Json)) };
        private static object Document<T>(T value) => new { fields = JsonSerializer.SerializeToElement(value, Protocol.Json).EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value)) };
        private static object Value(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => new { stringValue = value.GetString() }, JsonValueKind.Number => value.TryGetInt64(out _) ? (object)new { integerValue = value.GetRawText() } : new { doubleValue = value.GetDouble() },
            JsonValueKind.Object => new { mapValue = new { fields = value.EnumerateObject().ToDictionary(p => p.Name, p => Value(p.Value)) } },
            JsonValueKind.Array => new { arrayValue = new { values = value.EnumerateArray().Select(Value).ToArray() } }, _ => new { nullValue = (object?)null }
        };
    }
}
