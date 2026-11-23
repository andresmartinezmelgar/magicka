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

internal sealed partial class AccountIntegrationSuite(FirestoreDb database, FirestoreAccountRepository repository,
    string suffix, string alice, string bob, AccountOptions options, AccountService accounts)
{
    private int passed, failed;
    public int Passed => passed;
    public int Failed => failed;

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static async Task Ready(params ArenaConnection[] connections)
    {
        for (int attempt = 0; attempt < 60 && connections.Any(c => !c.Ready); attempt++)
        {
            foreach (var connection in connections) connection.Send(new() { Type = "input", AimX = 1 });
            await Task.Delay(50);
        }
        Check(connections.All(c => c.Ready), "El servidor no confirmó la preparación de los jugadores.");
    }

    public async Task Test(string name, Func<Task> action)
    {
        try { await action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static async Task Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); throw new Exception("Se aceptó una operación inválida."); } catch (T) { } }

    private RewardMatch Match(bool winner = true) => new(Guid.NewGuid().ToString("N"), "test-server", GameMode.Duel, ArenaMapId.Classic, DateTimeOffset.UtcNow,
        [new(alice, 1, 0, 20, winner), new(bob, 2, 1, 20, !winner)]);

    private async Task Seed(string uid, EquipmentItem item)
    {
        var user = database.Collection("users").Document(uid);
        await database.RunTransactionAsync(async tx =>
        {
            var doc = await tx.GetSnapshotAsync(user); var header = FirestoreData.Read<AccountHeader>(doc);
            tx.Create(user.Collection("items").Document(item.Id), FirestoreData.Map(item));
            tx.Update(user, new Dictionary<string, object> { ["itemCount"] = header.ItemCount + 1 });
        });
    }

    public async Task<LoginPoll> Login(string uid)
    {
        var challenge = accounts.StartLogin(); string browser = new Uri(challenge.Url).Query[5..];
        await accounts.CompleteLogin(challenge.Id, new("verified:" + uid, browser), CancellationToken.None);
        return await accounts.Poll(new(challenge.Id, challenge.PollSecret), CancellationToken.None);
    }
}
