using Arcana.Core;

if (args.Contains("--profile-survival-generation"))
{
    SurvivalMapGenerationBenchmark.Run(args.LastOrDefault(a => a.StartsWith("--profile-output="))?[17..]
        ?? "artifacts/profiles/survival-map-generation.json");
    return 0;
}
if (args.Contains("--profile-maps"))
{
    MapPerformanceBenchmarks.Run(args.LastOrDefault(a => a.StartsWith("--profile-output="))?[17..] ?? "artifacts/profiles/map-simulation.json");
    return 0;
}
if (args.Contains("--profile-protocol"))
{
    ProtocolPerformanceBenchmarks.Run(args.LastOrDefault(a => a.StartsWith("--profile-output="))?[17..] ?? "artifacts/profiles/protocol.json");
    return 0;
}

int passed = 0, failed = 0;

void Test(string name, Action action)
{
    if (args.Contains("--only-network")) return;
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
async Task AsyncTest(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
if (args.Contains("--defense-balance") || args.Contains("--defense-balance-report-only"))
{
    DefenseBalanceTests.Profile(args.FirstOrDefault(a => a.StartsWith("--profile-output="))?[17..] ?? "artifacts/defense/balance.json", args.Contains("--defense-balance"));
    return 0;
}
if (args.Contains("--defense-benchmark"))
{
    StreamingPerformanceBenchmarks.Run();
    DefenseTests.Benchmark(args.FirstOrDefault(a => a.StartsWith("--profile-output="))?[17..] ?? "artifacts/defense/benchmark.json");
    return 0;
}
if (args.Contains("--defense-only"))
{
    DefenseTests.Run(Test);
    SurvivalEconomyTests.Run(Test);
    DefenseBotEquipmentTests.Run(Test);
    SurvivalBeamTests.Run(Test);
    DefenseScalingTests.Run(Test);
    DefenseBotPortalTests.Run(Test);
    SurvivalAuraAndOracleTests.Run(Test);
    Console.WriteLine($"RESULTADO DEFENSA: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--desktop-session-only"))
{
    DesktopSessionStoreTests.Run(Test);
    Console.WriteLine($"RESULTADO SESION WINDOWS: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--statistics-only")) { MatchStatisticsTests.Run(Test); PersonalDamageTests.Run(Test); DashboardTelemetryTests.Run(Test); ElementSelectionStatisticsTests.Run(Test); Console.WriteLine($"RESULTADO ESTADÍSTICAS: {passed} correctas, {failed} fallidas."); return failed == 0 ? 0 : 1; }
if (args.Contains("--audit-only")) { ComprehensiveAuditTests.Run(Test); Console.WriteLine($"RESULTADO REVISIÓN: {passed} correctas, {failed} fallidas."); return failed == 0 ? 0 : 1; }
if (args.Contains("--survival-continuity-only")) { SurvivalContinuityTests.Run(Test); Console.WriteLine($"RESULTADO CONTINUIDAD: {passed} correctas, {failed} fallidas."); return failed == 0 ? 0 : 1; }
var deltaFixtures = args.FirstOrDefault(a => a.StartsWith("--delta-fixtures="));
if (deltaFixtures != null)
{
    BinaryUpdateDeltaTests.Interoperability(deltaFixtures["--delta-fixtures=".Length..], Test);
    Console.WriteLine($"RESULTADO PARCHE APK: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}

BaselineEndpointTests.RunPublicAccount(Test);

AdaptiveSpellQualityTests.Run(Test);
AdaptiveAudioTests.Run(Test);
ElementTextTests.Run(Test);

BaselineEndpointTests.RunLocalHosting(Test);

DesktopSessionStoreTests.Run(Test);
ReleaseDownloadVerifierTests.Run(Test);
ReleaseAssetDownloaderTests.Run(Test);
ReleaseResumeTests.Run(Test);
ReleaseUpdatePlanTests.Run(Test);
ReleaseUpdateChannelTests.Run(Test);
ReleaseUpdateQueryTests.Run(Test);
BinaryUpdateDeltaTests.Run(Test);
MatchSeriesTests.Run(Test);
ResponsiveLayoutTests.Run(Test);
NetworkEndpointTests.Run(Test);
UpgradeTests.Run(Test);
NetworkDeltaTests.Run(Test);
PacketDecodingTests.Run(Test);
NetworkTrafficTests.Run(Test);
MeteorTargetingTests.Run(Test);
FrontlineTests.Run(Test);
DefenseTests.Run(Test);
SurvivalEconomyTests.Run(Test);
GameplayRegressionTests.Run(Test);
MovementCollisionTests.Run(Test);
PracticeTests.Run(Test);
PerformanceRegressionTests.Run(Test);
LoadingTests.Run(Test);
ElementInteractionTests.Run(Test);
DefenseBotEquipmentTests.Run(Test);
SurvivalBeamTests.Run(Test);
EquipmentTests.Run(Test);
GoldEquipmentTests.Run(Test);
RingTests.Run(Test);
BestiaryAndAffixTests.Run(Test);
BestiaryStatsTests.Run(Test);
DefenseThreatTests.Run(Test);
DefenseEliteTests.Run(Test);
DefenseScalingTests.Run(Test);
DefenseBotPortalTests.Run(Test);
SurvivalAuraAndOracleTests.Run(Test);
MatchStatisticsTests.Run(Test);
AuditHardeningTests.Run(Test);
DashboardTelemetryTests.Run(Test);
ElementSelectionStatisticsTests.Run(Test);
PersonalDamageTests.Run(Test);
SurvivalSaveTests.Run(Test);
SurvivalContinuityTests.Run(Test);
AccountLoadoutTests.Run(Test);
UsabilityTests.Run(Test);
AccountSessionTests.Run(Test);
AccountAccessTests.Run(Test);
AuditRegressionTests.Run(Test);
ReleaseAuditFixTests.Run(Test);
ComprehensiveAuditTests.Run(Test);
ShopRulesTests.Run(Test);
AccountShopPreloadTests.Run(Test);
PublicEquipmentTests.Run(Test);

BaselineSpellTests.RunBasics(Test);

BaselineSpellTests.RunAffinities(Test);
BaselineMatchTests.RunModes(Test);
BaselineCitadelTests.RunGeometry(Test);

BaselineAscensionTests.Run(Test);

BaselineRelicTests.Run(Test);

BaselineCitadelTests.RunCaptureAndMovement(Test);
BaselineInputTests.RunTimeoutAndBots(Test);
BaselineSpellTests.RunFieldsAndBeams(Test);
BaselineMatchTests.RunCaptureReplication(Test);
BaselineSpellCatalogueTests.Run(Test);
BaselineInputTests.RunReplicationAndCancellation(Test);
BaselineSpellTests.RunImpactRegressions(Test);
BaselineMatchTests.RunSpawnRegressions(Test);

BaselineProtocolTests.RunAddresses(Test);

BaselineProtocolTests.RunValidation(Test);
if (!args.Contains("--only-network")) await BaselineTransportTests.Run(AsyncTest);

string? server = args.FirstOrDefault(a => a.StartsWith("--server="))?[9..];
int discoveryPort = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--discovery-port="))?[17..], out int port) ? port : Protocol.DiscoveryPort;

if (server != null && args.Contains("--series-lobby-only"))
{
    await BaselineLobbyTests.Run(server, AsyncTest);
    Console.WriteLine($"RESULTADO LOBBY: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}

if (server != null) await BaselineNetworkTests.Run(server, discoveryPort, args.Contains("--skip-discovery"), AsyncTest);
string? package = args.FirstOrDefault(a => a.StartsWith("--package="))?[10..];
await BaselinePackageTests.Run(package, AsyncTest);
Console.WriteLine($"RESULTADO: {passed} correctas, {failed} fallidas.");
return failed == 0 ? 0 : 1;
