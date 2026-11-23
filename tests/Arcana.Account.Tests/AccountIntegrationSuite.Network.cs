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
    public async Task RunNetwork(WebApplication app, LoginPoll aliceLogin, LoginPoll bobLogin)
    {
        await Test("Red: el servidor aplica el equipo guardado y rechaza sesiones duplicadas o inventadas", async () =>
        {
            var item = EquipmentCatalogue.Generate(new(11), tier: ItemTier.Mythic, slot: EquipmentSlot.Weapon, family: EquipmentFamily.Shield);
            await Seed(alice, item); await repository.Equip(alice, new(item.Id, item.Slot));
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync("127.0.0.1:28339", "A", 0, "", accountToken: aliceLogin.AccessToken);
            await b.ConnectAsync("127.0.0.1:28339", "B", 1, "", accountToken: bobLogin.AccessToken);
            await Ready(a, b);
            Check(a.Latest!.Players.Single(p => p.Id == a.PlayerId).Equipment?.Weapon == ItemTier.Mythic, "El equipo no llegó a la partida.");
            a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "self" });
            for (int attempt = 0; attempt < 60 && a.Latest!.Players.Single(p => p.Id == a.PlayerId).ShieldMax <= 420; attempt++)
            {
                a.Send(new() { Type = "input", AimX = 1 }); b.Send(new() { Type = "input", AimX = 1 });
                await Task.Delay(50);
            }
            var player = a.Latest!.Players.Single(p => p.Id == a.PlayerId);
            Check(player.ShieldMax > 420, "El arma no modificó la absorción real.");
            var visible = await b.InspectEquipmentAsync(a.PlayerId);
            var equipped = PublicEquipment.Prepare(await repository.Profile(alice, "Alice"));
            Check(visible.Name == "A" && visible.Equipment.Items.Single(i => i.Slot == EquipmentSlot.Weapon).Tier == ItemTier.Mythic &&
                visible.Equipment.Stats.SequenceEqual(equipped.View.Stats), "Otro jugador no recibe los bonos reales del equipo guardado.");
            Check(!Protocol.Serialize(visible).Contains(alice) && !Protocol.Serialize(visible).Contains(item.Id), "La ficha expone la identidad o los IDs del inventario.");
            await Reject<InvalidOperationException>(() => a.InspectEquipmentAsync(999999));
            await repository.Equip(alice, new("", EquipmentSlot.Weapon));
            await Task.Delay(125);
            var stillActive = await b.InspectEquipmentAsync(a.PlayerId);
            Check(stillActive.Equipment.Stats.SequenceEqual(visible.Equipment.Stats), "Cambiar el inventario alteró la ficha de una ronda en marcha.");
            await repository.Equip(alice, new(item.Id, item.Slot));
            await using var duplicate = new ArenaConnection();
            await Reject<InvalidOperationException>(() => duplicate.ConnectAsync("127.0.0.1:28339", "D", 0, "", accountToken: aliceLogin.AccessToken));
            await using var forged = new ArenaConnection();
            await Reject<InvalidOperationException>(() => forged.ConnectAsync("127.0.0.1:28339", "F", 0, "", accountToken: "invalid-account-session-token-123456"));
        });
        await Test("Bonos públicos: las fichas se limitan a la sala y llegan junto a snapshots sin perderse", async () =>
        {
            var game = app.Services.GetRequiredService<GameService>();
            var one = game.CreateRoom(new() { Name = "Fichas uno", Mode = GameMode.Duel, MaxPlayers = 2 });
            var two = game.CreateRoom(new() { Name = "Fichas dos", Mode = GameMode.Duel, MaxPlayers = 2 });
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync("127.0.0.1:28339", "Uno", 0, "", roomId: one.Room!.Id, accountToken: aliceLogin.AccessToken);
            await b.ConnectAsync("127.0.0.1:28339", "Dos", 0, "", roomId: two.Room!.Id, accountToken: bobLogin.AccessToken);
            await Ready(a, b);
            Check(a.PlayerId == b.PlayerId, "La prueba no tiene IDs coincidentes de salas distintas.");
            long before = a.Latest!.Tick;
            var views = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => a.InspectEquipmentAsync(b.PlayerId)));
            Check(views.All(v => v.Name == "Uno"), "La consulta cruzó a otra sala.");
            await Task.Delay(100);
            Check(a.Connected && a.Latest!.Tick > before, "Las fichas interrumpieron los snapshots de la partida.");
            await Reject<InvalidOperationException>(() => b.InspectEquipmentAsync(99999));
        });
        await Test("PvP completo: fin de partida, diario y recompensa llegan a la cuenta sin bloquear la simulación", async () =>
        {
            var before = await repository.Profile(alice, "Alice");
            await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
            await a.ConnectAsync("127.0.0.1:28339", "A", 0, "", accountToken: aliceLogin.AccessToken);
            await b.ConnectAsync("127.0.0.1:28339", "B", 1, "", accountToken: bobLogin.AccessToken);
            var start = DateTimeOffset.UtcNow;
            while ((DateTimeOffset.UtcNow - start).TotalSeconds < 18)
            {
                a.Send(new() { Type = "input", MoveX = .1f, AimX = 10 });
                b.Send(new() { Type = "input", MoveX = -.1f, AimX = -10 });
                await Task.Delay(250);
            }
            var game = app.Services.GetRequiredService<GameService>();
            var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            lock (gate) rooms["default"].World.Scores[0] = 5; // Fixture de victoria; el servidor resuelve el fin y toda la entrega.
            AccountProfile after = before;
            for (int n = 0; n < 120 && (after.Wins == before.Wins || after.Items.Length == before.Items.Length); n++)
            {
                await Task.Delay(100);
                after = await repository.Profile(alice, "Alice");
            }
            Check(a.Latest!.MatchEnded && after.Wins == before.Wins + 1 && after.Items.Length == before.Items.Length + 1,
                "El fin de la partida no entregó una única recompensa.");
            long tick = game.CurrentTick; await Task.Delay(150); Check(game.CurrentTick > tick, "La persistencia bloqueó el reloj de simulación.");
        });
        await Test("Defensa online: hito autenticado con bots, entrega única y sin victorias PvP", async () =>
        {
            var before = await repository.Profile(alice, "Alice");
            var game = app.Services.GetRequiredService<GameService>();
            var (created, error, _) = game.CreateRoom(new CreateRoomRequest {
                Name = "Defensa autenticada", Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = 1 });
            var room = created ?? throw new InvalidOperationException(error);
            await using var player = new ArenaConnection();
            await player.ConnectAsync("127.0.0.1:28339", "Defensor", 1, "", roomId: room.Id, accountToken: aliceLogin.AccessToken);
            var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            lock (gate)
            {
                var world = rooms[room.Id].World;
                // Fixture de cinco minutos: no se introduce ningún comando de reloj en el protocolo público.
                typeof(GameWorld).GetProperty("Tick")!.SetValue(world, 18010L);
                typeof(GameWorld).GetField("_defenseElapsed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 300.0);
                var progress = (System.Collections.IDictionary)typeof(GameWorld).GetField("_defenders", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
                var data = progress[player.PlayerId]!;
                data.GetType().GetField("ActiveTicks")!.SetValue(data, 18000L);
            }
            AccountProfile after = before;
            for (int n = 0; n < 100 && after.Items.Length == before.Items.Length; n++)
            { await Task.Delay(100); after = await repository.Profile(alice, "Alice"); }
            Check(after.Items.Length == before.Items.Length + 1 && after.Wins == before.Wins && after.Losses == before.Losses,
                "No se entregó el hito o se alteraron estadísticas PvP.");
            await Task.Delay(300);
            Check((await repository.Profile(alice, "Alice")).Items.Length == after.Items.Length, "Se repitió el mismo hito en otro tick.");
            Check(player.Latest!.Defense!.Defenders.Single(p => p.PlayerId == player.PlayerId).Items.Length == 0,
                "El premio permanente entró en el inventario temporal.");
        });
        await Test("Defensa online: salida voluntaria y nueva entrada conservan recursos sin protección gratuita", async () =>
        {
            var game = app.Services.GetRequiredService<GameService>();
            var (created, error, _) = game.CreateRoom(new CreateRoomRequest { Name = "Recursos al volver", Mode = GameMode.Defense,
                Map = ArenaMapId.Bastion, MaxPlayers = 2, Bots = 0 });
            var room = created ?? throw new InvalidOperationException(error);
            await using var ally = new ArenaConnection();
            await ally.ConnectAsync("127.0.0.1:28339", "Aliado", 0, "", roomId: room.Id, accountToken: bobLogin.AccessToken);
            var player = new ArenaConnection();
            await player.ConnectAsync("127.0.0.1:28339", "Defensor", 0, "", roomId: room.Id, accountToken: aliceLogin.AccessToken);
            for (int n = 0; n < 150 && (!player.Ready || !ally.Ready); n++) await Task.Delay(20);
            Check(player.Ready && ally.Ready, "Los jugadores no terminaron la carga inicial.");
            var gate = typeof(GameService).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            var rooms = (Dictionary<string, GameRoom>)typeof(GameService).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
            lock (gate)
            {
                var world = rooms[room.Id].World;
                typeof(GameWorld).GetField("_spawnClock", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, 10000f);
                var body = world.Players[player.PlayerId]; body.Health = 100; body.Energy = 5; body.Invulnerable = 0; body.MagickCooldowns[1] = 10;
                typeof(Wizard).GetProperty("FreezeRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(body, 2.5f);
            }
            int oldId = player.PlayerId; await player.DisposeAsync();
            bool removed = false;
            for (int n = 0; n < 100; n++)
            {
                lock (gate) removed = !rooms[room.Id].World.Players.ContainsKey(oldId);
                if (removed) break;
                await Task.Delay(20);
            }
            Check(removed, "La salida no eliminó la sesión anterior.");
            await using var returned = new ArenaConnection();
            await returned.ConnectAsync("127.0.0.1:28339", "Defensor", 0, "", roomId: room.Id, accountToken: aliceLogin.AccessToken, deferReady: true);
            // La sala sigue avanzando con el aliado mientras el cliente que vuelve carga.
            // El servidor no debe reemplazar el estado restaurado con la protección de un jugador nuevo.
            await Task.Delay(150);
            lock (gate)
            {
                var body = rooms[room.Id].World.Players[returned.PlayerId];
                Check(body.Invulnerable == 0, "La fase de carga volvió a conceder invulnerabilidad al personaje restaurado.");
                float recovery = (float)typeof(Wizard).GetProperty("FreezeRecovery", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(body)!;
                Check(recovery is > 0 and <= 2.5f, "La fase de carga eliminó o reinició la recuperación del frío.");
            }
            Check(returned.CompleteLoading(), "No pudo confirmar la carga al volver.");
            for (int n = 0; n < 150 && !returned.Ready; n++) await Task.Delay(20);
            Check(returned.Ready, "El personaje restaurado no terminó de cargar.");
            lock (gate)
            {
                var body = rooms[room.Id].World.Players[returned.PlayerId];
                Check(body.Id != oldId && body.Health is > 0 and <= 100 && body.Energy < 25 && body.MagickCooldowns[1] > 8 &&
                    body.Invulnerable == 0 && body.Deaths == 0,
                    $"La nueva entrada cambió recursos: vida {body.Health}, energía {body.Energy}, recarga {body.MagickCooldowns[1]}, invulnerable {body.Invulnerable}, muertes {body.Deaths}.");
            }
        });
    }
}
