using Arcana.Core;
using System.Net;
using System.Text.Json;

internal static class AccountShopPreloadTests
{
    private static void Check(bool valid, string reason) { if (!valid) throw new Exception(reason); }
    private static TaskCompletionSource<ShopResult> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ShopResult Result(AccountClient account, int seed = 17, DateTimeOffset? now = null) =>
        new(account.Profile!, ShopRules.Generate(new(seed), now: now), "Ofertas", now ?? DateTimeOffset.UtcNow);

    internal static void Run(Action<string, Action> test)
    {
        test("Tienda precargada: una consulta compartida y cerrar la pestaña no cancela la carga", () => Shared().GetAwaiter().GetResult());
        test("Tienda precargada: descarta respuestas de otra cuenta y de una sesión anterior del mismo usuario", () => Sessions().GetAwaiter().GetResult());
        test("Tienda precargada: caduca con la hora del servidor y avanza el reloj al reabrir", () => Expiry().GetAwaiter().GetResult());
        test("Tienda precargada: una respuesta anterior no restaura una oferta comprada ni un saldo antiguo", () => Purchase().GetAwaiter().GetResult());
        test("Tienda precargada: un fallo no se reintenta por fotograma y abrir permite recuperar", () => Retry().GetAwaiter().GetResult());
        test("Tienda precargada: el perfil sin sesión no inicia consultas", () => Offline().GetAwaiter().GetResult());
    }

    private static async Task Shared()
    {
        using var fixture = new Fixture(); await fixture.Connect("alpha");
        var cache = new AccountShopPreload(); var response = Pending(); int calls = 0;
        Task<ShopResult> Fetch(CancellationToken token) { calls++; return response.Task.WaitAsync(token); }
        var preload = cache.Load(fixture.Client, Fetch, CancellationToken.None);
        using var tab = new CancellationTokenSource();
        var opened = cache.Load(fixture.Client, Fetch, CancellationToken.None, tab.Token);
        tab.Cancel();
        try { await opened; throw new Exception("Cerrar la pestaña no canceló su espera."); }
        catch (OperationCanceledException) { }
        Check(!preload.IsCompleted && calls == 1, "La pestaña canceló o duplicó la consulta de inicio.");
        response.SetResult(Result(fixture.Client)); await preload.WaitAsync(TimeSpan.FromSeconds(3));
        var reopened = cache.Load(fixture.Client, Fetch, CancellationToken.None);
        Check(reopened.IsCompletedSuccessfully && calls == 1 && (await reopened).Shop.Id == cache.Get(fixture.Client)!.Shop.Id,
            "Reabrir inicia otra consulta o no entrega el catálogo inmediatamente.");
    }

    private static async Task Sessions()
    {
        using var fixture = new Fixture(); await fixture.Connect("alpha");
        var cache = new AccountShopPreload(); var response = Pending();
        var previous = cache.Load(fixture.Client, _ => response.Task, CancellationToken.None);
        var oldResult = Result(fixture.Client);
        await fixture.Connect("beta");
        var current = await cache.Load(fixture.Client, _ => Task.FromResult(Result(fixture.Client, 21)), CancellationToken.None);
        response.SetResult(oldResult);
        try { await previous.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Se aceptó el catálogo de alpha en beta."); }
        catch (OperationCanceledException) { }
        Check(cache.Get(fixture.Client) is { Profile.Uid: "beta" } ready && ready.Shop.Id == current.Shop.Id,
            "La respuesta anterior sustituyó la tienda de la cuenta nueva.");
        await fixture.Connect("beta");
        Check(cache.Get(fixture.Client) == null && cache.NeedsPreload(fixture.Client),
            "Volver a iniciar sesión conserva la caché de la sesión anterior.");
    }

    private static async Task Expiry()
    {
        using var fixture = new Fixture(); await fixture.Connect("alpha");
        var server = DateTimeOffset.Parse("2026-11-12T22:59:50Z");
        var clock = new Clock { WallTime = server.AddYears(20) };
        var cache = new AccountShopPreload(clock);
        var first = Result(fixture.Client, now: server);
        await cache.Load(fixture.Client, _ => Task.FromResult(first), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(2));
        Check(cache.Get(fixture.Client) is { } ready && ready.ServerTime == server.AddSeconds(2),
            "La caché usa el reloj incorrecto del dispositivo o congela la hora al reabrir.");
        clock.Advance(TimeSpan.FromSeconds(10));
        Check(cache.Get(fixture.Client) == null, "Se reutilizan las ofertas tras la medianoche del servidor.");
        var next = Result(fixture.Client, 29, server.AddSeconds(12));
        var renewed = await cache.Load(fixture.Client, _ => Task.FromResult(next), CancellationToken.None);
        Check(renewed.Shop.Id == next.Shop.Id && renewed.Shop.Id != first.Shop.Id, "No se cargó el catálogo del nuevo día.");
    }

    private static async Task Purchase()
    {
        using var fixture = new Fixture(); await fixture.Connect("alpha");
        var cache = new AccountShopPreload(); var first = Result(fixture.Client);
        await cache.Load(fixture.Client, _ => Task.FromResult(first), CancellationToken.None);
        var response = Pending();
        var reading = cache.Load(fixture.Client, _ => response.Task, CancellationToken.None, refresh: true);
        var offer = first.Shop.Offers[0];
        var bought = first with
        {
            Profile = first.Profile with { Gold = first.Profile.Gold - offer.Price, Items = [offer.Item] },
            Shop = first.Shop with { Offers = first.Shop.Offers.Select(o => o.Item.Id == offer.Item.Id ? o with { Sold = true } : o).ToArray() }
        };
        fixture.Client.RestoreOfflineProfile(bought.Profile);
        Check(cache.Store(fixture.Client, fixture.Client.SessionRevision, bought), "No se guardó la compra confirmada.");
        response.SetResult(first);
        try { await reading.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("La lectura anterior sustituyó la compra."); }
        catch (OperationCanceledException) { }
        var ready = cache.Get(fixture.Client)!;
        Check(ready.Shop.Offers[0].Sold && ready.Profile.Gold == bought.Profile.Gold && ready.Profile.Items.Single().Id == offer.Item.Id,
            "Reabrir restaura el saldo o permite comprar otra vez la oferta.");
        fixture.Client.RestoreOfflineProfile(bought.Profile with { Gold = 123 });
        Check(cache.Get(fixture.Client)!.Profile.Gold == 123, "La caché oculta cambios posteriores del saldo.");
    }

    private static async Task Retry()
    {
        using var fixture = new Fixture(); await fixture.Connect("alpha");
        var cache = new AccountShopPreload(); int calls = 0;
        Task<ShopResult> Fetch(CancellationToken _) => ++calls == 1
            ? Task.FromException<ShopResult>(new IOException("Sin conexión")) : Task.FromResult(Result(fixture.Client));
        try { await cache.Load(fixture.Client, Fetch, CancellationToken.None); throw new Exception("Se ocultó el fallo."); }
        catch (IOException) { }
        Check(!cache.NeedsPreload(fixture.Client) && cache.Get(fixture.Client) == null,
            "Un fallo provoca reintentos automáticos por fotograma.");
        await cache.Load(fixture.Client, Fetch, CancellationToken.None);
        Check(calls == 2 && cache.Get(fixture.Client) != null, "Abrir no permite reintentar la carga fallida.");
    }

    private static async Task Offline()
    {
        using var fixture = new Fixture(); var cache = new AccountShopPreload(); int calls = 0;
        fixture.Client.RestoreOfflineProfile(fixture.Handler.Profile);
        Check(!cache.NeedsPreload(fixture.Client), "El perfil guardado se trata como una sesión autenticada.");
        try
        {
            await cache.Load(fixture.Client, _ => { calls++; return Task.FromResult(Result(fixture.Client)); }, CancellationToken.None);
            throw new Exception("Se inició la consulta sin sesión.");
        }
        catch (InvalidOperationException) { }
        Check(calls == 0, "La consulta sin sesión llegó al servidor.");
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset WallTime;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => WallTime;
        public void Advance(TimeSpan elapsed) { _ticks += elapsed.Ticks; WallTime += elapsed; }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly LoginHandler Handler = new();
        private readonly HttpClient _http;
        public readonly AccountClient Client;
        public Fixture() { _http = new(Handler); Client = new(_http); }
        public async Task Connect(string uid)
        {
            Handler.Profile = Handler.Profile with { Uid = uid, Name = uid };
            var challenge = await Client.BeginLogin("https://shop.example", CancellationToken.None);
            Check(await Client.PollLogin(challenge, CancellationToken.None), "No se conectó la cuenta de prueba.");
        }
        public void Dispose() { Client.Dispose(); _http.Dispose(); }
    }

    private sealed class LoginHandler : HttpMessageHandler
    {
        public AccountProfile Profile = new("alpha", "alpha", 10000, 0, 0, new(), []);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            if (request.RequestUri!.AbsolutePath == "/account/login/start")
            {
                string id = Guid.NewGuid().ToString("N"), secret = new('a', 64);
                result = new LoginChallenge(id, secret, "https://shop.example/account/login/" + id + "?key=" + secret,
                    DateTimeOffset.UtcNow.AddMinutes(2));
            }
            else if (request.RequestUri.AbsolutePath == "/account/login/poll")
                result = new LoginPoll(true, new string('t', 64), DateTimeOffset.UtcNow.AddHours(1), Profile);
            else throw new InvalidOperationException("Petición inesperada en la prueba: " + request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(JsonSerializer.Serialize(result, result.GetType(), Protocol.Json)) });
        }
    }
}
