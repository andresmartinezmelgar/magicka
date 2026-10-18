using Arcana.Core;
using System.Net;
using System.Text.Json;

internal static class AccountSessionTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        foreach (string operation in new[] { "refresh", "equip", "loadout", "sell", "shop" })
            foreach (bool changeAccount in new[] { false, true })
                test($"Cuentas: {operation} tardío se descarta al {(changeAccount ? "cambiar de cuenta" : "cerrar sesión")}", () => Race(operation, changeAccount).GetAwaiter().GetResult());
        test("Cuentas: un 401 de la cuenta anterior no desconecta la cuenta nueva", () => Race("sell", true, unauthorized: true).GetAwaiter().GetResult());
        test("Cuentas: terminar un cierre heredado no borra un nuevo inicio de sesión", () => LateSignOut().GetAwaiter().GetResult());
        test("Cuentas: un 401 iniciado durante el cambio de cuenta tampoco invalida la sesión nueva", () => DuringLogin().GetAwaiter().GetResult());
        foreach (string operation in new[] { "equip", "loadout" })
            test($"Cuentas: {operation} directo conserva el oro y objetos de una compra simultánea", () => DirectLoadoutKeepsPurchase(operation).GetAwaiter().GetResult());
    }

    private static Task Connect(AccountClient client, string uid) =>
        client.SetFirebaseSession("test-project", uid, uid, "token-" + uid, DateTimeOffset.UtcNow.AddHours(1));
    private static async Task Race(string operation, bool changeAccount, bool unauthorized = false)
    {
        var handler = new DelayedHandler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client, "accountA"); client.SetProtectedActionsServer("https://accounts.example");
        handler.DelayNext = true; handler.Unauthorized = unauthorized;
        Task pending = operation switch
        {
            "refresh" => client.Refresh(), "equip" => client.Equip(EquipmentSlot.Boots, ""), "loadout" => client.SaveLoadout(new()),
            "sell" => client.Sell(Guid.NewGuid().ToString("N")), _ => client.Shop()
        };
        await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.SignOut();
        if (changeAccount) await Connect(client, "accountB");
        handler.Release.SetResult();
        try { await pending; throw new Exception("Se publicó una respuesta de la sesión anterior."); }
        catch (OperationCanceledException) { }
        Check(changeAccount ? client.SignedIn && client.Profile?.Uid == "accountB" && client.Token == "token-accountB" : !client.SignedIn && client.Profile == null,
            "La operación tardía restauró el perfil anterior o alteró la cuenta nueva.");
    }
    private static async Task LateSignOut()
    {
        var handler = new DelayedHandler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        var challenge = await client.BeginLogin("https://accounts.example", CancellationToken.None);
        await client.PollLogin(challenge, CancellationToken.None);
        handler.DelayNext = true;
        var logout = client.SignOut(); await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Connect(client, "accountB"); handler.Release.SetResult(); await logout;
        Check(client.SignedIn && client.Profile?.Uid == "accountB", "El cierre anterior borró la nueva cuenta.");
    }
    private static async Task DuringLogin()
    {
        var handler = new DelayedHandler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client, "accountA"); client.SetProtectedActionsServer("https://accounts.example");
        handler.DelayNext = true; var login = Connect(client, "accountB"); await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        handler.DelayApi = true; var oldRequest = client.Sell(Guid.NewGuid().ToString("N")); await handler.ApiWaiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        handler.Release.SetResult(); await login; handler.ApiRelease.SetResult();
        try { await oldRequest; throw new Exception("Se aceptó una respuesta anterior al inicio de sesión."); }
        catch (OperationCanceledException) { }
        Check(client.SignedIn && client.Profile?.Uid == "accountB", "El 401 invalidó la nueva cuenta.");
    }
    private static async Task DirectLoadoutKeepsPurchase(string operation)
    {
        var handler = new DelayedHandler { GrantOnShop = true };
        using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client, "accountA"); client.SetProtectedActionsServer("https://accounts.example");
        handler.DelayNext = true;
        var saving = operation == "equip" ? client.Equip(EquipmentSlot.Boots, "") : client.SaveLoadout(new());
        await handler.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var bought = await client.Shop();
        Check(client.Profile!.Gold == 750 && client.Profile.Items.Length == 1, "La compra de prueba no actualizó el perfil.");
        handler.Release.SetResult(); var result = await saving;
        Check(client.Profile!.Gold == bought.Profile.Gold && client.Profile.Items.Select(i => i.Id).SequenceEqual(bought.Profile.Items.Select(i => i.Id)) &&
            result.Profile.Gold == bought.Profile.Gold && result.Profile.Items.Length == 1,
            "Guardar equipación restauró el saldo antiguo y eliminó de la memoria el objeto recién comprado.");
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public bool DelayNext, Unauthorized, DelayApi, GrantOnShop;
        public readonly TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ApiWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ApiRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string _challenge = Guid.NewGuid().ToString("N");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string path = request.RequestUri!.AbsolutePath;
            string uid = request.Headers.Authorization?.Parameter == "token-accountB" ? "accountB" : "accountA";
            if (DelayApi && path.StartsWith("/account/", StringComparison.Ordinal))
            { DelayApi = false; ApiWaiting.SetResult(); await ApiRelease.Task.WaitAsync(token); return new(HttpStatusCode.Unauthorized); }
            bool delayed = DelayNext;
            if (delayed) { DelayNext = false; Waiting.SetResult(); await Release.Task.WaitAsync(token); }
            if (delayed && Unauthorized) return new(HttpStatusCode.Unauthorized);
            var profile = new AccountProfile(uid, uid, 1000, 0, 0, new(), []);
            if (path == "/account/login/start") return Json(new LoginChallenge(_challenge, new('p', 64),
                "https://accounts.example/account/login/" + _challenge + "?key=" + new string('b', 64), DateTimeOffset.UtcNow.AddMinutes(2)));
            if (path == "/account/login/poll") return Json(new LoginPoll(true, new('s', 64), DateTimeOffset.UtcNow.AddHours(1), profile));
            if (path == "/account/logout") return Json(new ApiError(""));
            if (path == "/account/shop") return Json(new ShopResult(GrantOnShop
                ? profile with { Gold = 750, Items = [EquipmentCatalogue.Generate(new(74))] } : profile,
                ShopRules.Generate(new(73)), "Ofertas"));
            if (path.StartsWith("/account/", StringComparison.Ordinal)) return Json(new InventoryResult(profile, "Guardado"));
            if (request.Method == HttpMethod.Patch) return Json(new { });
            if (path.EndsWith("/items", StringComparison.Ordinal)) return Json(new { documents = Array.Empty<object>() });
            if (path.EndsWith("/loadouts/active", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            return Json(new { fields = new Dictionary<string, object>
            {
                ["uid"] = new { stringValue = uid }, ["name"] = new { stringValue = uid },
                ["gold"] = new { integerValue = "0" }, ["wins"] = new { integerValue = "0" }, ["losses"] = new { integerValue = "0" },
                ["itemCount"] = new { integerValue = "0" }, ["schemaVersion"] = new { integerValue = "1" }
            } });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Protocol.Json)) };
    }
}
