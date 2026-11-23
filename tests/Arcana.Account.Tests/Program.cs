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

if (args.FirstOrDefault(a => a.StartsWith("--journal-lock-probe=")) is { } probe)
{
    try { using var lease = new SharedJournalLease(probe["--journal-lock-probe=".Length..]); Console.WriteLine("LOCK_FREE"); }
    catch (IOException) { Console.WriteLine("LOCK_BUSY"); }
    return;
}
if (args.Contains("--reward-outbox-only")) { await RewardOutboxTests.Run(); return; }
if (args.Contains("--rolling-update-only")) { await RollingUpdateTests.Run(); return; }
if (args.Contains("--defense-rewards-only")) { await DefenseRewardsEmulatorTests.Run(); return; }
await AdMobRewardVerifierTests.Run();
PlayBillingTests.Run();
VerifiedGoogleClaimsTests.Run();
if (args.Contains("--verified-claims-only")) return;

string emulator = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "";
if (!Uri.TryCreate("http://" + emulator, UriKind.Absolute, out var emulatorUri) || !emulatorUri.IsLoopback)
    throw new InvalidOperationException("Estas pruebas requieren FIRESTORE_EMULATOR_HOST apuntando a un emulador local; nunca usan la base real.");
var database = new FirestoreDbBuilder { ProjectId = "demo-arcana-accounts", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
var repository = new FirestoreAccountRepository(database);
await HttpAbuseProtectionTests.Run();
await RepositoryHardeningEmulatorTests.Run(database);
await BestiaryEmulatorTests.Run(database);
await MatchStatisticsEmulatorTests.Run(database);
await PublicDashboardEmulatorTests.Run(database);
string suffix = Guid.NewGuid().ToString("N");
string alice = "alice_" + suffix, bob = "bob_" + suffix;
var options = new AccountOptions("demo-arcana-accounts", "test-only-key", "demo-arcana-accounts.firebaseapp.com", "http://127.0.0.1:28339");
var accounts = new AccountService(options, new TestVerifier(), repository);
var suite = new AccountIntegrationSuite(database, repository, suffix, alice, bob, options, accounts);

await suite.RunInventory();
await suite.RunAccess();

var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls(options.PublicUrl);
Environment.SetEnvironmentVariable("ARCANA_REWARD_OUTBOX", Path.Combine(Path.GetTempPath(), "arcana-account-live-" + suffix));
builder.Services.ConfigureHttpJsonOptions(json =>
{ foreach (var converter in Protocol.Json.Converters) json.SerializerOptions.Converters.Add(converter); });
builder.Logging.ClearProviders(); builder.Services.AddSingleton(accounts); builder.Services.AddArcanaProtection();
builder.Services.AddSingleton(new ServerSettings { Port = 28339, DiscoveryPort = 28340, World = new() { Mode = GameMode.Duel, MaxPlayers = 2, ScoreLimit = 5 } });
builder.Services.AddSingleton<RewardService>();
builder.Services.AddSingleton<MatchStatisticsService>();
builder.Services.AddHostedService(p => p.GetRequiredService<MatchStatisticsService>());
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(p => p.GetRequiredService<GameService>());
builder.Services.AddHostedService(p => p.GetRequiredService<RewardService>());
var app = builder.Build(); app.UseArcanaProtection(); app.UseWebSockets(); app.MapAccounts(); app.MapItemShop(); app.Map("/play", (HttpContext context, GameService game) => game.HandleConnection(context));
await app.StartAsync();
using var http = new HttpClient { BaseAddress = new(options.PublicUrl) };
var aliceLogin = await suite.Login(alice);
var bobLogin = await suite.Login(bob);
await suite.RunHttp(http, aliceLogin, bobLogin);
await suite.RunNetwork(app, aliceLogin, bobLogin);
await ShopEmulatorTests.Run(database, repository, new Uri(options.PublicUrl), suite.Test);
await AdvertisingEmulatorTests.Run(database, repository, new Uri(options.PublicUrl), suite.Test);
await StoreEmulatorTests.Run(database, repository, suite.Test);
await app.StopAsync(); await app.DisposeAsync();

await suite.RunRewards();
Console.WriteLine($"RESULTADO CUENTAS: {suite.Passed} correctas, {suite.Failed} fallidas.");
Environment.ExitCode = suite.Failed == 0 ? 0 : 1;
