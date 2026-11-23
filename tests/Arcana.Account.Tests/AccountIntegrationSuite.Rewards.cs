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

internal sealed partial class AccountIntegrationSuite
{
    public async Task RunRewards()
    {
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
        await Test("Anillos Firestore: seis ranuras y mejoras concurrentes se conservan sin doble cobro", async () =>
        {
            string uid = "rings_" + suffix;
            await repository.Profile(uid, "Anillos");
            var items = Enum.GetValues<EquipmentSlot>().Select(slot => EquipmentCatalogue.Generate(new(800 + (int)slot),
                tier: ItemTier.Silver, slot: slot, family: EquipmentFamily.Fire)).ToArray();
            foreach (var item in items) await Seed(uid, item);
            var loadout = new EquipmentLoadout(); foreach (var item in items) loadout = loadout.With(item.Slot, item.Id);
            await repository.SaveLoadout(uid, loadout);
            await database.Collection("users").Document(uid).UpdateAsync(new Dictionary<string, object> { ["gold"] = 1000000L });
            var ring = items.Single(i => i.Slot == EquipmentSlot.GoldRing); long gold = 1000000;
            for (int level = 0; level < 10; level++)
            {
                var request = new RingUpgradeRequest(ring.Id, level);
                await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.UpgradeRing(uid, request)));
                gold -= DefenseRules.UpgradeCost(ring.Tier, level);
                var profile = await new FirestoreAccountRepository(database).Profile(uid, "Otra sesión");
                Check(profile.Gold == gold && profile.Items.Single(i => i.Id == ring.Id).UpgradeLevel == level + 1 &&
                    profile.Loadout == loadout && EquipmentModifiers.Calculate(profile).ExtraGoldEvery == 10 - level,
                    "Las mejoras concurrentes repiten el cobro o no persisten el nivel y las seis ranuras.");
            }
            await Reject<ArgumentException>(() => repository.UpgradeRing(bob, new(ring.Id, 0)));
            await Reject<ArgumentException>(() => repository.UpgradeRing(uid, new(items[0].Id, 0)));
            await Reject<ArgumentException>(() => repository.UpgradeRing(uid, new(ring.Id, 10)));
            await Reject<ArgumentException>(() => repository.Sell(uid, ring.Id));
            var magic = items.Single(i => i.Slot == EquipmentSlot.MagicRing);
            await repository.UpgradeRing(uid, new(magic.Id, 0));
            var stored = await repository.Profile(uid, "Anillos");
            Check(EquipmentModifiers.Calculate(stored).MagicRingLevel == 1 && stored.Items.Single(i => i.Id == magic.Id).UpgradeLevel == 1,
                "La mejora del anillo mágico no modifica el efecto.");
            await database.Collection("users").Document(uid).UpdateAsync(new Dictionary<string, object> { ["gold"] = 0L });
            await Reject<ArgumentException>(() => repository.UpgradeRing(uid, new(magic.Id, 1)));
        });
    }
}
