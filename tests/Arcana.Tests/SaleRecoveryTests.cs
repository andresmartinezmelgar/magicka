using Arcana.Core;
using System.Net;

internal static class SaleRecoveryTests
{
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    private static async Task Connect(AccountClient client)
    {
        var challenge = await client.BeginLogin("https://sales.example", CancellationToken.None);
        Check(await client.PollLogin(challenge, CancellationToken.None), "No se pudo abrir la sesión de prueba.");
    }
    private static async Task Lose(AccountClient client, string id)
    {
        try { await client.Sell(id); throw new Exception("Se ocultó la respuesta perdida."); }
        catch (HttpRequestException) { }
        catch (InvalidOperationException) when (client.InventoryNeedsRefresh) { }
    }
    public static void Run(Action<string, Action> test)
    {
        foreach (bool serverError in new[] { false, true })
            test($"Ventas: {(serverError ? "un 503" : "una respuesta perdida")} exige sincronizar y nunca reenvía la escritura", () => Recover(serverError).GetAwaiter().GetResult());
        foreach (bool freshFirst in new[] { false, true })
            test($"Ventas: una lectura anterior al fallo no restaura datos viejos {(freshFirst ? "después" : "antes")} de sincronizar", () => OldRefresh(freshFirst).GetAwaiter().GetResult());
        test("Ventas: cambiar de sesión descarta el bloqueo de la cuenta anterior", () => NewSession().GetAwaiter().GetResult());
        test("Ventas: cancelar antes de enviar no deja una operación pendiente", () => Canceled().GetAwaiter().GetResult());
    }
    private static async Task Recover(bool serverError)
    {
        var handler = new Handler { FailWithStatus = serverError }; using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client); string first = handler.Profile.Items[0].Id, second = handler.Profile.Items[1].Id;
        await Lose(client, first);
        Check(handler.Writes == 1 && handler.Profile.Items.Length == 1 && client.InventoryNeedsRefresh, "La pérdida de respuesta no marca el inventario sin confirmar.");
        try { await client.Sell(first); throw new Exception("Se reenvió la venta sin confirmar."); } catch (InvalidOperationException) { }
        Check(handler.Writes == 1, "El bloqueo permite contactar de nuevo al endpoint de venta.");
        handler.ReadsOffline = true;
        try { await client.Refresh(); throw new Exception("Se inventó una sincronización sin conexión."); } catch (HttpRequestException) { }
        client.RestoreOfflineProfile(client.Profile!); client.EquipOffline(new EquipmentLoadout());
        Check(client.InventoryNeedsRefresh, "El fallo de lectura o una copia local habilitan la venta.");
        handler.ReadsOffline = false; await client.Refresh();
        Check(!client.InventoryNeedsRefresh && client.Profile!.Items.Single().Id == second && client.Profile.Gold == handler.Profile.Gold,
            "La sincronización no recupera la venta aplicada por el servidor.");
        await client.Sell(second);
        Check(handler.Writes == 2 && client.Profile!.Items.Length == 0 && client.Profile.Gold == handler.Profile.Gold, "La reanudación duplica una escritura o pierde oro.");
    }
    private static async Task OldRefresh(bool freshFirst)
    {
        var handler = new Handler { HoldRead = true }; using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client); var read = client.Refresh(); await handler.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Lose(client, handler.Profile.Items[0].Id);
        if (freshFirst) await client.Refresh();
        handler.ReadRelease.SetResult();
        try { await read; throw new Exception("Se aceptó una lectura anterior a la venta sin confirmar."); } catch (OperationCanceledException) { }
        Check(client.InventoryNeedsRefresh == !freshFirst && (!freshFirst || client.Profile!.Items.Length == 1), "La respuesta antigua alteró el bloqueo o restauró un objeto vendido.");
        if (!freshFirst) await client.Refresh();
        Check(!client.InventoryNeedsRefresh && client.Profile!.Items.Length == 1, "Una lectura nueva no resuelve la incertidumbre.");
    }
    private static async Task NewSession()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client); await Lose(client, handler.Profile.Items[0].Id);
        handler.Profile = handler.Profile with { Uid = "another-owner" };
        await Connect(client); Check(!client.InventoryNeedsRefresh && client.Profile!.Uid == "another-owner", "La sesión nueva heredó una venta pendiente ajena.");
    }
    private static async Task Canceled()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); using var client = new AccountClient(http);
        await Connect(client);
        try { await client.Sell(handler.Profile.Items[0].Id, new CancellationToken(true)); throw new Exception("Se ignoró la cancelación."); } catch (OperationCanceledException) { }
        Check(!client.InventoryNeedsRefresh && handler.Writes == 0, "Cancelar antes de enviar produjo un bloqueo o una escritura.");
    }
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _challenge = Guid.NewGuid().ToString("N");
        public AccountProfile Profile = new("sale-recovery", "Mago", 500, 0, 0, new(),
            [EquipmentCatalogue.Generate(new(201)), EquipmentCatalogue.Generate(new(202))]);
        public bool LoseResponse = true, ReadsOffline, HoldRead, FailWithStatus;
        public int Writes;
        public readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/account/login/start": result = new LoginChallenge(_challenge, new('p', 64), "https://sales.example/account/login/" + _challenge + "?key=" + new string('k', 64), DateTimeOffset.UtcNow.AddMinutes(2)); break;
                case "/account/login/poll": result = new LoginPoll(true, new('s', 64), DateTimeOffset.UtcNow.AddHours(1), Profile); break;
                case "/account/profile":
                    var snapshot = Profile;
                    if (HoldRead) { HoldRead = false; ReadStarted.SetResult(); await ReadRelease.Task.WaitAsync(token); }
                    if (ReadsOffline) throw new HttpRequestException("Lectura sin conexión.");
                    result = snapshot; break;
                case "/account/sell":
                    Writes++; var sale = Protocol.Deserialize<InventoryRequest>(await request.Content!.ReadAsStringAsync(token))!;
                    Profile = AccountInventory.Sell(Profile, sale.ItemId);
                    if (LoseResponse)
                    {
                        LoseResponse = false;
                        if (FailWithStatus) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent(Protocol.Serialize(new ApiError("Respuesta perdida por un 503."))) };
                        throw new HttpRequestException("Respuesta perdida después de confirmar la venta.");
                    }
                    result = new InventoryResult(Profile, "Vendido"); break;
                default: throw new Exception("Petición inesperada: " + request.RequestUri.AbsolutePath);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(Protocol.Serialize(result)) };
        }
    }
}
