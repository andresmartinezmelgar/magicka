using Arcana.Core;
using Arcana.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;

internal static class DefenseRewardsEmulatorTests
{
    // Verificación autónoma: sin cliente Google, Firebase CLI, credenciales ni proyectos reales.
    public static async Task Run()
    {
        string endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "";
        if (!Uri.TryCreate("http://" + endpoint, UriKind.Absolute, out var address) || !address.IsLoopback || address.Port < 1024)
            throw new InvalidOperationException("Solo se permite un emulador Firestore en loopback.");
        var db = new FirestoreDbBuilder { ProjectId = "demo-arcana-defense", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var repository = new FirestoreAccountRepository(db);
        string uid = "fixture_" + Guid.NewGuid().ToString("N"), run = Guid.NewGuid().ToString("N");
        await repository.Profile(uid, "Defensor de prueba");
        for (int checkpoint = 1; checkpoint <= 4; checkpoint++)
        {
            var reward = new RewardMatch(RewardRules.DefenseReceiptId(run, uid, checkpoint), "local-emulator", GameMode.Defense,
                ArenaMapId.Bastion, DateTimeOffset.UtcNow, [new(uid, 1, 0, checkpoint * 300, true)], 2, run, checkpoint);
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.Award(reward)));
            var profile = await repository.Profile(uid, "Defensor de prueba");
            if (profile.Items.Length != checkpoint || profile.Wins != 0 || profile.Losses != 0 || profile.Gold != 0 || !profile.Items.All(EquipmentCatalogue.Valid))
                throw new InvalidOperationException("Premio duplicado, inválido o mezclado con PvP.");
            Console.WriteLine($"PASS Defensa emulador: hito {checkpoint}, tres reintentos concurrentes, {profile.Items.Length} objetos.");
        }
        var fifth = new RewardMatch(RewardRules.DefenseReceiptId(run, uid, 5), "local-emulator", GameMode.Defense,
            ArenaMapId.Bastion, DateTimeOffset.UtcNow, [new(uid, 1, 0, 1500, true)], 2, run, 5);
        await repository.Award(fifth);
        if ((await repository.Profile(uid, "Defensor de prueba")).Items.Length != 4)
            throw new InvalidOperationException("Se superó el máximo de cuatro premios.");
        Console.WriteLine("DEFENSE_REWARDS_EMULATOR_OK: cuatro premios, reintentos seguros y estadísticas PvP intactas.");
    }
}
