using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;

internal static class BestiaryEmulatorTests
{
    public static async Task Run(FirestoreDb database)
    {
        var repository = new FirestoreAccountRepository(database);
        string uid = "bestiary_" + Guid.NewGuid().ToString("N");
        await repository.Profile(uid, "Bestiario");
        var progress = new BestiaryProgress(Guid.NewGuid().ToString("N"), uid, 20,
            new() { ["runner"] = 5, ["boss"] = 1 });
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.SaveBestiary(progress)));
        var first = await repository.Profile(uid, "Bestiario");
        if (first.Bestiary.GetValueOrDefault("runner") != 5 || first.Bestiary.GetValueOrDefault("boss") != 1)
            throw new Exception("Los reintentos duplicaron las bajas.");
        var later = progress with { Kills = new() { ["runner"] = 8, ["boss"] = 2 } };
        await Task.WhenAll(repository.SaveBestiary(later), repository.SaveBestiary(progress));
        var another = progress with { RunId = Guid.NewGuid().ToString("N"), Kills = new() { ["runner"] = 4 } };
        await repository.SaveBestiary(another);
        var final = await repository.Profile(uid, "Bestiario");
        if (final.Bestiary["runner"] != 12 || final.Bestiary["boss"] != 2 || final.Gold != 0 ||
            final.Wins != 0 || final.Losses != 0 || final.Items.Length != 0)
            throw new Exception("El bestiario perdió precisión, repitió bajas o alteró las recompensas.");
        foreach (var invalid in new[]
        {
            progress with { Kills = new() { ["unknown"] = 1 } },
            progress with { Kills = new() { ["runner"] = -1 } },
            progress with { RunId = "../otra-cuenta" },
            progress with { ActiveSeconds = double.NaN }
        })
        {
            bool rejected = false;
            try { await repository.SaveBestiary(invalid); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("El servidor aceptó un registro inválido.");
        }
        Console.WriteLine("PASS Bestiario Firestore: transacciones concurrentes, reintentos, acumulados por partida y validación.");
    }
}
