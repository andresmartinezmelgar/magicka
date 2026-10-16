using Arcana.Core;
using Arcana.Server;
using Microsoft.Extensions.Logging.Abstractions;

internal static class RewardOutboxTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Check(condition(), "La recompensa pendiente no se recuperó.");
    }
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "arcana-reward-outage-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("ARCANA_REWARD_OUTBOX");
        var repository = new OutageRepository();
        var accounts = new AccountService(new("", "", "", ""), new Verifier(), repository);
        RewardService Worker() => new(accounts, NullLogger<RewardService>.Instance);
        RewardMatch Reward(int checkpoint)
        {
            string run = "c784b77d0fac4fe4b153864f5b3bdc87", uid = "verified-defender";
            return new(RewardRules.DefenseReceiptId(run, uid, checkpoint), "test-server", GameMode.Defense, ArenaMapId.Bastion,
                DateTimeOffset.UtcNow, [new(uid, 1, 0, checkpoint * 300, true)], 2, run, checkpoint);
        }
        try
        {
            Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", root);
            var first = Reward(1);
            using (var stoppedServer = Worker())
            {
                Check(RewardRules.Eligible(first).Length == 1, "El hito de la prueba no es elegible.");
                Check(stoppedServer.Enqueue(first), "No guardó el hito sin worker ni conexión.");
                Check(File.Exists(Path.Combine(root, first.Id + ".json")), "Se confirmó el hito antes de persistirlo.");
            }
            repository.Offline = true;
            using (var offline = Worker())
            {
                await offline.StartAsync(CancellationToken.None);
                await Until(() => repository.Attempts > 0);
                Check(File.Exists(Path.Combine(root, first.Id + ".json")) && repository.Delivered == 0, "Perdió el premio durante la caída de Firebase.");
                await offline.StopAsync(CancellationToken.None);
            }
            repository.Offline = false;
            using (var recovered = Worker())
            {
                await recovered.StartAsync(CancellationToken.None);
                await Until(() => repository.Delivered == 1 && !Directory.EnumerateFiles(root, "*.json").Any());
                await recovered.StopAsync(CancellationToken.None);
            }
            Console.WriteLine("PASS Premios pendientes: Firebase caído, cierre del servidor y recuperación del diario.");
            var second = Reward(2); repository.LoseConfirmation = true;
            using (var ambiguous = Worker())
            {
                Check(ambiguous.Enqueue(second), "No guardó segundo hito.");
                await ambiguous.StartAsync(CancellationToken.None);
                await Until(() => repository.Delivered == 2);
                Check(File.Exists(Path.Combine(root, second.Id + ".json")), "Borró un hito sin confirmación del servidor.");
                await ambiguous.StopAsync(CancellationToken.None);
            }
            repository.LoseConfirmation = false;
            using (var retry = Worker())
            {
                await retry.StartAsync(CancellationToken.None);
                await Until(() => !Directory.EnumerateFiles(root, "*.json").Any());
                Check(repository.Delivered == 2, "Duplicó un objeto al recuperar una confirmación perdida.");
                await retry.StopAsync(CancellationToken.None);
            }
            Console.WriteLine("PASS Premios pendientes: respuesta perdida tras guardar; recibo entregado exactamente una vez.");
            using (var disabled = new RewardService(new AccountService(new("", "", "", "")), NullLogger<RewardService>.Instance))
            {
                Check(disabled.Enqueue(Reward(3)), "Desactivó el diario al faltar Firebase.");
                await disabled.StartAsync(CancellationToken.None); await Task.Delay(150);
                await disabled.StopAsync(CancellationToken.None);
                Check(Directory.EnumerateFiles(root, "*.json").Count() == 1, "Eliminó el premio sin cuentas habilitadas.");
                Check(!disabled.Enqueue(Reward(5)), "Aceptó un quinto premio.");
            }
            using (var enabled = Worker())
            {
                await enabled.StartAsync(CancellationToken.None);
                await Until(() => repository.Delivered == 3);
                await enabled.StopAsync(CancellationToken.None);
            }
            Console.WriteLine("PASS Premios pendientes: configuración de Firebase ausente, posterior recuperación y tope de cuatro.");
            var interrupted = Reward(4);
            await File.WriteAllTextAsync(Path.Combine(root, interrupted.Id + ".json.tmp"), Protocol.Serialize(interrupted));
            await File.WriteAllTextAsync(Path.Combine(root, "broken.json.tmp"), "{}");
            using (var restored = Worker())
            {
                await restored.StartAsync(CancellationToken.None);
                await Until(() => repository.Delivered == 4 && !Directory.EnumerateFiles(root, "*.json").Any() && !Directory.EnumerateFiles(root, "*.json.tmp").Any());
                Check(File.Exists(Path.Combine(root, "broken.json.tmp.incomplete")), "No conservó el recibo corrupto para revisión.");
                await restored.StopAsync(CancellationToken.None);
            }
            Console.WriteLine("PASS Premios pendientes: recupera una escritura interrumpida y aísla un recibo corrupto sin bloquear premios válidos.");
            Console.WriteLine("REWARD_OUTBOX_OK");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    private sealed class Verifier : IAccountIdentityVerifier
    { public Task<AccountIdentity> Verify(string idToken, CancellationToken token) => Task.FromResult(new AccountIdentity("verified-defender", "Defensor")); }
    private sealed class OutageRepository : IAccountRepository
    {
        private readonly HashSet<string> _receipts = [];
        public volatile bool Offline, LoseConfirmation;
        public int Attempts, Delivered;
        public Task Award(RewardMatch match, CancellationToken token = default)
        {
            Interlocked.Increment(ref Attempts);
            if (Offline) throw new IOException("Firebase no disponible (fallo simulado).");
            lock (_receipts) if (_receipts.Add(match.Id)) Interlocked.Increment(ref Delivered);
            if (LoseConfirmation) throw new IOException("Respuesta del servidor perdida después de confirmar.");
            return Task.CompletedTask;
        }
        public Task<AccountProfile> Profile(string uid, string name, CancellationToken token = default) => throw new NotSupportedException();
        public Task Equip(string uid, InventoryRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public Task<int> Sell(string uid, string itemId, CancellationToken token = default) => throw new NotSupportedException();
    }
}
