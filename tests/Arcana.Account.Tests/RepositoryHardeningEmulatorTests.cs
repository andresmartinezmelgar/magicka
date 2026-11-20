using Arcana.Core;
using Arcana.Server;
using Google.Cloud.Firestore;

internal static class RepositoryHardeningEmulatorTests
{
    internal static async Task Run(FirestoreDb database)
    {
        var repository = new FirestoreAccountRepository(database);
        var olderServer = new FirestoreAccountRepository(database);
        string uid = "hardening_" + Guid.NewGuid().ToString("N"), other = "other_" + Guid.NewGuid().ToString("N");
        await repository.Profile(uid, "Caché"); await repository.Profile(other, "Otro jugador");
        var user = database.Collection("users").Document(uid);
        var items = Enumerable.Range(0, 3).Select(i => EquipmentCatalogue.Generate(new Random(100 + i))).ToArray();
        var batch = database.StartBatch();
        foreach (var item in items) batch.Create(user.Collection("items").Document(item.Id), FirestoreData.Map(item));
        batch.Update(user, new Dictionary<string, object> { ["itemCount"] = items.Length }); await batch.CommitAsync();
        _ = await repository.Profile(uid, "Caché");
        await olderServer.Sell(uid, items[0].Id);
        var external = await repository.Profile(uid, "Caché");
        if (external.Items.Any(i => i.Id == items[0].Id) || external.Gold != items[0].SaleGold) throw new Exception("La caché oculta una venta del otro servidor.");
        await repository.Sell(uid, items[1].Id);
        var sold = await repository.Profile(uid, "Caché");
        if (sold.Items.Length != 1 || sold.Items[0].Id != items[2].Id) throw new Exception("La actualización de caché tras vender pierde objetos.");
        var replacement = EquipmentCatalogue.Generate(new Random(200));
        batch = database.StartBatch(); batch.Delete(user.Collection("items").Document(items[2].Id));
        batch.Create(user.Collection("items").Document(replacement.Id), FirestoreData.Map(replacement));
        batch.Update(user, new Dictionary<string, object> { ["itemCount"] = 1, ["gold"] = sold.Gold }); await batch.CommitAsync();
        var replaced = await repository.Profile(uid, "Caché");
        if (replaced.Items.Single().Id != replacement.Id) throw new Exception("La caché usa el saldo como revisión y conserva un inventario antiguo.");
        Console.WriteLine("INVENTORY_CACHE_COHERENCE_OK: ventas consecutivas, otra instancia y cambio con el mismo saldo.");

        var match = new RewardMatch(Guid.NewGuid().ToString("N"), "hardening-test", GameMode.Duel, ArenaMapId.Classic,
            DateTimeOffset.UtcNow, [new(uid, 1, 0, 20, true), new(other, 2, 1, 20, false)]);
        var archive = database.Collection("matches").Document(match.Id);
        await archive.SetAsync(FirestoreData.Map(match)); // Archivo legacy con identidades.
        await user.Collection("extra").Document("parent").Collection("nested").Document("child").SetAsync(new { value = 1 });
        var telemetry = database.Collection("dashboardTelemetry").Document(Guid.NewGuid().ToString("N"));
        await telemetry.SetAsync(new { ownerUid = uid });
        await database.Collection("statisticsReaders").Document(uid).SetAsync(new { enabled = true });
        await repository.RequestDeletion(uid); await repository.RequestDeletion(uid);
        try { await repository.Profile(uid, "No debe volver"); throw new Exception("Recreó una cuenta con eliminación pendiente."); }
        catch (UnauthorizedAccessException) { }
        await repository.DeleteAccountData(uid, CancellationToken.None);
        await repository.SaveBestiary(new BestiaryProgress(Guid.NewGuid().ToString("N"), uid, 20, new() { ["runner"] = 1 }));
        await repository.Award(match); // Una entrega encolada tampoco recrea al propietario eliminado.
        var anonymized = FirestoreData.Read<RewardMatch>(await archive.GetSnapshotAsync());
        if ((await user.GetSnapshotAsync()).Exists || (await user.Collection("items").GetSnapshotAsync()).Count != 0 ||
            (await user.Collection("extra").Document("parent").Collection("nested").GetSnapshotAsync()).Count != 0 ||
            (await telemetry.GetSnapshotAsync()).Exists || !await repository.IsDeletionRequested(uid) ||
            anonymized.Participants.Any(p => p.Uid == uid) || !anonymized.Participants.Any(p => p.Uid == other) ||
            !(await database.Collection("users").Document(other).GetSnapshotAsync()).Exists)
            throw new Exception("El borrado omite datos, resucita la cuenta o altera otro participante.");
        await repository.DeleteAccountData(uid, CancellationToken.None); // Reanudar tras un corte es idempotente.
        Console.WriteLine("ACCOUNT_DELETION_DATA_OK: recursión, telemetría, anonimización parcial, reintento y protección contra entregas tardías.");
    }
}
