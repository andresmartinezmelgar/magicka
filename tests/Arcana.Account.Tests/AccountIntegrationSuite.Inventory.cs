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
    public async Task RunInventory()
    {
        await Test("Firestore: misma UID conserva la cuenta y las lecturas tienen un esquema estable", async () =>
        {
            var first = await repository.Profile(alice, "Alice"); var second = await repository.Profile(alice, "Otro nombre");
            _ = await repository.Profile(bob, "Bob");
            Check(first.Uid == second.Uid && first.Gold == 0 && second.Name == "Alice" && second.Items.Length == 0 && second.SchemaVersion == 1, "Se recreó o alteró una cuenta.");
        });
        await Test("Firestore: premio PvP y resultado se entregan una vez, incluso con tres reintentos concurrentes", async () =>
        {
            var match = Match(); await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => repository.Award(match)));
            var profile = await repository.Profile(alice, "Alice"); var loser = await repository.Profile(bob, "Bob");
            Check(profile.Wins == 1 && profile.Items.Length == 1 && loser.Losses == 1 && loser.Items.Length == 0, "Premio repetido o entregado al perdedor.");
            Check(profile.Items[0].SourceMatchId == match.Id && EquipmentCatalogue.Valid(profile.Items[0]), "No se conservaron los valores sorteados.");
            Check((await database.Collection("matches").Document(match.Id).GetSnapshotAsync()).Exists, "Falta el registro del resultado.");
        });
        await Test("Firestore: guardar cuatro posiciones es atómico y rechaza objetos ajenos o mal colocados", async () =>
        {
            string user = "bulk_loadout_" + suffix;
            await repository.Profile(user, "Equipo completo");
            var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(80 + (int)s), slot: s)).ToArray();
            foreach (var item in items) await Seed(user, item);
            var loadout = new EquipmentLoadout(items[0].Id, items[1].Id, items[2].Id, items[3].Id);
            await repository.SaveLoadout(user, loadout);
            Check((await repository.Profile(user, "Equipo completo")).Loadout == loadout, "Se perdió una posición en el guardado conjunto.");
            await Reject<ArgumentException>(() => repository.SaveLoadout(user, loadout with { Hat = loadout.Chest }));
            await Reject<ArgumentException>(() => repository.SaveLoadout(bob, loadout));
            Check((await repository.Profile(user, "Equipo completo")).Loadout == loadout, "Una selección inválida guardó parte de la configuración.");
            await repository.SaveLoadout(user, new EquipmentLoadout());
            Check((await repository.Profile(user, "Equipo completo")).Loadout == new EquipmentLoadout(), "No se pueden quitar las cuatro piezas juntas.");
        });
        await Test("Firestore: mínimo, máximo y valor actual sobreviven a una nueva instancia del repositorio", async () =>
        {
            var original = (await repository.Profile(alice, "Alice")).Items[0];
            var restored = (await new FirestoreAccountRepository(database).Profile(alice, "Alice")).Items.Single(i => i.Id == original.Id);
            Check(restored.Bonuses.SequenceEqual(original.Bonuses) && restored.GenerationVersion == original.GenerationVersion, "El objeto se volvió a sortear.");
        });
        await Test("Firestore: equipar exige propiedad y posición correctas", async () =>
        {
            var item = (await repository.Profile(alice, "Alice")).Items[0];
            await Reject<ArgumentException>(() => repository.Equip(bob, new(item.Id, item.Slot)));
            await Reject<ArgumentException>(() => repository.Equip(alice, new(item.Id, (EquipmentSlot)(((int)item.Slot + 1) % 4))));
            await repository.Equip(alice, new(item.Id, item.Slot));
            Check((await repository.Profile(alice, "Alice")).Loadout.Get(item.Slot) == item.Id, "No se guardó el equipo.");
        });
        await Test("Firestore: equipada no se vende; tras desequipar una venta simultánea paga una vez con oro de 64 bits", async () =>
        {
            var item = (await repository.Profile(alice, "Alice")).Items[0];
            long original = 9007199254740993L;
            await database.Collection("users").Document(alice).UpdateAsync("gold", original);
            await Reject<ArgumentException>(() => repository.Sell(alice, item.Id));
            Check((await repository.Profile(alice, "Alice")).Gold == original &&
                !(await database.Collection("users").Document(alice).Collection("sales").Document(item.Id).GetSnapshotAsync()).Exists,
                "La venta bloqueada pagó oro o creó un recibo.");
            await repository.Equip(alice, new("", item.Slot));
            var payments = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.Sell(alice, item.Id)));
            var profile = await repository.Profile(alice, "Alice");
            Check(payments.Sum() == item.SaleGold && profile.Gold == original + item.SaleGold && profile.Items.Length == 0 && profile.Loadout.Get(item.Slot) == "", "Saldo duplicado, impreciso o equipo colgante.");
        });
        await Test("Firestore: vender y equipar al mismo tiempo no deja referencias a objetos eliminados", async () =>
        {
            var item = EquipmentCatalogue.Generate(new(9)); await Seed(alice, item);
            async Task Equip() { try { await repository.Equip(alice, new(item.Id, item.Slot)); } catch (ArgumentException) { } }
            async Task Sell() { try { await repository.Sell(alice, item.Id); } catch (ArgumentException) { } }
            await Task.WhenAll(Equip(), Sell());
            var profile = await repository.Profile(alice, "Alice");
            Check(profile.Items.Any(i => i.Id == item.Id) ? profile.Loadout.Get(item.Slot) == item.Id : profile.Loadout.Get(item.Slot) != item.Id,
                "La carrera vendió una equipada o dejó una referencia eliminada.");
        });

        await Test("Firestore: oro PvP y afijos se abonan una vez al cerrar, también con reintentos concurrentes", async () =>
        {
            string winUid = "gold_win_" + suffix, loseUid = "gold_lose_" + suffix;
            var beforeWin = await repository.Profile(winUid, "Gana");
            var beforeLose = await repository.Profile(loseUid, "Pierde");
            var win = new MatchParticipant(winUid, 1, 0, 30, true) { ExtraGoldEvery = 11, GoldBonusPercent = 10 };
            var lose = new MatchParticipant(loseUid, 2, 1, 30, false) { GoldBonusPercent = 20 };
            var match = new RewardMatch(Guid.NewGuid().ToString("N"), "test-server", GameMode.Duel, ArenaMapId.Classic,
                DateTimeOffset.UtcNow, [win, lose], RulesVersion: 4);
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.Award(match)));
            var afterWin = await repository.Profile(winUid, "Gana");
            var afterLose = await repository.Profile(loseUid, "Pierde");
            Check(afterWin.Gold == beforeWin.Gold + 59 && afterLose.Gold == beforeLose.Gold + 30 &&
                afterWin.Wins == beforeWin.Wins + 1 && afterLose.Losses == beforeLose.Losses + 1,
                "Se duplicó el pago o no se aplicó el bono al premio final.");
            Check((await database.Collection("users").Document(winUid).Collection("rewards").Document(match.Id).GetSnapshotAsync()).GetValue<long>("gold") == 59,
                "El recibo no conserva el pago exacto.");
        });
        await Test("Firestore: práctica, IA y cuenta repetida no conceden objetos ni victorias", async () =>
        {
            var before = await repository.Profile(alice, "Alice"); var match = Match();
            await repository.Award(match with { Map = ArenaMapId.Practice });
            await repository.Award(match with { Participants = [match.Participants[0]] });
            await repository.Award(match with { Participants = [match.Participants[0], match.Participants[1] with { Uid = alice }] });
            var after = await repository.Profile(alice, "Alice");
            Check(after.Wins == before.Wins && after.Items.Length == before.Items.Length, "Se concedió una recompensa inválida.");
        });
        await Test("Firestore: las cuatro piezas elegidas sobreviven a otro repositorio, nueva sesión y cambio de versión", async () =>
        {
            string user = "loadout_" + suffix;
            _ = await repository.Profile(user, "Equipo persistente");
            var items = Enum.GetValues<EquipmentSlot>().Select(s => EquipmentCatalogue.Generate(new(30 + (int)s),
                tier: ItemTier.Mythic, slot: s, family: EquipmentFamily.Fire)).ToArray();
            foreach (var item in items) { await Seed(user, item); await repository.Equip(user, new(item.Id, item.Slot)); }
            var original = await repository.Profile(user, "Equipo persistente");
            var restored = await new FirestoreAccountRepository(database).Profile(user, "Otra versión del cliente");
            Check(restored.Loadout == original.Loadout && restored.SchemaVersion == original.SchemaVersion &&
                restored.Items.All(i => i.Bonuses.SequenceEqual(items.Single(old => old.Id == i.Id).Bonuses) && i.Affix == items.Single(old => old.Id == i.Id).Affix), "La nueva instancia perdió las piezas o sus valores.");
            var login = await Login(user); Check(login.Profile!.Loadout == original.Loadout, "Una nueva sesión eliminó el equipo guardado.");
            await repository.Equip(user, new("", EquipmentSlot.Hat));
            restored = await new FirestoreAccountRepository(database).Profile(user, "Equipo persistente");
            Check(restored.Loadout.Hat == "" && restored.Loadout.Boots == original.Loadout.Boots && restored.Loadout.Chest == original.Loadout.Chest &&
                restored.Loadout.Weapon == original.Loadout.Weapon, "Quitar un gorro alteró otra posición.");
            accounts.Logout(login.AccessToken);
        });
        await Test("Firestore: ocho familias nuevas persisten y el catálogo antiguo conserva sus definiciones", async () =>
        {
            var legacyDefinition = database.Collection("catalogues").Document("1").Collection("bonusDefinitions").Document("firePower");
            await legacyDefinition.SetAsync(new Dictionary<string, object> { ["generationVersion"] = 1, ["legacyMarker"] = suffix, ["maximum"] = 0.14 });
            var updated = new FirestoreAccountRepository(database);
            foreach (var family in EquipmentCatalogue.FocusedFamilies)
            {
                string user = "element_" + family + "_" + suffix;
                _ = await updated.Profile(user, family.ToString());
                var items = Enum.GetValues<EquipmentSlot>().Select(slot => EquipmentCatalogue.Generate(new(100 + (int)slot),
                    tier: ItemTier.Mythic, slot: slot, family: family)).ToArray();
                foreach (var item in items) { await Seed(user, item); await updated.Equip(user, new(item.Id, item.Slot)); }
                var original = await updated.Profile(user, family.ToString());
                var restored = await new FirestoreAccountRepository(database).Profile(user, family.ToString());
                Check(restored.Loadout == original.Loadout && restored.SchemaVersion == 1 && EquipmentModifiers.Calculate(restored).Pieces(family) == 4,
                    "Se perdió el conjunto de " + family);
                Check(restored.Items.All(i => i.GenerationVersion == EquipmentCatalogue.GenerationVersion && i.Bonuses.SequenceEqual(items.Single(old => old.Id == i.Id).Bonuses)),
                    "Se volvió a sortear una pieza de " + family);
            }
            var old = await legacyDefinition.GetSnapshotAsync();
            Check(old.GetValue<string>("legacyMarker") == suffix && old.GetValue<long>("generationVersion") == 1, "Se sobrescribió el catálogo de la versión anterior.");
            var definitions = await database.Collection("catalogues").Document(EquipmentCatalogue.GenerationVersion.ToString()).Collection("bonusDefinitions").GetSnapshotAsync();
            Check(definitions.Count == 22 && definitions.Documents.All(d => d.GetValue<long>("generationVersion") == EquipmentCatalogue.GenerationVersion), "No se archivaron los atributos nuevos.");
        });
    }
}
