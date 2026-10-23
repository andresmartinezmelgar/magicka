using Arcana.Core;
using System.Text.Json;

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
void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
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
    Console.WriteLine($"RESULTADO DEFENSA: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--desktop-session-only"))
{
    DesktopSessionStoreTests.Run(Test);
    Console.WriteLine($"RESULTADO SESION WINDOWS: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}
if (args.Contains("--statistics-only")) { MatchStatisticsTests.Run(Test); Console.WriteLine($"RESULTADO ESTADÍSTICAS: {passed} correctas, {failed} fallidas."); return failed == 0 ? 0 : 1; }
var deltaFixtures = args.FirstOrDefault(a => a.StartsWith("--delta-fixtures="));
if (deltaFixtures != null)
{
    BinaryUpdateDeltaTests.Interoperability(deltaFixtures["--delta-fixtures=".Length..], Test);
    Console.WriteLine($"RESULTADO PARCHE APK: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}

Test("Public account server: valid HTTPS tunnel and expiry", () =>
{
    var now = DateTimeOffset.UtcNow;
    var json = JsonSerializer.Serialize(new { fields = new { url = new { stringValue = "wss://example.trycloudflare.com/play" }, expiresAt = new { timestampValue = now.AddMinutes(2).ToString("O") }, protocol = new { integerValue = Protocol.Version.ToString() } } });
    Check(PublicAccountServer.Parse(json, now) == "wss://example.trycloudflare.com/play", "Valid endpoint rejected.");
});
Test("Public account server: reject unsafe URLs, stale leases and protocols", () =>
{
    var now = DateTimeOffset.UtcNow;
    string Json(string url, DateTimeOffset expiry, int protocol) => JsonSerializer.Serialize(new { fields = new { url = new { stringValue = url }, expiresAt = new { timestampValue = expiry.ToString("O") }, protocol = new { integerValue = protocol.ToString() } } });
    foreach (string url in new[] { "ws://example.trycloudflare.com/play", "wss://evil.example/play", "wss://example.trycloudflare.com.evil.example/play", "wss://user:secret@example.trycloudflare.com/play", "wss://example.trycloudflare.com/play?token=secret", "wss://example.trycloudflare.com:8443/play", "wss://example.trycloudflare.com/other" })
    {
        bool rejected = false;
        try { PublicAccountServer.Parse(Json(url, now.AddMinutes(2), Protocol.Version), now); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Unsafe endpoint accepted: " + url);
    }
    foreach (var entry in new[] { (now.AddSeconds(-1), Protocol.Version), (now.AddMinutes(4), Protocol.Version), (now.AddMinutes(2), Protocol.Version + 1) })
    {
        bool rejected = false;
        try { PublicAccountServer.Parse(Json("wss://example.trycloudflare.com/play", entry.Item1, entry.Item2), now); } catch (IOException) { rejected = true; }
        Check(rejected, "Stale or incompatible announcement accepted.");
    }
});

(GameWorld World, Wizard A, Wizard B) Setup(GameMode mode = GameMode.Arena, bool friendly = true)
{
    var world = new GameWorld(new() { Mode = mode, FriendlyFire = friendly });
    var a = world.AddPlayer("Azul", 0)!; var b = world.AddPlayer("Rojo", 1)!;
    a.Position = new(-2, 0); b.Position = new(2, 0); a.Invulnerable = b.Invulnerable = 0;
    return (world, a, b);
}
void Send(GameWorld w, Wizard p, string type, Element element = Element.Water, V2? aim = null, int slot = 0)
{
    var at = aim ?? new V2(3, 0);
    w.ApplyMessage(p.Id, new() { Type = type, Sequence = p.LastSequence + 1, Element = element, Slot = slot, AimX = at.X, AimY = at.Y });
}
void Run(GameWorld w, Wizard p, int ticks)
{
    for (int i = 0; i < ticks; i++) { Send(w, p, "input"); w.Step(); }
}
void Select(GameWorld w, Wizard p, params Element[] elements)
{ foreach (var element in elements) Send(w, p, "element", element); }

Test("Firebase en alojamiento local requiere activación explícita y ficheros privados", () =>
{
    string root = Path.Combine(Path.GetTempPath(), "arcana-firebase-local-" + Guid.NewGuid().ToString("N"));
    try
    {
        var start = new System.Diagnostics.ProcessStartInfo("Arcana.Server");
        Check(!LocalFirebaseHosting.Configure(start, root), "Firebase se activó sin configuración local.");
        Check(!start.Environment.ContainsKey("GOOGLE_APPLICATION_CREDENTIALS"), "Se usó una credencial sin autorización local.");
        string directory = LocalFirebaseHosting.DirectoryPath(root);
        Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, LocalFirebaseHosting.ConfigFileName);
        string credential = Path.Combine(directory, LocalFirebaseHosting.CredentialFileName);
        File.WriteAllText(config, "{}");
        Check(LocalFirebaseHosting.PublicGameAddress(root) == null, "Un JSON sin URL produjo una dirección de cuenta.");
        bool missing = false;
        try { LocalFirebaseHosting.Configure(start, root); }
        catch (InvalidOperationException) { missing = true; }
        Check(missing, "Firebase se activó sin credencial de servicio.");
        File.WriteAllText(config, "{\"publicUrl\":\"http://localhost:29015\"}");
        Check(LocalFirebaseHosting.PublicGameAddress(root) == "ws://localhost:29015/play",
            "La dirección local no coincide con el origen de Google Login.");
        File.WriteAllText(config, "{\"publicUrl\":\"https://demo.trycloudflare.com\"}");
        Check(LocalFirebaseHosting.PublicGameAddress(root) == "wss://demo.trycloudflare.com/play",
            "La URL HTTPS no se transformó en conexión WSS.");
        File.WriteAllText(credential, "{}");
        Check(LocalFirebaseHosting.Configure(start, root), "No se activó con ambas configuraciones.");
        Check(start.Environment["ARCANA_FIREBASE_CONFIG"] == config, "Ruta pública incorrecta.");
        Check(start.Environment["GOOGLE_APPLICATION_CREDENTIALS"] == credential, "Ruta privada incorrecta.");
        Check(start.Environment["ARCANA_REWARD_OUTBOX"] == Path.Combine(directory, "reward-outbox"),
            "El diario de recompensas no se guardará entre partidas.");
        Check(!File.Exists(Path.Combine(root, "server.local.json")), "Se copió la configuración fuera de la carpeta privada.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

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
FrontlineTests.Run(Test);
DefenseTests.Run(Test);
GameplayRegressionTests.Run(Test);
MovementCollisionTests.Run(Test);
PracticeTests.Run(Test);
PerformanceRegressionTests.Run(Test);
LoadingTests.Run(Test);
ElementInteractionTests.Run(Test);
EquipmentTests.Run(Test);
GoldEquipmentTests.Run(Test);
RingTests.Run(Test);
BestiaryAndAffixTests.Run(Test);
BestiaryStatsTests.Run(Test);
DefenseThreatTests.Run(Test);
DefenseEliteTests.Run(Test);
MatchStatisticsTests.Run(Test);
SurvivalSaveTests.Run(Test);
AccountLoadoutTests.Run(Test);
UsabilityTests.Run(Test);
AccountSessionTests.Run(Test);
AuditRegressionTests.Run(Test);
ShopRulesTests.Run(Test);
PublicEquipmentTests.Run(Test);

Test("Límite de tres elementos y cancelación de incompatibles", () =>
{
    var buffer = new ElementBuffer();
    buffer.Add(Element.Fire); buffer.Add(Element.Fire); buffer.Add(Element.Fire);
    Check(!buffer.Add(Element.Earth) && buffer.Elements.Count == 3, "Se admitió un cuarto elemento.");
    buffer.Add(Element.Cold);
    Check(buffer.Elements.Count == 2 && buffer.Elements.All(e => e == Element.Fire), "Frío no anuló un fuego.");
    buffer.Clear(); buffer.Add(Element.Water); buffer.Add(Element.Lightning);
    Check(buffer.Elements.Count == 0, "Agua y rayo no se anularon.");
    buffer.Add(Element.Shield); buffer.Add(Element.Shield);
    Check(buffer.Elements.Count == 0, "Dos escudos no se anularon.");
});
Test("Vapor, hielo y prioridad de forma del hechizo", () =>
{
    Check(new SpellRecipe([Element.Water, Element.Fire]).Steam, "No se resolvió vapor.");
    Check(new SpellRecipe([Element.Water, Element.Cold]).Shape == SpellShape.Shards, "No se resolvió hielo.");
    Check(new SpellRecipe([Element.Earth, Element.Fire, Element.Arcane]).Shape == SpellShape.Rock, "Tierra perdió prioridad.");
    Check(new SpellRecipe([Element.Shield, Element.Fire]).Shape == SpellShape.Barrier, "Escudo perdió prioridad.");
});
Test("Toda selección de hasta tres teclas resuelve y serializa", () =>
{
    foreach (var a in Enum.GetValues<Element>())
    foreach (var b in Enum.GetValues<Element>())
    foreach (var c in Enum.GetValues<Element>())
    {
        var buffer = new ElementBuffer(); buffer.Add(a); buffer.Add(b); buffer.Add(c);
        var recipe = new SpellRecipe(buffer.Elements);
        Check(Enum.IsDefined(recipe.Shape) && recipe.Elements.Length <= 3, "Combinación sin resolver.");
    }
});
Test("Snapshot JSON sin recursión y copia independiente del estado", () =>
{
    var (w, a, _) = Setup();
    var snapshot = w.Snapshot();
    var json = Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot });
    var copy = Protocol.Deserialize<ServerMessage>(json)!.Snapshot!;
    a.Health = 400; a.Position = new(10, 10);
    Check(copy.Players[0].Health == 1000 && snapshot.Players[0].Position.X == -2, "La copia se modificó con el mundo.");
    Check(!json.Contains("normalized"), "Se serializaron propiedades calculadas del vector.");
});
Test("El servidor rechaza NaN y secuencias repetidas", () =>
{
    var (w, a, _) = Setup();
    Check(!w.ApplyMessage(a.Id, new() { Type = "input", Sequence = 1, AimX = float.NaN }), "Se aceptó una coordenada no finita.");
    Check(w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "No se aceptó una intención válida.");
    Check(!w.ApplyMessage(a.Id, new() { Type = "element", Sequence = 2, Element = Element.Fire }), "Se repitió una intención.");
});
Test("El rayo arcano aplica daño; al soltar deja de aplicarlo", () =>
{
    var (w, a, b) = Setup();
    Send(w, a, "input"); w.Step(); Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane);
    Send(w, a, "cast"); Run(w, a, 30);
    Check(Math.Abs(b.Health - 921.5125f) < 0.05f, "Daño continuo inesperado durante los primeros 0,5 s de carga.");
    Send(w, a, "release"); float health = b.Health; Run(w, a, 20);
    Check(Math.Abs(health - b.Health) < 0.01, "El hechizo siguió tras soltarlo.");
});
Test("Agua y frío congelan; fuego propio seca al mago mojado", () =>
{
    var (w, a, b) = Setup();
    Select(w, a, Element.Water); Send(w, a, "cast"); Run(w, a, 3); Send(w, a, "release");
    Check(b.Wet > 0, "El agua no mojó al objetivo.");
    Select(w, a, Element.Cold, Element.Cold, Element.Cold); Send(w, a, "cast"); Run(w, a, 45);
    Check(b.Frozen > 0, "El objetivo mojado no se congeló.");
    Send(w, a, "release"); a.Wet = 5; a.Burning = 0; float hp = a.Health;
    Select(w, a, Element.Fire); Send(w, a, "self");
    Check(a.Wet == 0 && a.Health == hp, "Secarse provocó daño o no eliminó mojado.");
});
Test("Curación propia, escudo y guarda elemental", () =>
{
    var (w, a, _) = Setup(); a.Health = 500;
    Select(w, a, Element.Life, Element.Life); Send(w, a, "self");
    Check(a.Health > 700, "No se curó al mago."); Run(w, a, 30);
    Select(w, a, Element.Shield, Element.Fire); Send(w, a, "self");
    Check(a.Shield > 0 && a.Ward.Contains(Element.Fire), "No se creó la guarda.");
});

Test("Escudo: capacidad real, absorción, duración, expiración y cambio de guarda", () =>
{
    var (w, a, b) = Setup(); Select(w, a, Element.Shield); Send(w, a, "self");
    var state = w.Snapshot().Players.First(p => p.Id == a.Id);
    Check(state.Shield == 420 && state.ShieldMax == 420 && state.ShieldSeconds == 12, "El escudo normal no empieza al 100%.");
    Send(w, b, "input", aim: a.Position); w.Step(); Select(w, b, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, b, "cast");
    for (int i = 0; i < 35; i++) { Send(w, b, "input", aim: a.Position); w.Step(); }
    state = w.Snapshot().Players.First(p => p.Id == a.Id);
    Check(state.Shield is > 0 and < 420 && state.ShieldMax == 420 && a.Health == 1000 && state.ShieldSeconds < 12, "El daño no consumió primero el escudo manteniendo su capacidad.");
    var roundTrip = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
    Check(roundTrip.Players[0].Shield == state.Shield && roundTrip.Players[0].ShieldMax == 420 && roundTrip.Players[0].ShieldSeconds == state.ShieldSeconds, "Se perdieron los valores del escudo al enviarlos.");
    Send(w, b, "release"); for (int i = 0; i < 725; i++) w.Step();
    state = w.Snapshot().Players[0]; Check(state.Shield == 0 && state.ShieldMax == 0 && state.ShieldSeconds == 0, "Quedó un escudo activo tras expirar.");
    Select(w, a, Element.Shield, Element.Fire); Send(w, a, "self");
    state = w.Snapshot().Players[0]; Check(state.Shield == 180 && state.ShieldMax == 180, "La guarda elemental usa una escala incorrecta.");
    for (int i = 0; i < 30; i++) w.Step(); Select(w, a, Element.Shield, Element.Earth); Send(w, a, "self");
    state = w.Snapshot().Players[0]; Check(state.Shield == 650 && state.ShieldMax == 650 && state.ShieldSeconds == 12, "Reaplicar tierra no actualizó la capacidad.");
    SnapshotValidation.Check(w.Snapshot());
});
Test("La roca cargada se lanza al soltar y colisiona con el enemigo", () =>
{
    var (w, a, b) = Setup(); Send(w, a, "input"); w.Step();
    Select(w, a, Element.Earth, Element.Earth, Element.Earth); Send(w, a, "cast"); Run(w, a, 35);
    Check(w.Snapshot().Projectiles.Length == 0 && a.ChargeTime > 0.5f, "Se lanzó antes de soltar.");
    Send(w, a, "release"); Run(w, a, 20);
    Check(b.Health < 650, "La roca atravesó al objetivo sin aplicar daño.");
});

float NovaDamage(WizardType attacker, WizardType defender, params Element[] elements)
{
    var world = new GameWorld(new() { Mode = GameMode.Arena });
    var a = world.AddPlayer("Atacante", wizardType: attacker)!; var b = world.AddPlayer("Defensor", wizardType: defender)!;
    a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
    Select(world, a, elements); Send(world, a, "nova"); return 1000 - b.Health;
}
Test("Afinidades: bonus y debilidad por elemento para todos los tipos", () =>
{
    foreach (var type in Enum.GetValues<WizardType>().Where(t => t != WizardType.Balanced))
    {
        var element = WizardTypes.Affinity(type)!.Value; var opposite = WizardTypes.Weakness(type)!.Value;
        float normal = NovaDamage(WizardType.Balanced, WizardType.Balanced, element);
        Check(Math.Abs(NovaDamage(type, WizardType.Balanced, element) - normal * 1.25f) < 0.01f, "No se aplica el bonus de " + type);
        float counter = NovaDamage(WizardType.Balanced, WizardType.Balanced, opposite);
        Check(Math.Abs(NovaDamage(WizardType.Balanced, type, opposite) - counter * 1.25f) < 0.01f, "No se aplica la debilidad de " + type);
    }
    Check(Math.Abs(NovaDamage(WizardType.Pyromancer, WizardType.Cryomancer, Element.Fire) - 58 * 1.5625f) < 0.01f, "Bonus y debilidad no se multiplican.");
});
Test("Combinaciones: el bonus no multiplica todo el hechizo ni la curación", () =>
{
    float mixed = NovaDamage(WizardType.Pyromancer, WizardType.Balanced, Element.Fire, Element.Arcane);
    Check(Math.Abs(mixed - (58 * 1.25f + 115)) < 0.01f, "El bonus de fuego multiplicó el componente arcano.");
    Check(Math.Abs(NovaDamage(WizardType.Pyromancer, WizardType.Balanced, Element.Water, Element.Fire) - (80 * 1.25f + 80)) < 0.01f, "El bonus de fuego multiplicó también el agua del vapor.");
    var w = new GameWorld(); var p = w.AddPlayer("Piromante", wizardType: WizardType.Pyromancer)!; p.Health = 500;
    Select(w, p, Element.Life); Send(w, p, "self"); Check(p.Health == 645, "La afinidad alteró la curación.");
});
Test("La quemadura conserva la potencia del piromante al desconectarse", () =>
{
    var w = new GameWorld(new() { Mode = GameMode.Arena });
    var a = w.AddPlayer("Fuego", wizardType: WizardType.Pyromancer)!; var b = w.AddPlayer("Hielo", wizardType: WizardType.Cryomancer)!;
    a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
    Select(w, a, Element.Fire); Send(w, a, "nova"); float health = b.Health; w.RemovePlayer(a.Id);
    for (int i = 0; i < 60; i++) w.Step();
    Check(Math.Abs(health - b.Health - 28 * 1.5625f) < 0.02f, "La quemadura perdió el bonus o la debilidad.");
});
Test("La debilidad consume más escudo y los efectos de impacto tienen límite", () =>
{
    var w = new GameWorld(new() { Mode = GameMode.Arena });
    var a = w.AddPlayer("Hielo", wizardType: WizardType.Cryomancer)!; var b = w.AddPlayer("Fuego", wizardType: WizardType.Pyromancer)!;
    a.Position = new(-2, 0); b.Position = new(1, 0); a.Invulnerable = b.Invulnerable = 0;
    b.Shield = b.ShieldMax = 420; b.ShieldTime = 12;
    Select(w, a, Element.Cold); Send(w, a, "nova");
    Check(Math.Abs(b.Shield - (420 - 24 * 1.5625f)) < 0.01f && b.Health == 1000, "La debilidad ignoró el escudo.");
    Check(w.Snapshot().Events.Any(e => e.Kind == "shield_hit"), "Falta el impacto de escudo.");
    a.ActionCooldown = 0; b.Shield = 1; Select(w, a, Element.Cold); Send(w, a, "nova");
    Check(w.Snapshot().Events.Count(e => e.Kind == "shield_break") == 1 && b.Shield == 0, "La rotura se perdió por el límite de efectos.");
    b.Health = 1000; b.Chill = b.Frozen = 0; a.ActionCooldown = 0;
    Select(w, a, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 60; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Check(w.Snapshot().Events.Count(e => e.Kind == "hurt") <= 9, "Se emite un impacto por cada tick del rayo.");
});
Test("El tipo es válido, se replica y no puede cambiar con una intención", () =>
{
    var w = new GameWorld(); Check(w.AddPlayer("Inválido", wizardType: (WizardType)999) == null, "Se admitió un tipo desconocido.");
    var p = w.AddPlayer("Mago", wizardType: WizardType.Geomancer)!;
    w.ApplyMessage(p.Id, new() { Type = "input", Sequence = 1, WizardType = WizardType.Pyromancer });
    var state = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
    Check(state.Players[0].WizardType == WizardType.Geomancer, "Una intención cambió el tipo o no se replicó.");
    bool rejected = false;
    try { SnapshotValidation.Check(state with { Players = [state.Players[0] with { WizardType = (WizardType)999 }] }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "El cliente aceptó un tipo desconocido.");
});
Test("Duelo y arena respetan su límite, ganador y metadatos de victoria", () =>
{
    foreach (var mode in new[] { GameMode.Duel, GameMode.Arena })
    {
        var w = new GameWorld(new() { Mode = mode, ScoreLimit = 2, Tickets = 33 });
        var a = w.AddPlayer("Ganador", 0, wizardType: WizardType.Pyromancer)!; var b = w.AddPlayer("Rival", 1)!;
        for (int kill = 0; kill < 2; kill++)
        {
            a.Position = new(-2, 0); b.Position = new(1, 0); b.Health = 1; a.Invulnerable = b.Invulnerable = a.ActionCooldown = 0;
            Select(w, a, Element.Arcane); Send(w, a, "nova"); w.Step();
            if (kill == 0) Check(!w.MatchEnded, "La partida terminó antes del límite.");
        }
        var state = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
        Check(state.MatchEnded && state.Winner == (mode == GameMode.Duel ? a.Team : a.Id), "Ganador incorrecto.");
        Check(state.ScoreLimit == 2 && state.StartingTickets == 33 && MatchRules.Objective(mode, 2, 33).Contains("2"), "La ayuda no recibe el límite real.");
        for (int i = 0; i < 550; i++) w.Step();
        Check(!w.MatchEnded && a.Kills == 0 && a.WizardType == WizardType.Pyromancer, "La nueva partida perdió el tipo o mantuvo bajas.");
    }
});
Test("Obstáculos bloquean movimiento y rayos", () =>
{
    var (w, a, b) = Setup(); a.Position = new(-16, 0); b.Position = new(-10, 0);
    Send(w, a, "input", aim: b.Position); w.Step();
    Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 60; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Check(b.Health == 1000, "El rayo atravesó una columna.");
    Check(!w.Walkable(new(-13, 0)), "Se puede caminar dentro de la columna.");
});
Test("Joystick: intensidad proporcional, diagonal limitada y ruta a velocidad normal", () =>
{
    var world = new GameWorld(new() { Mode = GameMode.Arena });
    var player = world.AddPlayer("Joystick")!;
    float Distance(V2 movement, bool moveTo = false)
    {
        player.Position = V2.Zero;
        world.ApplyMessage(player.Id, new() { Type = "input", Sequence = player.LastSequence + 1,
            MoveX = movement.X, MoveY = movement.Y, MoveTo = moveTo, TargetX = 10, AimX = 10 });
        world.Step();
        return player.Position.Length;
    }
    float maximum = Distance(new(1, 0));
    Check(Math.Abs(maximum - 6.2f / 60) < 0.0001f, "Cambió la velocidad máxima.");
    Check(Math.Abs(Distance(new(0.25f, 0)) - maximum * 0.25f) < 0.0001f, "Una inclinación pequeña se normaliza a velocidad completa.");
    Check(Math.Abs(Distance(new(0.3f, 0.4f)) - maximum * 0.5f) < 0.0001f, "El joystick diagonal pierde su intensidad.");
    Check(Math.Abs(Distance(new(1, 1)) - maximum) < 0.0001f, "Las flechas diagonales exceden la velocidad máxima.");
    Check(Distance(V2.Zero) == 0, "El mago se mueve con el joystick suelto.");
    Check(Math.Abs(Distance(V2.Zero, true) - maximum) < 0.0001f, "La ruta del ratón perdió su velocidad.");
});
Test("Se aplica la configuración de fuego amigo", () =>
{
    var world = new GameWorld(new() { Mode = GameMode.Warfare, FriendlyFire = false });
    var a = world.AddPlayer("A", 0)!; world.AddPlayer("Enemigo", 1); var ally = world.AddPlayer("Aliado", 0)!;
    a.Position = new(-2, 0); ally.Position = new(2, 0); a.Invulnerable = ally.Invulnerable = 0;
    Send(world, a, "input"); world.Step(); Select(world, a, Element.Arcane, Element.Arcane, Element.Arcane);
    Send(world, a, "cast"); Run(world, a, 30);
    Check(ally.Health == 1000, "Se dañó al aliado con fuego amigo desactivado.");
});
Test("El bastón respeta el fuego amigo en vida y escudo sin proteger rivales", () =>
{
    foreach (var mode in new[] { GameMode.Warfare, GameMode.Arena })
    foreach (bool friendly in new[] { false, true })
    {
        var world = new GameWorld(new() { Mode = mode, FriendlyFire = friendly });
        var attacker = world.AddPlayer("Atacante", 0)!;
        var enemy = world.AddPlayer("Rival", 1)!;
        var ally = world.AddPlayer("Mismo color", 0)!;
        attacker.Position = new(-8, 0); attacker.Facing = new(1, 0);
        enemy.Position = new(-7, 0); ally.Position = new(-6.7f, 0);
        foreach (var player in world.Players.Values) player.Invulnerable = 0;
        ally.Shield = ally.ShieldMax = 50; ally.ShieldTime = 12;
        Send(world, attacker, "melee");
        bool protectedAlly = mode == GameMode.Warfare && !friendly;
        Check(enemy.Health == 910, "El bastón dejó de dañar al rival.");
        Check(ally.Health == (protectedAlly ? 1000 : 960) && ally.Shield == (protectedAlly ? 50 : 0),
            $"Daño o absorción incorrectos: {mode}, fuego amigo {friendly}.");
    }
});
Test("Captura, desgaste de tickets, victoria y nueva partida", () =>
{
    var w = new GameWorld(new() { Mode = GameMode.Warfare, Tickets = 5 }); var a = w.AddPlayer("A", 0)!;
    a.Position = V2.Zero;
    for (int i = 0; i < 1000 && !w.MatchEnded; i++) { Send(w, a, "input"); w.Step(); }
    Check(w.MatchEnded && w.Winner == 0 && w.Scores[1] == 0, "La captura no terminó la partida.");
    for (int i = 0; i < 550; i++) w.Step();
    Check(!w.MatchEnded && w.Scores[0] == 5 && w.Scores[1] == 5, "No se reinició la partida.");
});
Test("Ciudadela: suelo exactamente cuatro veces mayor, forma irregular y puntos accesibles", () =>
{
    var classic = ArenaMap.Get(ArenaMapId.Classic); var map = ArenaMap.Get(ArenaMapId.Citadel);
    Check(classic.Area == 1408 && map.Area == classic.Area * 4 && map.HalfWidth != map.HalfHeight,
        "El segundo mapa no tiene la superficie o la forma solicitadas.");
    Check(map.Floors.Count == 5 && !map.Inside(new(-22, 24)) && map.Walkable(new(-22, 10)), "Los pasillos no delimitan el suelo.");
    foreach (var point in map.ControlPoints) Check(map.Walkable(point), "Un punto está bloqueado.");
    for (int team = 0; team < 2; team++)
    for (int i = 0; i < 8; i++) Check(map.Walkable(map.Spawn(team, i)), "Una aparición está fuera del suelo.");
    var path = map.FindPath(map.ControlPoints[0], map.ControlPoints[2]);
    Check(path.Length > 1 && path[^1] == map.ControlPoints[2], "No existe ruta entre los extremos.");
    var at = map.ControlPoints[0];
    foreach (var next in path) { Check(map.ClearPath(at, next), "La ruta cruza una pared o un pilar."); at = next; }
    Check(map.FindPath(map.ControlPoints[0], new(-22, 24)).Length == 0, "Se trazó una ruta por un patio sin suelo.");
    var world = new GameWorld(new() { Map = ArenaMapId.Citadel }); var player = world.AddPlayer("Pasillos")!;
    Check(world.ApplyMessage(player.Id, new() { Type = "input", Sequence = 1, MoveTo = true, TargetX = 50, TargetY = 28 }) &&
        player.Input.TargetX == 50 && player.Input.TargetY == 28, "Se limitaron los destinos al mapa pequeño.");
    var snapshot = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
    Check(snapshot.Map == map.Id && snapshot.Points.Select(p => p.Position).SequenceEqual(map.ControlPoints), "La red perdió el mapa o sus puntos.");
    SnapshotValidation.Check(snapshot);
    foreach (var invalid in new[] { snapshot with { Map = (ArenaMapId)99 }, snapshot with { HoldingTeam = 2 },
        snapshot with { HoldSeconds = float.NaN }, snapshot with { HoldSeconds = 16 } })
    {
        bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Se aceptaron metadatos de mapa o control inválidos.");
    }
});
void LethalNova(GameWorld world, Wizard killer, Wizard victim)
{
    killer.Position = new(-2, 0); victim.Position = new(1, 0);
    killer.ActionCooldown = killer.Invulnerable = victim.Invulnerable = 0;
    victim.Health = 1; victim.Shield = victim.ShieldMax = 0;
    Send(world, killer, "clear"); Select(world, killer, Element.Arcane); Send(world, killer, "nova");
    Check(!victim.Alive, "El hechizo no produjo la baja de prueba.");
}
Test("Ascensión: mapa distinto, apariciones, puntos y rutas transitables", () =>
{
    var map = ArenaMap.Get(ArenaMapId.Ascension);
    Check(map.Floors.Count == 3 && map.Area == 2688 && map.KillHealthBonus == 10 && map.RespawnDeathPenalty == 0.5f,
        "No se registró el tercer mapa con sus reglas.");
    Check(!map.Inside(new(30, 20)) && map.Walkable(new(0, 20)), "El coliseo no tiene el contorno en cruz.");
    foreach (var point in map.ControlPoints) Check(map.Walkable(point), "Hay un punto bloqueado.");
    for (int team = 0; team < 2; team++)
    for (int i = 0; i < 8; i++) Check(map.Walkable(map.Spawn(team, i)), "Hay una aparición bloqueada.");
    var from = new V2(-31, 0); var goal = new V2(0, 20); var path = map.FindPath(from, goal);
    Check(path.Length > 0 && path[^1] == goal, "No se puede llegar al graderío.");
    foreach (var next in path) { Check(map.ClearPath(from, next), "Una ruta atraviesa el límite o un pilar."); from = next; }
    Check(MatchRules.Objective(GameMode.Warfare, 15, 75, map.Id).Contains("+10") &&
        MatchRules.Details(GameMode.Duel, map.Id).Contains("3 segundos"), "La ayuda no explica el bonus o la primera muerte.");
});
Test("Ascensión: +10 por baja, vida actual y máxima, bonus conservado al morir y replicación", () =>
{
    var world = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Arena, SpawnSeed = 271, PickupSeed = 271 });
    // Esta regresión mide las bajas y la reaparición. Una reliquia aleatoria
    // junto al punto de llegada puede añadir vida en el mismo tick; se prueba aparte.
    var pickups = (Array)typeof(GameWorld).GetField("_pickups", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(world)!;
    void WaitForRespawn(int ticks)
    {
        foreach (var pickup in pickups) pickup!.GetType().GetField("Respawn")!.SetValue(pickup, RelicBuffs.RespawnSeconds);
        for (int i = 0; i < ticks; i++) world.Step();
    }
    var a = world.AddPlayer("Ascendente")!; var b = world.AddPlayer("Rival")!;
    a.Health = 700; LethalNova(world, a, b);
    Check(a.Health == 710 && a.MaxHealth == 1010 && a.Kills == 1 && b.Deaths == 1 && b.Respawn == 4.5f,
        "La primera baja no sumó exactamente diez o la primera muerte no sumó medio segundo.");
    WaitForRespawn(271);
    Check(b.Alive && b.Health == 1000, "No reapareció el rival.");
    LethalNova(world, a, b); Check(a.MaxHealth == 1020 && a.Health == 720 && b.Respawn == 5, "El bonus o la penalización no se acumulan.");
    WaitForRespawn(301);
    LethalNova(world, b, a); Check(a.MaxHealth == 1020 && a.Health == 0, "Morir borró el bonus.");
    var dead = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
    SnapshotValidation.Check(dead);
    Check(dead.Map == ArenaMapId.Ascension && dead.Players.Single(p => p.Id == a.Id).MaxHealth == 1020 &&
        dead.Players.Single(p => p.Id == a.Id).Respawn == 4.5f, "La red perdió vida máxima o espera.");
    WaitForRespawn(271);
    Check(a.Alive && a.Health == 1020 && a.MaxHealth == 1020, "Reaparecer perdió la vida permanente.");
});
Test("Ascensión: cada muerte añade medio segundo y la espera nunca supera diez segundos", () =>
{
    foreach (var mode in Enum.GetValues<GameMode>().Where(m => m != GameMode.Defense))
    {
        var world = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = mode, ScoreLimit = 200, Tickets = 1000 });
        var a = world.AddPlayer("Autor")!; var b = world.AddPlayer("Objetivo")!;
        for (int death = 1; death <= 20; death++)
        {
            LethalNova(world, a, b);
            float delay = Math.Min(10, (mode == GameMode.Duel ? 2.5f : 4) + 0.5f * death);
            Check(b.Deaths == death && b.Respawn == delay, "Espera incorrecta en " + mode + ", muerte " + death);
            int ticks = (int)Math.Round(delay * 60);
            for (int i = 0; i < ticks - 1; i++) world.Step();
            Check(!b.Alive, "El mago apareció antes de completar la espera.");
            world.Step(); world.Step();
            Check(b.Alive, "El mago no apareció al finalizar la espera.");
        }
    }
});
Test("Ascensión: autocuración, rayos de vida, regeneración y minas respetan el máximo acumulado", () =>
{
    var world = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Warfare });
    var a = world.AddPlayer("Vida", 0)!; var b = world.AddPlayer("Rival", 1)!;
    var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(0, 20);
    LethalNova(world, a, b); a.ActionCooldown = 0; a.Health = 1000;
    Select(world, a, Element.Life); Send(world, a, "self");
    Check(a.Health == 1010, "La autocuración se limitó a 1000.");
    a.ActionCooldown = 0; a.Health = 1000;
    Select(world, a, Element.Shield, Element.Life); Send(world, a, "self");
    for (int i = 0; i < 30; i++) world.Step();
    Check(a.Health == 1010, "La regeneración se limitó a 1000.");
    a.Health = 1000; a.RegenerationRate = a.Shield = a.ShieldMax = a.ShieldTime = 0;
    ally.Position = new(-5, 0); a.Position = new(-1.8f, 0); ally.ActionCooldown = 0;
    Select(world, ally, Element.Life); Send(world, ally, "input", aim: a.Position); world.Step(); Send(world, ally, "cast");
    Run(world, ally, 30); Send(world, ally, "release");
    Check(a.Health == 1010, "El rayo de vida se limitó a 1000.");
    a.Health = 1000; ally.ActionCooldown = 0; ally.Position = new(-5, 0); a.Position = new(-1.8f, 0);
    Send(world, ally, "clear"); Select(world, ally, Element.Shield, Element.Life);
    Send(world, ally, "input", aim: new(3, 0)); world.Step(); Send(world, ally, "cast");
    for (int i = 0; i < 32; i++) world.Step();
    Check(a.Health == 1010 && world.Snapshot().Fields.All(f => f.Kind != "healing_mine"), "La mina no detectó vida faltante por encima de 1000.");
});
Test("Ascensión: fuego amigo cuenta, suicidio no da bonus y una baja póstuma no revive", () =>
{
    var world = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Warfare });
    var a = world.AddPlayer("Autor", 0)!; var b = world.AddPlayer("Rival", 1)!; var ally = world.AddPlayer("Aliado", 0)!;
    b.Position = new(0, 20); LethalNova(world, a, ally);
    Check(a.MaxHealth == 1010 && a.Kills == 0, "La baja aliada no recibió bonus o alteró el castigo de marcador.");
    a.ActionCooldown = a.Invulnerable = 0; a.Health = 1;
    Select(world, a, Element.Arcane); Send(world, a, "self");
    Check(!a.Alive && a.MaxHealth == 1010 && a.Deaths == 1, "El suicidio dio vida o no contó como muerte.");
    b.Position = new(1, 0); b.Health = 0.1f; b.Invulnerable = 0; b.Burning = 1; b.BurnOwner = a.Id;
    world.Step();
    Check(!b.Alive && !a.Alive && a.Health == 0 && a.MaxHealth == 1020 && b.Deaths == 1,
        "La baja póstuma reanimó al autor o no aumentó su capacidad.");
    world.Step(); Check(a.MaxHealth == 1020 && b.Deaths == 1, "La misma muerte otorgó bonus dos veces.");
});
Test("Ascensión: reanimación conserva el bonus y nueva partida reinicia vida y penalización", () =>
{
    var world = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Warfare });
    var a = world.AddPlayer("Autor", 0)!; var b = world.AddPlayer("Rival", 1)!; var ally = world.AddPlayer("Aliado", 0)!;
    ally.Position = new(0, 20); LethalNova(world, a, b);
    for (int i = 0; i < 271; i++) world.Step(); LethalNova(world, b, a);
    ally.Position = a.Position + new V2(1, 0); ally.ActionCooldown = 0; Send(world, ally, "magick", slot: 3);
    Check(a.Alive && a.Health == 600 && a.MaxHealth == 1010 && a.Deaths == 1, "Reanimar borró el bonus o la penalización.");
    var duel = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Duel, ScoreLimit = 1 });
    a = duel.AddPlayer("Ganador")!; b = duel.AddPlayer("Rival")!; LethalNova(duel, a, b); duel.Step();
    Check(duel.MatchEnded && a.MaxHealth == 1010, "No se conservó el bonus al terminar.");
    for (int i = 0; i < 541; i++) duel.Step();
    Check(!duel.MatchEnded && a.MaxHealth == 1000 && a.Health == 1000 && a.Kills == 0 && b.Deaths == 0,
        "La nueva partida conservó vida o penalización anterior.");
    LethalNova(duel, a, b); Check(b.Respawn == 3, "La nueva partida no reinició la espera.");
});
Test("Ascensión: las reglas de vida y reaparición no se aplican a los mapas anteriores", () =>
{
    foreach (var map in new[] { ArenaMapId.Classic, ArenaMapId.Citadel })
    foreach (var mode in Enum.GetValues<GameMode>().Where(m => m != GameMode.Defense))
    {
        var world = new GameWorld(new() { Map = map, Mode = mode }); var a = world.AddPlayer("Autor")!; var b = world.AddPlayer("Rival")!;
        a.Health = 700; LethalNova(world, a, b);
        Check(a.MaxHealth == 1000 && a.Health == 700 && b.Respawn == (mode == GameMode.Duel ? 2.5f : 4), "Las reglas se filtraron a " + map);
    }
});
(GameWorld World, Wizard A, Wizard B) RelicsSetup(GameMode mode = GameMode.Arena, int scoreLimit = 200)
{
    var w = new GameWorld(new() { Map = ArenaMapId.Relics, Mode = mode, ScoreLimit = scoreLimit, Tickets = 1000 });
    var a = w.AddPlayer("Recolector", 0)!; var b = w.AddPlayer("Rival", 1)!;
    a.Position = V2.Zero; b.Position = new(30, 20); a.Invulnerable = b.Invulnerable = 0;
    return (w, a, b);
}
void Collect(GameWorld w, Wizard p, BuffKind kind)
{
    p.Position = w.Snapshot().Pickups!.First(i => i.Kind == kind && i.Active).Position;
    w.Step(); p.Position = V2.Zero;
}
Test("Reliquias: catorce objetos simétricos, siete tipos y exclusividad del mapa", () =>
{
    var (w, _, _) = RelicsSetup(); var items = w.Snapshot().Pickups!;
    Check(items.Length == 14 && items.All(i => i.Active && i.Respawn == 0 && w.Map.Walkable(i.Position)), "Objetos iniciales inválidos.");
    foreach (var kind in Enum.GetValues<BuffKind>())
    {
        var pair = items.Where(i => i.Kind == kind).ToArray();
        Check(pair.Length == 2 && pair[0].Position.X == -pair[1].Position.X && pair[0].Position.Y == pair[1].Position.Y, "Un tipo no tiene dos objetos simétricos.");
    }
    foreach (var map in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).HasPickups))
        Check(new GameWorld(new() { Map = map, Mode = map == ArenaMapId.Bastion ? GameMode.Defense : GameMode.Warfare, MaxPlayers = 4 }).Snapshot().Pickups!.Length == 0, "Se filtraron objetos a " + map);
    SnapshotValidation.Check(w.Snapshot());
});
Test("Reliquias: solo recoge el vivo más cercano, empate estable y reaparición tras doce segundos", () =>
{
    var (w, a, b) = RelicsSetup(); var item = w.Snapshot().Pickups![0];
    a.Position = b.Position = item.Position; w.Step();
    Check(a.BuffStacks[0] == 1 && b.BuffStacks[0] == 0 && !w.Snapshot().Pickups![0].Active, "La recogida tuvo dos ganadores o un empate inestable.");
    a.Position = V2.Zero; b.Position = new(30, 20);
    for (int i = 0; i < 719; i++) w.Step();
    Check(!w.Snapshot().Pickups![0].Active, "El objeto reapareció antes de doce segundos.");
    w.Step(); w.Step(); Check(w.Snapshot().Pickups![0].Active, "El objeto no reapareció.");
    a.Position = item.Position; a.Health = 0; a.Respawn = 10; b.Position = item.Position + new V2(0.3f, 0);
    w.Step(); Check(b.BuffStacks[0] == 1 && a.BuffStacks[0] == 1, "Un muerto recogió el objeto.");
    var (nearest, c, d) = RelicsSetup(); var at = nearest.Snapshot().Pickups![0].Position;
    c.Position = at + new V2(0.8f, 0); d.Position = at + new V2(0.1f, 0); nearest.Step();
    Check(c.BuffStacks[0] == 0 && d.BuffStacks[0] == 1, "El ID desplazó al mago más cercano.");
});
Test("Reliquias: daño y protección acumulados afectan vida, escudo, bastón y quemaduras", () =>
{
    var (w, a, b) = RelicsSetup(); Collect(w, a, BuffKind.Power); Collect(w, a, BuffKind.Power);
    Collect(w, b, BuffKind.Protection); Collect(w, b, BuffKind.Protection);
    a.Position = new(-2, 0); b.Position = new(1, 0);
    float multiplier = 1.2f * 0.92f * 0.92f;
    Select(w, a, Element.Arcane); Send(w, a, "nova");
    Check(Math.Abs(b.Health - (1000 - 115 * multiplier)) < 0.01f, "Daño o reducción no se acumulan.");
    b.Health = 1000; b.Shield = b.ShieldMax = 420; b.ShieldTime = 12; a.ActionCooldown = 0;
    Select(w, a, Element.Arcane); Send(w, a, "nova");
    Check(b.Health == 1000 && Math.Abs(b.Shield - (420 - 115 * multiplier * 0.5f)) < 0.01f, "La reducción no protege el escudo.");
    b.Shield = 0; b.Position = new(-0.6f, 0); a.Facing = new(1, 0); a.ActionCooldown = 0; Send(w, a, "melee");
    Check(Math.Abs(b.Health - (1000 - 90 * multiplier)) < 0.01f, "El bastón ignora las mejoras.");
    b.Health = 1000; b.Burning = 1; b.BurnOwner = a.Id; b.BurnMultiplier = 1; w.Step();
    Check(Math.Abs(b.Health - (1000 - 28 * multiplier / 60)) < 0.01f, "La quemadura ignora las mejoras.");
    b.BuffStacks[(int)BuffKind.Protection] = 100;
    Check(RelicBuffs.Incoming(b) == 0.2f, "La protección superó la reducción máxima del 80 %.");
});
Test("Reliquias: vida, regeneración, energía, movimiento y curación se acumulan y respetan límites", () =>
{
    var (w, a, _) = RelicsSetup();
    foreach (var kind in Enum.GetValues<BuffKind>()) { Collect(w, a, kind); Collect(w, a, kind); }
    Check(a.MaxHealth == 1200 && a.BuffStacks.All(n => n == 2), "No se acumularon los siete tipos.");
    a.Health = 600; a.Energy = 30; Run(w, a, 60);
    Check(Math.Abs(a.Health - 640) < 0.01f && Math.Abs(a.Energy - 33.8f) < 0.01f, "Regeneración o energía incorrectas.");
    var start = a.Position;
    w.ApplyMessage(a.Id, new() { Type = "input", Sequence = a.LastSequence + 1, MoveX = 1 }); w.Step();
    Check(Math.Abs(V2.Distance(start, a.Position) - 6.2f * 1.1f / 60) < 0.001f, "La velocidad no se acumuló.");
    Send(w, a, "input"); float before = a.Health;
    Select(w, a, Element.Life); Send(w, a, "self");
    Check(Math.Abs(a.Health - before - 145 * 1.2f) < 0.01f, "La curación no se acumuló.");
    a.Health = 1199; a.Energy = 99.99f; w.Step();
    Check(a.Health <= 1200 && a.Energy == 100, "Se superaron los máximos.");
    a.BuffStacks[(int)BuffKind.Speed] = 100; Check(RelicBuffs.Speed(a) == 2, "La velocidad superó su límite.");
    SnapshotValidation.Check(w.Snapshot());
});
Test("Reliquias: sanación amplifica rayos, minas y escudo sin multiplicar la regeneración de objetos", () =>
{
    var (w, a, b) = RelicsSetup(GameMode.Warfare); var ally = w.AddPlayer("Aliado", 0)!;
    ally.Position = new(30, -20); ally.Invulnerable = 0; Collect(w, a, BuffKind.Healing);
    a.Position = new(-5, 0); ally.Position = new(-1.8f, 0); ally.Health = 500;
    Select(w, a, Element.Life); Send(w, a, "input", aim: ally.Position); Send(w, a, "cast"); w.Step(); Send(w, a, "release");
    Check(Math.Abs(ally.Health - 500 - 140 * 1.1f / 60) < 0.01f, "El rayo no amplificó la curación.");
    a.ActionCooldown = 0; Select(w, a, Element.Shield, Element.Life); Send(w, a, "cast");
    for (int i = 0; i < 32; i++) w.Step();
    Check(Math.Abs(ally.Health - 500 - 140 * 1.1f / 60 - 252 * 1.1f) < 0.01f, "La mina no amplificó la curación.");
    a.ActionCooldown = 0; a.Health = 500; Collect(w, a, BuffKind.Regeneration);
    Select(w, a, Element.Shield, Element.Life); Send(w, a, "self"); float before = a.Health; w.Step();
    Check(Math.Abs(a.Health - before - (38 * 1.1f + 20) / 60) < 0.01f && Math.Abs(w.Snapshot().Players.Single(p => p.Id == a.Id).RegenerationRate - 41.8f) < 0.01f,
        "Escudo o regeneración de objetos incorrectos.");
});
Test("Reliquias: muerte y suicidio borran todas las mejoras; reanimar y reaparecer no las restauran", () =>
{
    var (w, a, b) = RelicsSetup(GameMode.Warfare); var ally = w.AddPlayer("Reanimador", 0)!; ally.Position = new(30, -20);
    foreach (var kind in Enum.GetValues<BuffKind>()) Collect(w, a, kind);
    LethalNova(w, b, a);
    Check(a.MaxHealth == 1000 && a.BuffStacks.All(n => n == 0) && w.Snapshot().Players.Single(p => p.Id == a.Id).Buffs!.Length == 0,
        "Morir dejó mejoras activas.");
    ally.Position = a.Position + new V2(1, 0); Send(w, ally, "magick", slot: 3);
    Check(a.Alive && a.Health == 600 && a.MaxHealth == 1000 && a.BuffStacks.All(n => n == 0), "Reanimar restauró mejoras.");
    Collect(w, a, BuffKind.Vitality); a.Health = 1; a.Invulnerable = a.ActionCooldown = 0;
    Select(w, a, Element.Arcane); Send(w, a, "self");
    Check(!a.Alive && a.MaxHealth == 1000 && a.BuffStacks.All(n => n == 0), "El suicidio conservó mejoras.");
    a.Position = V2.Zero; for (int i = 0; i < 241; i++) w.Step();
    Check(a.Alive && a.Health == 1000 && a.BuffStacks.All(n => n == 0), "La aparición restauró mejoras.");
    SnapshotValidation.Check(w.Snapshot());
});
Test("Reliquias: nueva partida reinicia acumulaciones, vida máxima y los catorce objetos", () =>
{
    var (w, a, b) = RelicsSetup(GameMode.Duel, 1); Collect(w, a, BuffKind.Vitality); Collect(w, a, BuffKind.Power);
    LethalNova(w, a, b); w.Step(); Check(w.MatchEnded && a.MaxHealth == 1100, "No terminó el duelo de prueba.");
    for (int i = 0; i < 541; i++) w.Step();
    Check(!w.MatchEnded && a.MaxHealth == 1000 && a.Health == 1000 && a.BuffStacks.All(n => n == 0) && w.Snapshot().Pickups!.All(i => i.Active),
        "El reinicio conservó vida, objetos recogidos o mejoras.");
});
Test("Reliquias: red independiente y validación de acumulaciones y objetos incompletos", () =>
{
    var (w, a, _) = RelicsSetup(); Collect(w, a, BuffKind.Power); var state = w.Snapshot();
    var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(state))!; SnapshotValidation.Check(copy);
    Check(copy.Pickups!.Length == 14 && copy.Players[0].Buffs!.Single().Stacks == 1 && !copy.Pickups[0].Active, "JSON perdió objetos o mejoras.");
    state.Players[0].Buffs![0] = new(BuffKind.Power, 500); state.Pickups![0] = state.Pickups[0] with { Respawn = 1 };
    Check(w.Snapshot().Players[0].Buffs!.Single().Stacks == 1 && w.Snapshot().Pickups![0].Respawn == 12, "Un snapshot modificó el mundo.");
    foreach (var invalid in new[]
    {
        copy with { Pickups = [] }, copy with { Map = ArenaMapId.Classic },
        copy with { Pickups = copy.Pickups!.Select(i => i.Id == 1 ? i with { Respawn = float.NaN } : i).ToArray() },
        copy with { Players = [copy.Players[0] with { Buffs = [new(BuffKind.Power, 0)] }] },
        copy with { Players = [copy.Players[0] with { Buffs = [new((BuffKind)99, 1)] }] },
        copy with { Players = [copy.Players[0] with { Buffs = [new(BuffKind.Power, 1), new(BuffKind.Power, 1)] }] },
        copy with { Players = [copy.Players[0] with { Alive = false }] },
        copy with { Players = [copy.Players[0] with { CastingElements = [Element.Fire, Element.Fire, Element.Fire, Element.Fire] }] }
    })
    {
        bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Se aceptaron objetos o acumulaciones inválidos.");
    }
});
Test("Coliseo: objetos aleatorios transitables, separados y reproducibles con semilla", () =>
{
    for (int seed = 0; seed < 80; seed++)
    {
        var w = new GameWorld(new() { Map = ArenaMapId.Ascension, PickupSeed = seed }); var items = w.Snapshot().Pickups!;
        Check(items.Length == 14 && items.All(i => i.Active && w.Map.Walkable(i.Position) && w.Map.Inside(i.Position, 1.5f)), "Un objeto salió del suelo.");
        foreach (var kind in Enum.GetValues<BuffKind>()) Check(items.Count(i => i.Kind == kind) == 2, "Falta un tipo de objeto.");
        foreach (var item in items)
        {
            Check(items.All(i => i.Id == item.Id || V2.Distance(i.Position, item.Position) > 3) &&
                w.Map.ControlPoints.All(p => V2.Distance(p, item.Position) > 4), "Objetos superpuestos o sobre un punto.");
            for (int team = 0; team < 2; team++) for (int index = 0; index < 4; index++)
                Check(V2.Distance(item.Position, w.Map.Spawn(team, index)) > 5, "Un objeto apareció sobre una salida.");
            Check(w.Map.FindPath(w.Map.Spawn(0, 0), item.Position).Length > 0, "Un objeto no es accesible.");
        }
        var twin = new GameWorld(new() { Map = ArenaMapId.Ascension, PickupSeed = seed });
        Check(items.SequenceEqual(twin.Snapshot().Pickups!), "La semilla no reproduce las posiciones."); SnapshotValidation.Check(w.Snapshot());
    }
    var a = new GameWorld(new() { Map = ArenaMapId.Ascension }); var b = new GameWorld(new() { Map = ArenaMapId.Ascension });
    Check(!a.Snapshot().Pickups!.SequenceEqual(b.Snapshot().Pickups!), "Todas las salas comparten la misma distribución.");
});
Test("Coliseo: el objeto conserva su espera y reaparece en una posición nueva", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Arena, PickupSeed = 51 }); var a = w.AddPlayer("Recolector")!;
    var item = w.Snapshot().Pickups![0]; a.Position = item.Position; a.Invulnerable = 0; w.Step(); a.Position = V2.Zero;
    Check(a.BuffStacks[(int)item.Kind] == 1 && !w.Snapshot().Pickups![0].Active, "No se recogió el objeto.");
    for (int i = 0; i < 719; i++) w.Step();
    Check(!w.Snapshot().Pickups![0].Active && w.Snapshot().Pickups![0].Position == item.Position, "Se movió o apareció antes de tiempo.");
    w.Step(); w.Step(); var respawned = w.Snapshot().Pickups![0];
    Check(respawned.Active && V2.Distance(respawned.Position, item.Position) > 4 && w.Map.Walkable(respawned.Position), "La nueva aparición no cambió de sitio.");
    SnapshotValidation.Check(w.Snapshot());
    var json = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
    Check(json.Pickups![0] == respawned, "La red perdió la nueva posición.");
});
Test("Coliseo: al morir desaparece la vitalidad temporal y se conserva la vida por bajas", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Warfare, PickupSeed = 17 });
    var a = w.AddPlayer("Ascendente", 0)!; var b = w.AddPlayer("Rival", 1)!; var ally = w.AddPlayer("Aliado", 0)!; ally.Position = new(0, 20);
    Collect(w, a, BuffKind.Vitality); Collect(w, a, BuffKind.Power); LethalNova(w, a, b);
    Check(a.MaxHealth == 1110 && a.BuffStacks[(int)BuffKind.Vitality] == 1, "La baja no sumó vida permanente a la temporal.");
    for (int i = 0; i < 271; i++) w.Step(); LethalNova(w, b, a);
    Check(a.MaxHealth == 1010 && a.BuffStacks.All(n => n == 0) && a.Respawn == 4.5f, "Morir borró el bonus permanente o conservó mejoras temporales.");
    ally.Position = a.Position + new V2(1, 0); ally.ActionCooldown = 0; Send(w, ally, "magick", slot: 3);
    Check(a.Alive && a.MaxHealth == 1010 && a.BuffStacks.All(n => n == 0), "Reanimar mezcló las mejoras permanentes y temporales."); SnapshotValidation.Check(w.Snapshot());
});
Test("Coliseo: nueva partida recoloca los catorce objetos y reinicia ambas clases de mejoras", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Ascension, Mode = GameMode.Duel, ScoreLimit = 1, PickupSeed = 38 });
    var a = w.AddPlayer("Autor", 0)!; var b = w.AddPlayer("Rival", 1)!; var initial = w.Snapshot().Pickups!;
    Collect(w, a, BuffKind.Vitality); LethalNova(w, a, b); w.Step();
    for (int i = 0; i < 541; i++) w.Step(); var restarted = w.Snapshot();
    Check(!w.MatchEnded && a.MaxHealth == 1000 && a.BuffStacks.All(n => n == 0) && restarted.Pickups!.All(i => i.Active) &&
        !initial.Select(i => i.Position).SequenceEqual(restarted.Pickups!.Select(i => i.Position)), "El reinicio no recolocó los objetos o conservó mejoras."); SnapshotValidation.Check(restarted);
});
Test("Temas: bosque, ciudad, ruinas y santuario conservan geometría y reglas compartidas", () =>
{
    foreach (var (id, theme) in new[] { (ArenaMapId.Classic, ArenaTheme.Forest), (ArenaMapId.Citadel, ArenaTheme.City),
        (ArenaMapId.Ascension, ArenaTheme.Ruins), (ArenaMapId.Relics, ArenaTheme.Sanctuary) })
    {
        var map = ArenaMap.Get(id); Check(map.Theme == theme && map.ControlPoints.All(map.Walkable), "Tema o puntos inválidos.");
        for (int team = 0; team < 2; team++) for (int index = 0; index < 4; index++) Check(map.Walkable(map.Spawn(team, index)), "Una aparición quedó bloqueada.");
    }
});
Test("Reliquias: bots buscan y recogen objetos manteniendo snapshots válidos", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Relics, Mode = GameMode.Arena, Bots = 8, ScoreLimit = 200 });
    bool collected = false;
    for (int i = 0; i < 1200; i++)
    {
        w.Step(); if (i % 60 != 0) continue;
        var state = w.Snapshot(); SnapshotValidation.Check(state); collected |= state.Players.Any(p => p.Buffs!.Length > 0);
        Check(state.Players.All(p => w.Map.Walkable(p.Position)), "Un bot salió del suelo.");
    }
    Check(collected, "Los bots nunca recogieron objetos.");
});
(GameWorld World, Wizard[] Capturers) CaptureCitadel(int team)
{
    var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Tickets = 5 });
    for (int i = 0; i < 6; i++) w.AddPlayer("Mago " + i);
    var capturers = w.Players.Values.Where(p => p.Team == team).ToArray();
    for (int i = 0; i < capturers.Length; i++) capturers[i].Position = w.Map.ControlPoints[i];
    for (int i = 0; i < 300 && w.HoldingTeam < 0; i++) w.Step();
    Check(w.HoldingTeam == team && w.Snapshot().Points.All(p => p.Owner == team), "No se capturaron los tres puntos.");
    return (w, capturers);
}
Test("Ciudadela: ambos equipos ganan exactamente tras 900 ticks de control, y el reinicio borra el contador", () =>
{
    foreach (int team in new[] { 0, 1 })
    {
        var (w, _) = CaptureCitadel(team);
        Check(Math.Abs(w.HoldSeconds - GameWorld.StepSeconds) < 0.00001f, "El contador no empezó al capturar el último punto.");
        for (int i = 0; i < 898; i++) w.Step();
        Check(!w.MatchEnded && w.HoldSeconds < 15 && w.Scores.All(s => s == 5), "Se ganó antes de 15 segundos o se consumieron tickets.");
        w.Step(); var state = w.Snapshot();
        Check(w.MatchEnded && w.Winner == team && state.HoldSeconds == 15 && state.HoldingTeam == team, "No se ganó a los 15 segundos exactos.");
        SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(state))!);
        for (int i = 0; i < 550; i++) w.Step();
        Check(!w.MatchEnded && w.HoldingTeam == -1 && w.HoldSeconds == 0 && w.Snapshot().Points.All(p => p.Owner == -1) &&
            w.Players.Values.All(p => w.Map.Walkable(p.Position)), "El reinicio no limpió el control o reapareció en el mapa equivocado.");
    }
});
Test("Ciudadela: disputar y neutralizar reinicia el contador y nunca acumula intervalos separados", () =>
{
    var (w, capturers) = CaptureCitadel(0);
    for (int i = 0; i < 898; i++) w.Step();
    var enemy = w.Players.Values.First(p => p.Team == 1); enemy.Position = capturers[0].Position + new V2(1, 0); w.Step();
    Check(!w.MatchEnded && w.HoldingTeam == -1 && w.HoldSeconds == 0 && w.Snapshot().Points[0].Contested,
        "La disputa no detuvo la victoria en el último tick.");
    enemy.Position = w.Map.Spawn(1, 0); w.Step();
    for (int i = 0; i < 448; i++) w.Step();
    Check(!w.MatchEnded && w.HoldSeconds < 8, "Se conservaron segundos anteriores a la disputa.");
    capturers[0].Position = w.Map.Spawn(0, 0); enemy.Position = w.Map.ControlPoints[0]; w.Step();
    Check(w.HoldSeconds == 0, "Un ataque sin defensor no interrumpió el control.");
    for (int i = 0; i < 110; i++) w.Step();
    Check(w.Snapshot().Points[0].Owner == -1 && w.HoldingTeam == -1, "Neutralizar el punto mantuvo el contador.");
    enemy.Position = w.Map.Spawn(1, 0); capturers[0].Position = w.Map.ControlPoints[0];
    for (int i = 0; i < 300 && w.HoldingTeam < 0; i++) w.Step();
    Check(!w.MatchEnded && w.HoldSeconds == GameWorld.StepSeconds, "Recapturar no empezó un intervalo nuevo.");
});
Test("Ciudadela: control parcial y bajas no sustituyen los 15 segundos de control total", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Tickets = 1 }); var a = w.AddPlayer("Azul", 0)!;
    var b = w.AddPlayer("Rojo", 1)!; a.Position = w.Map.ControlPoints[1];
    for (int i = 0; i < 1500; i++) w.Step();
    Check(!w.MatchEnded && w.HoldSeconds == 0 && w.Scores.All(s => s == 1), "Un solo punto ganó por tickets.");
    b.Position = a.Position + new V2(1, 0); b.Health = 1; b.Invulnerable = 0;
    Select(w, a, Element.Arcane); Send(w, a, "nova"); w.Step();
    Check(!b.Alive && !w.MatchEnded && w.Scores.All(s => s == 1), "Una baja decidió la victoria por tickets.");
    Check(MatchRules.Objective(GameMode.Warfare, 15, 75, ArenaMapId.Citadel).Contains("15 segundos"), "La ayuda muestra otra condición de victoria.");
});
Test("Ciudadela: paredes bloquean caminar, rayos, proyectiles y traslación", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
    var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; a.Position = new(-35, -22); b.Position = new(-1, -22);
    a.Invulnerable = b.Invulnerable = 0;
    Check(!w.Walkable(new(-32, -22)) && !w.Walkable(new(-33.2f, -22)), "Se permitió entrar en el patio o atravesar la pared.");
    Send(w, a, "input", aim: b.Position); w.Step(); Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 30; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Check(b.Health == 1000 && w.Snapshot().Beams.All(beam => beam.To.X <= -33), "El rayo atravesó un límite del mapa.");
    Send(w, a, "release"); Select(w, a, Element.Earth); Send(w, a, "cast");
    for (int i = 0; i < 10; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Send(w, a, "release");
    for (int i = 0; i < 25; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Check(w.Snapshot().Projectiles.Length == 0 && b.Health == 1000, "La roca cruzó el patio.");
    Send(w, a, "magick", slot: 1);
    Check(a.Position.X < -33.48f && w.Map.Walkable(a.Position), "La traslación cruzó una pared del pasillo.");
    a.Position = new(-33.5f, -22); a.ActionCooldown = 0;
    Select(w, a, Element.Arcane, Element.Arcane, Element.Arcane); Send(w, a, "cast"); w.Step();
    Check(w.Snapshot().Beams.All(beam => beam.From.X < -33 && beam.To.X <= -33), "El origen del hechizo saltó la pared al estar pegado a ella.");
});
Test("Ciudadela: el desplazamiento al cursor sigue los pasillos y los bots no salen del suelo", () =>
{
    var w = new GameWorld(new() { Map = ArenaMapId.Citadel }); var player = w.AddPlayer("Ruta")!;
    player.Position = w.Map.ControlPoints[0]; var goal = w.Map.ControlPoints[2];
    for (int i = 0; i < 1700 && V2.Distance(player.Position, goal) > 0.2f; i++)
    {
        w.ApplyMessage(player.Id, new() { Type = "input", Sequence = player.LastSequence + 1, MoveTo = true,
            TargetX = goal.X, TargetY = goal.Y, AimX = goal.X, AimY = goal.Y }); w.Step();
        Check(w.Map.Walkable(player.Position), "El jugador cortó por una zona sin suelo.");
    }
    Check(V2.Distance(player.Position, goal) < 0.2f, "El jugador no llegó al punto al otro lado de los pasillos.");
    var bots = new GameWorld(new() { Map = ArenaMapId.Citadel, Bots = 8 });
    for (int i = 0; i < 3600; i++) bots.Step();
    SnapshotValidation.Check(bots.Snapshot());
    Check(bots.Players.Values.All(p => bots.Map.Walkable(p.Position)) && bots.Players.Values.Any(p => Math.Abs(p.Position.X) < 30),
        "Los bots quedaron atascados o atravesaron los bordes.");
});
Test("Traslación: la siguiente ruta se calcula desde la llegada", () =>
{
    var world = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
    var player = world.AddPlayer("Ruta previa")!;
    player.Position = world.Map.ControlPoints[0];
    var goal = world.Map.ControlPoints[2];
    void Click(GameWorld w, Wizard p) => w.ApplyMessage(p.Id, new() { Type = "input", Sequence = p.LastSequence + 1,
        MoveTo = true, TargetX = goal.X, TargetY = goal.Y, AimX = goal.X, AimY = goal.Y });
    Check(!world.Map.ClearPath(player.Position, goal), "La prueba no necesita una ruta por los pasillos.");
    Click(world, player); world.Step();
    player.Facing = new(0, 1);
    Send(world, player, "input", aim: player.Position + player.Facing * 14);
    var before = player.Position;
    Send(world, player, "magick", slot: 1);
    var arrival = player.Position;
    Check(V2.Distance(before, arrival) > 6 && !player.Input.MoveTo, "Traslación no detuvo el recorrido anterior.");
    world.Step(); Check(player.Position == arrival, "Se conservó el destino anterior al salto.");
    var fresh = new GameWorld(new() { Map = ArenaMapId.Citadel, Mode = GameMode.Arena });
    var reference = fresh.AddPlayer("Ruta desde la llegada")!; reference.Position = arrival;
    Click(world, player); Click(fresh, reference);
    for (int i = 0; i < 12; i++)
    {
        world.Step(); fresh.Step();
        Check(V2.Distance(player.Position, reference.Position) < 0.0001f,
            "El siguiente clic reutilizó una ruta calculada antes del salto.");
        Check(world.Map.Walkable(player.Position), "La nueva ruta dejó el suelo.");
    }
    Check(V2.Distance(player.Position, arrival) > 0.5f, "La nueva ruta no avanzó desde la llegada.");
});
Test("Traslación: usa el último destino aunque no haya pasado un tick y respeta los pilares", () =>
{
    foreach (var map in Enum.GetValues<ArenaMapId>())
    {
        var world = new GameWorld(new() { Map = map, MaxPlayers = 4, Mode = map == ArenaMapId.Bastion ? GameMode.Defense : ArenaMap.Get(map).SequentialCapture ? GameMode.Warfare : GameMode.Arena });
        var player = world.AddPlayer("Giro inmediato")!;
        var origin = map == ArenaMapId.Frontline ? new V2(-150, -12) : V2.Zero;
        player.Position = origin; player.Facing = new(1, 0);
        Send(world, player, "input", aim: origin + new V2(0, 7));
        Send(world, player, "magick", slot: 1);
        Check(Math.Abs(player.Position.X - origin.X) < 0.0001f && player.Position.Y > origin.Y + 6.5f,
            "Traslación reutilizó la orientación del tick anterior en " + map);
        Check(player.Facing == new V2(0, 1) && !player.Input.MoveTo && world.Map.Walkable(player.Position),
            "El salto no dejó orientación y posición coherentes.");
        var arrival = player.Position;
        world.Step(); Check(player.Position == arrival, "El mago continuó hacia el destino tras llegar.");
    }
    var (blocked, wizard, _) = Setup();
    wizard.Position = new(-10, -5); wizard.Facing = new(0, 1);
    Send(blocked, wizard, "input", aim: new(0, -5));
    Send(blocked, wizard, "magick", slot: 1);
    Check(wizard.Position.X is > -10 and < -7.5f && Math.Abs(wizard.Position.Y + 5) < 0.0001f && blocked.Walkable(wizard.Position),
        "Actualizar la dirección permitió saltar al otro lado de un pilar.");
});
Test("Timeout de entrada interrumpe un canal abandonado", () =>
{
    var (w, a, _) = Setup(); Send(w, a, "input"); Select(w, a, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 70; i++) w.Step();
    Check(a.Channel == null, "El jugador abandonado siguió lanzando.");
});
Test("Partida con ocho bots se mantiene válida durante un minuto simulado", () =>
{
    var world = new GameWorld(new() { Bots = 8 });
    for (int i = 0; i < 3600; i++) world.Step();
    var snapshot = world.Snapshot();
    Check(snapshot.Players.All(p => p.Position.IsFinite && p.Health >= 0 && p.Health <= 1000 && ArenaMap.Inside(p.Position)), "Estado inválido tras combate.");
    Check(snapshot.Events.Any() && snapshot.Players.Any(p => p.Kills > 0 || p.Health < 1000), "Los bots no combatieron.");
});
Test("Escudo de vida: regeneración gradual, límite, rotura y sustitución", () =>
{
    var (world, a, _) = Setup(); a.Health = 500;
    Select(world, a, Element.Shield, Element.Life); Send(world, a, "self");
    Check(a.Health == 500 && a.RegenerationRate == 38, "El escudo curó instantáneamente o no activó regeneración.");
    for (int i = 0; i < 120; i++) world.Step();
    Check(Math.Abs(a.Health - 576) < 0.03f, "La regeneración no depende del tiempo simulado.");
    var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!;
    Check(copy.Players[0].RegenerationRate == 38, "La tasa no se replica.");
    a.Shield = 0; float hp = a.Health; world.Step();
    Check(a.Health == hp && a.RegenerationRate == 0, "Se siguió curando sin escudo.");
    a.Health = 990; Select(world, a, Element.Shield, Element.Life, Element.Life); Send(world, a, "self");
    for (int i = 0; i < 60; i++) world.Step();
    Check(a.Health == 1000 && a.RegenerationRate == 76, "La curación superó el máximo o no cuenta dos vidas.");
    Select(world, a, Element.Shield); Send(world, a, "self");
    Check(a.RegenerationRate == 0, "Sustituir el escudo conservó regeneración.");
});
Test("Mina de vida: ignora enemigos y cura aliados y propietario una sola vez", () =>
{
    var (world, a, b) = Setup(GameMode.Warfare, false);
    var ally = world.AddPlayer("Aliado", 0)!; ally.Position = new(8, 0); ally.Invulnerable = 0;
    a.Position = new(-5, 0); a.Facing = new(1, 0); a.Health = 500;
    b.Position = new(-1.8f, 0); b.Health = 500;
    Select(world, a, Element.Shield, Element.Life); Send(world, a, "cast");
    Check(!world.Snapshot().Fields[0].Armed, "La mina apareció armada sin demora.");
    for (int i = 0; i < 40; i++) world.Step();
    Check(world.Snapshot().Fields.Length == 1 && b.Health == 500, "Un enemigo consumió o se benefició de la mina de vida.");
    ally.Position = new(-1.8f, 0); ally.Health = 600; a.Position = new(-2.4f, 0);
    world.Step();
    Check(world.Snapshot().Fields.Length == 0 && Math.Abs(ally.Health - 852) < 0.01f && Math.Abs(a.Health - 752) < 0.01f && b.Health == 500,
        "La mina no respetó los destinatarios de la curación.");
    float health = ally.Health; world.Step(); Check(ally.Health == health, "Una mina explotó dos veces.");
    Check(world.Snapshot().Events.Any(e => e.Kind == "mine_heal" && e.Radius == 2.4f), "Falta la señal visual con su radio real.");
});
Test("Mina ofensiva: activación por enemigo, caducidad y limpieza al desconectar", () =>
{
    var (world, a, b) = Setup(GameMode.Warfare, true);
    var ally = world.AddPlayer("Aliado", 0)!; ally.Invulnerable = 0;
    a.Position = new(-5, 0); a.Facing = new(1, 0); b.Position = new(9, 0); ally.Position = new(-1.8f, 0);
    Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
    for (int i = 0; i < 40; i++) world.Step();
    Check(world.Snapshot().Fields.Length == 1 && ally.Health == 1000, "Un aliado activó la mina ofensiva.");
    b.Position = new(-1.8f, 0); world.Step();
    Check(b.Health < 1000 && world.Snapshot().Fields.Length == 0, "El enemigo no activó la mina.");
    a.ActionCooldown = 0; b.Position = ally.Position = new(9, 0);
    Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
    for (int i = 0; i < 725; i++) world.Step();
    Check(world.Snapshot().Fields.Length == 0, "La mina no caducó.");
    a.ActionCooldown = 0; Select(world, a, Element.Shield, Element.Arcane); Send(world, a, "cast");
    world.RemovePlayer(a.Id); Check(world.Snapshot().Fields.Length == 0, "Una mina sobrevivió a su propietario desconectado.");
});
Test("Rocas: impacto directo, splash selectivo y suelo con caducidad", () =>
{
    foreach (var elements in new Element[][] { [Element.Earth], [Element.Earth, Element.Arcane], [Element.Earth, Element.Fire], [Element.Earth, Element.Water], [Element.Earth, Element.Cold] })
    {
        var (world, a, b) = Setup(); var neighbor = world.AddPlayer("Vecino")!;
        a.Position = new(-5, 0); b.Position = new(-1.5f, 0); neighbor.Position = new(-1.2f, 1.3f); neighbor.Invulnerable = 0;
        Select(world, a, elements); Send(world, a, "cast"); Run(world, a, 40); Send(world, a, "release");
        for (int i = 0; i < 35 && !world.Snapshot().Events.Any(e => e.Kind == "impact"); i++) { Send(world, a, "input"); world.Step(); }
        Check(b.Health < 1000, "La roca no golpeó al objetivo.");
        if (elements.Length == 1) Check(neighbor.Health == 1000 && world.Snapshot().Fields.Length == 0, "Tierra pura creó un splash.");
        else
        {
            Check(neighbor.Health < 1000 && world.Snapshot().Events.Any(e => e.Kind == "splash" && e.Radius > 0), "Falta el splash de una roca imbuida.");
            string? kind = new SpellRecipe(elements).GroundKind;
            Check(kind == null ? world.Snapshot().Fields.Length == 0 : world.Snapshot().Fields.Any(f => f.Kind == kind), "El suelo no corresponde a la combinación.");
        }
        for (int i = 0; i < 250; i++) world.Step();
        Check(world.Snapshot().Fields.Length == 0, "Un campo de suelo no terminó.");
        SnapshotValidation.Check(world.Snapshot());
    }
});
Test("Dos rayos frontales suman efectos y desvían la salida sin atravesar el cruce", () =>
{
    var (world, a, b) = Setup(); var target = world.AddPlayer("Salida")!;
    a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4); target.Invulnerable = 0;
    Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
    Select(world, a, Element.Arcane); Select(world, b, Element.Arcane); Send(world, a, "cast"); Send(world, b, "cast");
    for (int i = 0; i < 30; i++) { Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step(); }
    var merged = world.Snapshot().Beams.Single(ray => ray.Interaction == "merged");
    Check(merged.Elements.SequenceEqual(new[] { Element.Arcane, Element.Arcane }) && Math.Abs(merged.Power - 1.2f) < 0.0001f && merged.To.Y > merged.From.Y,
        "No se sumaron los componentes o no se desvió el rayo.");
    Check(a.Health == 1000 && b.Health == 1000 && Math.Abs(target.Health - 947.675f) < 0.05f, "La salida no suma ambos daños o las entradas atraviesan el cruce.");
    Check(world.Snapshot().Events.Any(e => e.Kind == "beam_merge"), "No se replicó el destello del cruce.");
    SnapshotValidation.Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!);
});
Test("Láser: el daño aumenta con el tiempo, alcanza el 150 % en dos segundos y se mantiene", () =>
{
    var (w, a, b) = Setup(); b.MaxHealth = b.Health = 10000;
    Select(w, a, Element.Arcane); Send(w, a, "cast");
    Run(w, a, 1);
    Check(Math.Abs(10000 - b.Health - 115 * 0.31f / 60) < 0.002f, "El primer paso no empieza con daño bajo.");
    Check(Math.Abs(w.Snapshot().Beams.Single().Power - 0.31f) < 0.0001f, "La intensidad inicial no se replica.");
    Run(w, a, 59);
    float before = b.Health; Run(w, a, 1);
    Check(Math.Abs(before - b.Health - 115 * 0.91f / 60) < 0.002f && Math.Abs(a.BeamCharge - 61f / 120) < 0.0001f,
        "El daño o la barra de carga no progresan a mitad del lanzamiento.");
    Run(w, a, 59);
    var full = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!;
    Check(full.Players.Single(p => p.Id == a.Id).Charge == 1 && Math.Abs(full.Beams.Single().Power - 1.5f) < 0.0001f,
        "La carga completa no llega a la red a los dos segundos.");
    before = b.Health; Run(w, a, 180);
    Check(a.BeamTicks == 120 && Math.Abs(before - b.Health - 172.5f * 3) < 0.15f,
        "El láser supera el límite o no mantiene 172,5 daño/s a plena carga.");
    SnapshotValidation.Check(full);
});
Test("Láser: soltar, sustituir, autocast, timeout, congelación y muerte reinician la carga", () =>
{
    foreach (string cause in new[] { "release", "recast", "replace", "self", "timeout", "freeze", "death" })
    {
        var (w, a, b) = Setup(); b.Position = new(18, 8);
        Select(w, a, Element.Arcane); Send(w, a, "cast"); Run(w, a, 120);
        Check(a.BeamCharge == 1, "No llegó a carga completa antes de " + cause);
        switch (cause)
        {
            case "release": Send(w, a, "release"); break;
            case "recast": Select(w, a, Element.Arcane); Send(w, a, "cast"); break;
            case "replace": Select(w, a, Element.Earth); Send(w, a, "cast"); break;
            case "self": Select(w, a, Element.Life); Send(w, a, "self"); break;
            case "timeout": for (int i = 0; i < 40; i++) w.Step(); break;
            case "freeze": a.Frozen = 1; Run(w, a, 1); break;
            case "death":
                a.Health = 1; b.Position = new(1, 0); Select(w, b, Element.Arcane); Send(w, b, "nova"); break;
        }
        Check(a.BeamTicks == 0 && w.Snapshot().Players.Single(p => p.Id == a.Id).Charge == 0,
            "La carga se conservó tras " + cause);
        if (cause == "death") for (int i = 0; i < 245; i++) w.Step();
        a.Frozen = 0; a.ActionCooldown = 0;
        Select(w, a, Element.Arcane); Send(w, a, "cast"); Run(w, a, 1);
        Check(Math.Abs(w.Snapshot().Beams.Single(ray => ray.Owner == a.Id && ray.Interaction == "direct").Power - 0.31f) < 0.0001f,
            "El siguiente láser heredó la potencia tras " + cause);
    }
});
Test("Láseres cruzados conservan el tiempo individual y los reflejados conservan la potencia", () =>
{
    var (w, a, b) = Setup(); var target = w.AddPlayer("Salida")!;
    a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4); target.Invulnerable = 0;
    Send(w, a, "input", aim: b.Position); w.Step(); Select(w, a, Element.Arcane); Send(w, a, "cast");
    for (int i = 0; i < 119; i++) { Send(w, a, "input", aim: b.Position); w.Step(); }
    Send(w, b, "input", aim: a.Position); w.Step(); Select(w, b, Element.Arcane); Send(w, b, "cast");
    float before = target.Health;
    Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
    Check(Math.Abs(w.Snapshot().Beams.Single(ray => ray.Interaction == "merged").Power - 1.81f) < 0.0001f &&
        Math.Abs(before - target.Health - 115 * 1.81f / 60) < 0.001f, "El cruce igualó la carga de los dos magos.");
    var (shielded, caster, defender) = Setup(); defender.Position = new(18, 8);
    Select(shielded, caster, Element.Arcane); Send(shielded, caster, "cast"); Run(shielded, caster, 120);
    defender.Position = new(2, 0); Select(shielded, defender, Element.Shield); Send(shielded, defender, "self");
    float health = caster.Health, shield = defender.Shield; Run(shielded, caster, 1);
    Check(Math.Abs(shielded.Snapshot().Beams.Single(ray => ray.Interaction == "reflected").Power - 1.2f) < 0.0001f &&
        Math.Abs(health - caster.Health - 115 * 1.2f / 60) < 0.001f &&
        Math.Abs(shield - defender.Shield - 115 * 1.5f * 0.5f * 0.2f / 60) < 0.001f,
        "La reflexión reinició la carga o calculó mal daño y presión.");
});
Test("La carga del láser aumenta solo el daño: curación y frío conservan su tasa", () =>
{
    foreach (var elements in new[] { new[] { Element.Life, Element.Fire }, new[] { Element.Arcane, Element.Cold } })
    {
        var (w, a, b) = Setup(); b.Position = new(18, 8);
        Select(w, a, elements); Send(w, a, "cast"); Run(w, a, 120);
        b.Position = new(2, 0); b.Health = 500; b.Chill = 0; Run(w, a, 1);
        if (elements.Contains(Element.Life))
            Check(Math.Abs(b.Health - (500 + (140 - 58 * 1.5f) / 60)) < 0.001f,
                "La carga multiplicó también la curación o la quemadura inmediata.");
        else Check(Math.Abs(b.Chill - 38f / 60) < 0.001f, "El frío se aplica más rápido por la carga del láser.");
    }
});
Test("Rayos y escudos: reflexión, refracción elemental y rebotes acotados", () =>
{
    foreach (bool elemental in new[] { false, true })
    {
        var (world, a, b) = Setup(); Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
        Select(world, b, elemental ? [Element.Shield, Element.Fire] : [Element.Shield]); Send(world, b, "self");
        Select(world, a, Element.Arcane, Element.Arcane); Send(world, a, "cast");
        for (int i = 0; i < 20; i++) { Send(world, a, "input", aim: b.Position); world.Step(); }
        var diverted = world.Snapshot().Beams.FirstOrDefault(ray => ray.Interaction == (elemental ? "refracted" : "reflected"));
        Check(diverted != null && b.Shield < b.ShieldMax && b.Health == 1000, "El escudo no desvió o no consumió energía.");
        if (elemental) Check(diverted!.Elements.Contains(Element.Fire) && Math.Abs(diverted.To.Y - diverted.From.Y) > 0.2f, "La guarda no añadió su elemento y ángulo.");
        else Check(a.Health < 1000, "La reflexión no puede alcanzar al lanzador.");
        Check(world.Snapshot().Beams.Length <= 3, "El rayo produjo una cadena de rebotes sin límite.");
        SnapshotValidation.Check(world.Snapshot());
    }
});
Test("Una barrera impide combinar rayos al otro lado", () =>
{
    var (world, a, b) = Setup(); a.Position = new(-5, 0); b.Position = new(5, 0);
    Send(world, b, "input", aim: a.Position); world.Step(); Select(world, b, Element.Shield, Element.Earth); Send(world, b, "cast");
    for (int i = 0; i < 45; i++) world.Step();
    Send(world, a, "input", aim: b.Position); Send(world, b, "input", aim: a.Position); world.Step();
    Select(world, a, Element.Arcane); Select(world, b, Element.Arcane); Send(world, a, "cast"); Send(world, b, "cast"); world.Step();
    Check(!world.Snapshot().Beams.Any(ray => ray.Interaction == "merged"), "Los rayos se combinaron a través de la barrera.");
});
Test("Captura: progreso firmado, disputa, neutralización y replicación", () =>
{
    var (world, a, b) = Setup(GameMode.Warfare); a.Position = V2.Zero; b.Position = new(18, 8);
    for (int i = 0; i < 60; i++) world.Step();
    var point = world.Snapshot().Points[1]; Check(Math.Abs(point.Progress + 0.24f) < 0.001f && point.Owner == -1, "No hay captura azul gradual.");
    b.Position = new(0, 1); for (int i = 0; i < 60; i++) world.Step();
    point = world.Snapshot().Points[1]; Check(point.Contested && Math.Abs(point.Progress + 0.24f) < 0.001f, "La disputa no bloqueó el progreso.");
    Check(Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(world.Snapshot()))!.Points[1].Contested, "No se replicó la disputa.");
    b.Position = new(18, 8); for (int i = 0; i < 200; i++) world.Step();
    Check(world.Snapshot().Points[1].Owner == 0, "No terminó la captura azul.");
    a.Position = new(-5, 0); b.Position = V2.Zero; for (int i = 0; i < 360; i++) world.Step();
    point = world.Snapshot().Points[1]; Check(point.Owner == -1 && point.Progress > 0 && point.Progress < 1 && !point.Contested, "El rojo no neutralizó primero el progreso azul.");
});
Test("Catálogo: todas las combinaciones se demuestran en sus tres modos y repiten", () =>
{
    Check(SpellCatalogue.All.Select(c => c.Keys).Distinct().Count() == SpellCatalogue.All.Length && SpellCatalogue.FromKeys("EW")!.Recipe.Healing,
        "El catálogo tiene duplicados o depende del orden de las teclas.");
    foreach (var combination in SpellCatalogue.All)
    foreach (var mode in Enum.GetValues<CastMode>())
    {
        var selection = new ElementBuffer(); foreach (var e in combination.Elements) selection.Add(e);
        Check(selection.Elements.SequenceEqual(combination.Elements), "El catálogo contiene una selección anulada.");
        var demo = new SpellDemonstration(combination.Elements, mode);
        for (int i = 0; i < 361; i++) { demo.Step(); if (i % 60 == 0) SnapshotValidation.Check(demo.World.Snapshot()); }
        Check(demo.Cycle == 2 && SpellCatalogue.Describe(combination.Recipe, mode).Length > 10, "Una combinación no repite o carece de explicación.");
    }
    foreach (var kind in new[] { DemonstrationKind.CrossedBeams, DemonstrationKind.ShieldRefraction, DemonstrationKind.Capture })
    {
        var demo = new SpellDemonstration([Element.Arcane], kind: kind);
        for (int i = 0; i < 300; i++) demo.Step();
        SnapshotValidation.Check(demo.World.Snapshot());
    }
    Console.WriteLine($"CATALOGO: {SpellCatalogue.All.Length} combinaciones, {SpellCatalogue.All.Length * 3} demostraciones y 3 interacciones.");
});

Test("F1: cifras de las 120 combinaciones coinciden con autocast y nova en combate", () =>
{
    foreach (var combination in SpellCatalogue.All)
    foreach (var mode in Enum.GetValues<CastMode>())
    {
        var spell = combination.Recipe; var n = SpellBalance.Numbers(spell, mode);
        Check(float.IsFinite(n.Minimum) && n.Minimum >= 0 && n.Maximum >= n.Minimum &&
            n.HealingMinimum >= 0 && n.HealingMaximum >= n.HealingMinimum && SpellBalance.Format(spell, mode).StartsWith("DAÑO BASE:"),
            "Faltan cifras para " + combination.Keys + "/" + mode);
        if (mode == CastMode.Forward || spell.Has(Element.Shield)) continue;
        var (w, a, b) = Setup(); b.Position = new(1, 0); var target = mode == CastMode.Self ? a : b;
        target.Health = 1000; target.MaxHealth = 2000;
        Select(w, a, combination.Elements); Send(w, a, mode == CastMode.Self ? "self" : "nova");
        float expected = 1000 + n.HealingMinimum - n.Minimum;
        Check(Math.Abs(target.Health - expected) < 0.02f, "F1 difiere del combate en " + combination.Keys + "/" + mode + ": " + target.Health + " vs " + expected);
    }
});
Test("F1: daño por segundo, carga, hielo, minas, mojado, quemadura y curación tienen unidades y cifras reales", () =>
{
    SpellNumbers N(string keys, CastMode mode = CastMode.Forward) => SpellBalance.Numbers(SpellCatalogue.FromKeys(keys)!.Recipe, mode);
    Check(Math.Abs(N("SSS").Minimum - 103.5f) < 0.001f && N("SSS").Maximum == 517.5f &&
        N("SSS").Unit == "daño/s" && N("SSS").Extra.Contains("150 %"), "Rayo arcano incorrecto.");
    Check(N("FFF").Maximum == 174 && N("FFF").BurnPerSecond == 28, "Fuego o quemadura incorrectos.");
    Check(N("AAA").Maximum == 300 && N("AAA").WetMaximum == 570, "El catálogo perdió el bonus contra mojado.");
    Check(N("DD").Minimum == 220 && N("DD").Maximum == 500 && N("DD").Unit == "por impacto", "El rango de carga es incorrecto.");
    Check(Math.Abs(N("QR").Maximum - 40.8f) < 0.01f && N("QR").Extra.Contains("4 fragmentos"), "Los fragmentos no tienen daño individual.");
    Check(Math.Abs(N("ESF").Maximum - 311.4f) < 0.01f && N("ESF").Unit == "por mina", "La mina ofensiva no tiene daño individual.");
    Check(N("WWW").HealingMaximum == 420 && N("WWW", CastMode.Self).HealingMaximum == 435 && N("EWW", CastMode.Self).HealingMaximum == 76,
        "Curación y regeneración incorrectas.");
    foreach (var keys in new[] { "SSS", "FFF", "AAA", "WWW" })
    {
        var (w, a, b) = Setup(); b.Position = new(1, 0); b.Health = 500; b.MaxHealth = 2000;
        Select(w, a, SpellCatalogue.FromKeys(keys)!.Elements); Send(w, a, "input", aim: b.Position); Send(w, a, "cast"); w.Step();
        var n = N(keys);
        float firstDamage = keys == "SSS" ? 345 * 0.31f : n.Minimum;
        Check(Math.Abs(b.Health - (500 + (n.HealingMinimum - firstDamage) / 60)) < 0.02f, "Daño/s de F1 difiere en " + keys);
    }
});
Test("F1: todas las combinaciones de láser tienen el daño máximo real, en seco y contra mojado", () =>
{
    foreach (var combination in SpellCatalogue.All.Where(c => c.Recipe.Shape == SpellShape.Beam && SpellBalance.Damage(c.Recipe) > 0))
    foreach (bool wet in new[] { false, true })
    {
        var (w, a, b) = Setup(); b.Position = new(18, 8);
        Select(w, a, combination.Elements); Send(w, a, "cast"); Run(w, a, 120);
        b.Position = new(2, 0); b.MaxHealth = 10000; b.Health = 5000; b.Wet = wet ? 6 : 0;
        var n = SpellBalance.Numbers(combination.Recipe, CastMode.Forward);
        Run(w, a, 1);
        float expected = 5000 + (n.HealingMaximum - (wet ? n.WetMaximum : n.Maximum)) / 60;
        Check(Math.Abs(b.Health - expected) < 0.002f, "F1 y daño máximo difieren en " + combination.Keys + ", mojado=" + wet);
    }
});
Test("Elementos preparados: selección, carga y canal se replican y sus arrays son independientes", () =>
{
    var (w, a, _) = Setup(); Select(w, a, Element.Earth, Element.Earth, Element.Fire);
    Check(w.Snapshot().Players[0].DisplayElements.SequenceEqual(new[] { Element.Earth, Element.Earth, Element.Fire }), "No se ven los tres elementos preparados.");
    Send(w, a, "input"); Send(w, a, "cast"); var charging = w.Snapshot().Players[0];
    Check(charging.Selected.Length == 0 && charging.Casting && charging.DisplayElements.SequenceEqual(new[] { Element.Earth, Element.Earth, Element.Fire }),
        "Consumir la selección ocultó la roca mientras se carga.");
    charging.CastingElements![0] = Element.Life;
    Check(w.Snapshot().Players[0].CastingElements![0] == Element.Earth, "El snapshot modificó la roca en carga.");
    Run(w, a, 10); Send(w, a, "release");
    Check(w.Snapshot().Players[0].DisplayElements.Length == 0, "Soltar conservó elementos de la carga.");
    a.ActionCooldown = 0; Select(w, a, Element.Arcane, Element.Arcane); Send(w, a, "cast");
    var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(w.Snapshot()))!; SnapshotValidation.Check(copy);
    Check(copy.Players[0].DisplayElements.SequenceEqual(new[] { Element.Arcane, Element.Arcane }), "La red perdió la combinación del canal.");
    Send(w, a, "release"); Select(w, a, Element.Fire); Select(w, a, Element.Cold);
    Check(w.Snapshot().Players[0].DisplayElements.Length == 0, "La cancelación no vació las casillas.");
});

Test("Cancelar una carga por falta de entrada, congelación, otro hechizo o muerte vacía su estado", () =>
{
    foreach (string cause in new[] { "timeout", "freeze", "replace", "death" })
    {
        var (w, a, b) = Setup();
        Select(w, a, Element.Earth); Send(w, a, "cast"); Run(w, a, 12);
        Check(a.ChargeTime > 0, "La roca no se estaba cargando.");
        if (cause == "timeout") for (int i = 0; i < 40; i++) w.Step();
        if (cause == "freeze") { a.Frozen = 1; Run(w, a, 1); }
        if (cause == "replace") { Select(w, a, Element.Arcane); Send(w, a, "cast"); }
        if (cause == "death")
        {
            a.Health = 1; b.Position = new(1, 0);
            Select(w, b, Element.Arcane); Send(w, b, "nova");
        }
        var state = w.Snapshot().Players.Single(p => p.Id == a.Id);
        Check(a.Charging == null && a.ChargeTime == 0 && state.Charge == 0, "La carga sobrevivió a " + cause);
        if (cause == "death")
        {
            for (int i = 0; i < 245; i++) w.Step();
            Check(a.Alive && w.Snapshot().Players.Single(p => p.Id == a.Id).Charge == 0, "La carga reapareció con el mago.");
        }
    }
});
Test("Una descarga de hielo crea todos los fragmentos y un único evento de lanzamiento", () =>
{
    var (w, a, _) = Setup(); Select(w, a, Element.Water, Element.Cold, Element.Cold); Send(w, a, "cast");
    var state = w.Snapshot();
    Check(state.Projectiles.Length == 5, "Cambió el número de fragmentos.");
    Check(state.Events.Count(e => e.Kind == "cast") == 1, "Cada fragmento duplicó el sonido y los efectos del lanzamiento.");
});
Test("El empuje no permite saltar pilares ni muros entre dos posiciones transitables", () =>
{
    var (w, a, _) = Setup(); a.Position = new(10, 0); a.Knockback = new(360, 0); Run(w, a, 1);
    Check(a.Position.X < 13 - 1.3f - ArenaMap.WizardRadius, "El empuje atravesó un pilar.");
    var (walls, caster, _) = Setup(); Select(walls, caster, Element.Shield, Element.Earth); Send(walls, caster, "cast");
    Check(walls.Snapshot().Walls.Length == 1, "Falta el muro de prueba.");
    caster.Knockback = new(360, 0); Run(walls, caster, 1);
    Check(caster.Position.X < 1.2f - ArenaMap.WizardRadius - 0.18f, "El empuje atravesó un muro.");
});
Test("La roca impacta exactamente en la superficie de un muro", () =>
{
    var (w, a, b) = Setup(); b.Position = new(8, 8);
    Select(w, a, Element.Shield, Element.Fire); Send(w, a, "cast"); Run(w, a, 43);
    Select(w, a, Element.Earth); Send(w, a, "cast"); Run(w, a, 40); Send(w, a, "release");
    float radius = w.Snapshot().Projectiles.Single().Radius;
    for (int i = 0; i < 20 && !w.Snapshot().Events.Any(e => e.Kind == "impact"); i++) Run(w, a, 1);
    var impact = w.Snapshot().Events.Single(e => e.Kind == "impact");
    Check(Math.Abs(impact.Position.X - (1.2f - radius - 0.2f)) < 0.001f, "La colisión depende del muestreo y penetra en el muro.");
});
Test("Un muro destruido o caducado deja de aplicar efectos de contacto en ese paso", () =>
{
    foreach (bool destroyed in new[] { true, false })
    {
        var (w, a, b) = Setup(); b.Position = new(8, 8);
        Select(w, a, Element.Shield, Element.Fire); Send(w, a, "cast"); Run(w, a, 43);
        if (destroyed) { Select(w, a, Element.Water, Element.Water, Element.Water); Send(w, a, "cast"); }
        float pressure = 110 * 3 * 1.5f * GameWorld.StepSeconds;
        for (int i = 0; i < 450; i++)
        {
            var wall = w.Snapshot().Walls.Single();
            if (destroyed ? wall.Health <= pressure : wall.Lifetime <= GameWorld.StepSeconds) break;
            Run(w, a, 1);
        }
        b.Position = new(1.2f, 2.95f); b.Invulnerable = 0;
        Run(w, a, 1);
        Check(w.Snapshot().Walls.Length == 0, "El muro no terminó en el paso esperado.");
        Check(b.Health == 1000 && b.Burning == 0, "Un muro que ya terminó todavía quemó al mago.");
    }
});
Test("Cada componente de un rayo combinado conserva el bonus de daño de su autor contra un escudo", () =>
{
    foreach (bool firstBoosted in new[] { true, false })
    {
        var w = new GameWorld(new() { Mode = GameMode.Arena, Map = ArenaMapId.Relics });
        var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; var target = w.AddPlayer("Escudo")!;
        a.Position = new(-5, 0); b.Position = new(5, 0); target.Position = new(0, 4);
        a.Invulnerable = b.Invulnerable = target.Invulnerable = 0;
        (firstBoosted ? a : b).BuffStacks[(int)BuffKind.Power] = 3;
        Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
        target.Shield = target.ShieldMax = 420; target.ShieldTime = 12;
        Select(w, a, Element.Arcane, Element.Arcane); Select(w, b, Element.Arcane, Element.Arcane);
        Send(w, a, "cast"); Send(w, b, "cast");
        Send(w, a, "input", aim: b.Position); Send(w, b, "input", aim: a.Position); w.Step();
        float expected = 230 * (1 + RelicBuffs.Power(firstBoosted ? a : b)) * 0.31f * 0.5f * 0.2f * GameWorld.StepSeconds;
        Check(Math.Abs(420 - target.Shield - expected) < 0.001f, "El bonus del primer mago se aplicó a ambos rayos.");
    }
});
Test("La reaparición encuentra suelo libre aunque los cuatro puestos estén cubiertos por muros", () =>
{
    var (w, a, b) = Setup(GameMode.Warfare);
    a.Health = 0; a.Respawn = 10;
    foreach (float y in new[] { -2.2f, 2.2f })
    {
        b.Position = new(-21.2f, y); b.Facing = new(1, 0); b.ActionCooldown = 0;
        Select(w, b, Element.Shield, Element.Earth); Send(w, b, "cast");
    }
    Check(w.Snapshot().Walls.Length == 2 && Enumerable.Range(0, 4).All(i => !w.Walkable(w.Map.Spawn(a.Team, i))), "La prueba requiere cuatro puestos bloqueados.");
    a.Respawn = GameWorld.StepSeconds / 2; w.Step();
    Check(a.Alive && w.Walkable(a.Position), "El mago reapareció dentro de un muro y no puede moverse.");
});
Test("Todos contra todos reparte entradas y reapariciones por los cuatro mapas", () =>
{
    foreach (var map in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice && ArenaMap.Get(m).SupportsMode(GameMode.Arena)))
    {
        var world = new GameWorld(new() { Mode = GameMode.Arena, Map = map, MaxPlayers = 16, SpawnSeed = 902, PickupSeed = 1 });
        var players = Enumerable.Range(0, 16).Select(i => world.AddPlayer("Mago " + i, i % 2)!).ToArray();
        Check(players.All(p => world.Walkable(p.Position) && p.Invulnerable == 1), "Entrada bloqueada o sin protección en " + map);
        foreach (var a in players) foreach (var b in players)
            if (a.Id != b.Id) Check(V2.Distance(a.Position, b.Position) >= 3, "Dos jugadores entraron juntos en " + map);
        Check(players.Any(p => Math.Abs(p.Position.X) < world.Map.HalfWidth * 0.5f), "Las entradas siguen confinadas a las bases.");
        var respawns = new List<V2>(); var player = players[0];
        for (int i = 0; i < 48; i++)
        {
            var before = player.Position; player.Health = 0; player.Deaths++; player.Respawn = GameWorld.StepSeconds / 2;
            world.Step(); respawns.Add(player.Position);
            Check(player.Alive && world.Walkable(player.Position) && player.Invulnerable == 1 && V2.Distance(before, player.Position) >= 4,
                "Reaparición inválida en " + map);
            Check(players.Where(p => p.Id != player.Id).All(p => V2.Distance(p.Position, player.Position) >= 3), "Reaparición sobre un rival.");
        }
        Check(respawns.Any(p => p.X < 0) && respawns.Any(p => p.X > 0) && respawns.Any(p => p.Y < 0) && respawns.Any(p => p.Y > 0),
            "El equipo limita la posición aleatoria en " + map);
    }
});
Test("La semilla reproduce apariciones sin cambiar las bases de los modos por equipos", () =>
{
    var first = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 91 });
    var second = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 91 });
    var different = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 92 });
    for (int i = 0; i < 8; i++)
        Check(first.AddPlayer("Mago")!.Position == second.AddPlayer("Mago")!.Position, "La semilla no reproduce las entradas.");
    Check(first.Players[1].Position != different.AddPlayer("Mago")!.Position, "Cambiar de semilla no cambia la aparición.");
    var guarded = new GameWorld(new() { Mode = GameMode.Arena, SpawnSeed = 72 });
    var caster = guarded.AddPlayer("Mago")!; var returning = guarded.AddPlayer("Rival")!;
    caster.Position = new(-2, 0); returning.Position = new(20, 10); caster.Invulnerable = 0;
    Select(guarded, caster, Element.Shield, Element.Earth); Send(guarded, caster, "cast");
    caster.ActionCooldown = 0; Send(guarded, caster, "input", aim: new(8, 4)); guarded.Step(); Send(guarded, caster, "magick", slot: 2);
    var danger = guarded.Snapshot().Fields.Single();
    Check(guarded.Snapshot().Walls.Length == 1 && danger.Kind == "meteor", "Faltan los peligros de la prueba.");
    for (int i = 0; i < 30; i++)
    {
        returning.Health = 0; returning.Deaths++; returning.Respawn = GameWorld.StepSeconds / 2; guarded.Step();
        Check(guarded.Walkable(returning.Position) && V2.Distance(returning.Position, danger.Position) >= danger.Radius + ArenaMap.WizardRadius,
            "La reaparición ignora un muro o el área de un meteorito.");
    }
    foreach (var mode in new[] { GameMode.Duel, GameMode.Warfare })
    {
        var world = new GameWorld(new() { Mode = mode });
        var blue = world.AddPlayer("Azul", 0)!; var red = world.AddPlayer("Rojo", 1)!;
        Check(blue.Position == world.Map.Spawn(0, 0) && red.Position == world.Map.Spawn(1, 0), "Cambió una base de " + mode);
        blue.Health = 0; blue.Respawn = GameWorld.StepSeconds / 2; world.Step();
        Check(blue.Position == world.Map.Spawn(0, blue.Id % 4) && blue.Invulnerable == 1.2f, "Cambió la reaparición de " + mode);
    }
});
Test("La protección de Arena bloquea daño y estados y caduca exactamente al segundo", () =>
{
    var (w, a, b) = Setup(); b.Invulnerable = 1; b.Position = new(1, 0);
    Select(w, a, Element.Fire, Element.Fire, Element.Fire); Send(w, a, "cast");
    Run(w, a, 59);
    Check(b.Health == b.MaxHealth && b.Burning == 0 && b.Invulnerable > 0, "La protección terminó antes de un segundo.");
    Run(w, a, 1);
    Check(b.Invulnerable == 0 && b.Health < b.MaxHealth && b.Burning > 0, "La protección dura más de un segundo.");
    Send(w, a, "release"); b.Health = 0; b.Respawn = GameWorld.StepSeconds / 2; b.Deaths++;
    w.Step();
    Check(b.Alive && b.Invulnerable == 1 && w.Snapshot().Players.Single(p => p.Id == b.Id).InvulnerableSeconds == 1,
        "La reaparición no replica un segundo de protección.");
    a.Position = b.Position + new V2(-1, 0); a.ActionCooldown = 0;
    Select(w, a, Element.Water, Element.Cold, Element.Cold); Send(w, a, "nova");
    Check(b.Health == b.MaxHealth && b.Wet == 0 && b.Chill == 0 && b.Frozen == 0 && b.Knockback == V2.Zero,
        "El escudo inmortal permite estados o empujes.");
    Run(w, a, 60);
    Check(b.Invulnerable == 0 && w.Snapshot().Players.Single(p => p.Id == b.Id).InvulnerableSeconds == 0, "El escudo no desaparece tras 60 pasos.");
});
Test("La intensidad del escudo conserva elementos repetidos y la red valida la protección", () =>
{
    var (w, a, _) = Setup();
    Select(w, a, Element.Shield, Element.Earth, Element.Earth); Send(w, a, "self");
    var snapshot = w.Snapshot(); var player = snapshot.Players.Single(p => p.Id == a.Id);
    Check(player.ShieldCharges == 3 && player.Ward.SequenceEqual(new[] { Element.Earth }) && player.ShieldMax == 650,
        "El escudo perdió las cargas repetidas o cambió su capacidad.");
    var copy = Protocol.Deserialize<WorldSnapshot>(Protocol.Serialize(snapshot))!; SnapshotValidation.Check(copy);
    Check(copy.Players.Single(p => p.Id == a.Id).ShieldCharges == 3, "La red perdió la intensidad del escudo.");
    var legacy = System.Text.Json.Nodes.JsonNode.Parse(Protocol.Serialize(snapshot))!;
    foreach (var oldPlayer in legacy["players"]!.AsArray())
    {
        oldPlayer!.AsObject().Remove("shieldCharges"); oldPlayer.AsObject().Remove("invulnerableSeconds");
    }
    var previousVersion = Protocol.Deserialize<WorldSnapshot>(legacy.ToJsonString())!; SnapshotValidation.Check(previousVersion);
    Check(previousVersion.Players.All(p => p.ShieldCharges == 1 && p.InvulnerableSeconds == 0), "Los campos opcionales rompen el protocolo anterior.");
    foreach (var invalid in new[] { player with { ShieldCharges = 0 }, player with { ShieldCharges = 4 },
        player with { InvulnerableSeconds = float.NaN }, player with { InvulnerableSeconds = 1.21f } })
    {
        bool rejected = false;
        try { SnapshotValidation.Check(snapshot with { Players = snapshot.Players.Select(p => p.Id == a.Id ? invalid : p).ToArray() }); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Se aceptó protección o intensidad inválida.");
    }
});
Test("Reanimación evita devolver al aliado al interior de un muro creado tras su muerte", () =>
{
    var w = new GameWorld(new() { Mode = GameMode.Warfare });
    var a = w.AddPlayer("Reanimador", 0)!; w.AddPlayer("Rojo", 1); var ally = w.AddPlayer("Aliado", 0)!;
    a.Position = new(-2, 0); ally.Position = new(1.2f, 0); ally.Health = 0; ally.Respawn = 10;
    Select(w, a, Element.Shield, Element.Earth); Send(w, a, "cast"); Run(w, a, 43);
    Check(!w.Walkable(ally.Position), "La prueba requiere un muro encima del aliado caído.");
    Send(w, a, "magick", slot: 3);
    Check(ally.Alive && ally.Health == 600 && w.Walkable(ally.Position), "El aliado reanimado quedó atrapado dentro del muro.");
});

Test("Direcciones IP, dominio, IPv6 y WSS", () =>
{
    Check(ArenaConnection.Address("192.168.1.10").Port == 27015, "Puerto por defecto incorrecto.");
    Check(ArenaConnection.Address("127.0.0.1:29015").AbsolutePath == "/play", "Ruta por defecto incorrecta.");
    Check(ArenaConnection.Address("::1").Port == 27015, "IPv6 inválida.");
    Check(ArenaConnection.Address("wss://juego.example/play").Scheme == "wss", "No se acepta WSS.");
    Check(RoomBrowser.Endpoint("wss://juego.example/arcana/play", "rooms").ToString() == "https://juego.example/arcana/rooms", "No se conservó el prefijo del proxy para salas.");
});


Test("Una intención inválida no envenena la secuencia y no se conserva su referencia", () =>
{
    var (w, a, _) = Setup();
    Check(!w.ApplyMessage(a.Id, new() { Type = "input", Sequence = long.MaxValue, MoveX = float.NaN }), "NaN aceptado.");
    Check(!w.ApplyMessage(a.Id, new() { Type = "element", Sequence = long.MaxValue, Element = (Element)999 }), "Elemento desconocido aceptado.");
    var message = new ClientMessage { Type = "input", Sequence = 1, MoveX = 1, AimX = 4 };
    Check(w.ApplyMessage(a.Id, message), "La secuencia quedó bloqueada por un mensaje inválido.");
    message.MoveX = -1; message.AimX = float.NaN;
    var before = a.Position; w.Step();
    Check(a.Position.X > before.X && a.Facing.IsFinite, "Modificar el mensaje alteró una entrada ya aceptada.");
});
Test("Las recetas de proyectiles, muros, campos y eventos del snapshot son copias", () =>
{
    var (w, a, _) = Setup();
    Select(w, a, Element.Earth); Send(w, a, "cast"); Run(w, a, 10); Send(w, a, "release");
    var snapshot = w.Snapshot(); Check(snapshot.Projectiles.Length == 1, "Falta la roca de prueba.");
    snapshot.Projectiles[0].Elements[0] = Element.Life;
    foreach (var e in snapshot.Events) if (e.Elements.Length > 0) e.Elements[0] = Element.Life;
    Check(w.Snapshot().Projectiles[0].Elements[0] == Element.Earth && !w.Snapshot().Events.Any(e => e.Elements.Contains(Element.Life)), "Una copia alteró el mundo.");
    var (walls, b, _) = Setup(); Select(walls, b, Element.Shield, Element.Earth); Send(walls, b, "cast");
    var wallSnapshot = walls.Snapshot(); Check(wallSnapshot.Walls.Length == 1, "Falta el muro de prueba.");
    wallSnapshot.Walls[0].Elements[0] = Element.Life;
    Check(!walls.Snapshot().Walls[0].Elements.Contains(Element.Life), "El snapshot modificó un muro.");
    var (fields, c, _) = Setup(); Send(fields, c, "magick", slot: 2);
    var fieldSnapshot = fields.Snapshot(); Check(fieldSnapshot.Fields.Length == 1, "Falta el meteoro de prueba.");
    fieldSnapshot.Fields[0].Elements[0] = Element.Life;
    Check(!fields.Snapshot().Fields[0].Elements.Contains(Element.Life), "El snapshot modificó un campo.");
});
Test("Validación de snapshots acepta combates y rechaza estado parcial o no finito", () =>
{
    var world = new GameWorld(new() { Bots = 8 });
    for (int i = 0; i < 1800; i++) { world.Step(); if (i % 30 == 0) SnapshotValidation.Check(world.Snapshot()); }
    var snapshot = world.Snapshot();
    foreach (var invalid in new[] { snapshot with { Scores = [1] }, snapshot with { Players = null! },
        snapshot with { Points = [] }, snapshot with { Players = [snapshot.Players[0] with { Position = new(float.NaN, 0) }] },
        snapshot with { Players = [snapshot.Players[0] with { Cooldowns = [0] }] } })
    {
        bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Se aceptó un estado que rompe el renderer.");
    }
});
Test("Snapshots: límites espaciales antes del renderer y combate válido en los cinco mapas", () =>
{
    foreach (var id in Enum.GetValues<ArenaMapId>().Where(m => !ArenaMap.Get(m).IsPractice && m != ArenaMapId.Bastion))
    {
        var world = new GameWorld(new() { Map = id, Mode = ArenaMap.Get(id).SequentialCapture ? GameMode.Warfare : GameMode.Arena,
            MaxPlayers = 16, Bots = 16, Seed = 41, SpawnSeed = 27, PickupSeed = 31, EnvironmentSeed = 21 });
        for (int tick = 0; tick < 900; tick++)
        {
            world.Step();
            if (tick % 30 == 0)
            {
                try { SnapshotValidation.Check(world.Snapshot()); }
                catch (InvalidDataException e) { throw new Exception($"{id}, tick {tick}: {e.Message}"); }
            }
        }
        var state = world.Snapshot(); var map = world.Map; var player = state.Players[0];
        var huge = new V2(float.MaxValue, float.MaxValue);
        foreach (var invalid in new[]
        {
            state with { Players = [player with { Position = huge }] },
            state with { Players = [player with { Facing = new(2, 0) }] },
            state with { Projectiles = [new(10001, player.Id, huge, new(1, 0), [Element.Earth], 0.4f)] },
            state with { Projectiles = [new(10001, player.Id, V2.Zero, huge, [Element.Earth], 0.4f)] },
            state with { Projectiles = [new(10001, player.Id, V2.Zero, new(1, 0), [Element.Earth], 100)] },
            state with { Walls = [new(10001, player.Id, V2.Zero, new(0, 6), 650, 7, [Element.Shield])] },
            state with { Walls = [new(10001, player.Id, V2.Zero, huge, 650, 7, [Element.Shield])] },
            state with { Fields = [new(10001, player.Id, V2.Zero, 100, 1, [Element.Fire], "meteor")] },
            state with { Fields = [new(10001, player.Id, huge, 4, 1, [Element.Fire], "meteor")] },
            state with { Fields = [new(10001, player.Id, V2.Zero, 4, 1, [Element.Fire], "meteor", 100)] },
            state with { Beams = [new(player.Id, V2.Zero, huge, [Element.Arcane], SpellShape.Beam)] },
            state with { Beams = [new(player.Id, new(-9, 0), new(9, 0), [Element.Arcane], SpellShape.Beam)] },
            state with { Points = state.Points.Select(p => p.Id == 0 ? p with { Position = huge } : p).ToArray() },
            state with { Events = [new(1, "hurt", huge, [], "")] }
        })
        {
            bool rejected = false;
            try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Se aceptó una dimensión o posición no representable en " + id);
        }
        // El extremo de un rayo y el destello del bastón pueden tocar o sobrepasar la frontera.
        var edge = map.Boundary[0]; var midpoint = (edge.A + edge.B) / 2;
        SnapshotValidation.Check(state with { Projectiles = [], Walls = [], Fields = [],
            Beams = [new(player.Id, midpoint, midpoint, [Element.Arcane], SpellShape.Beam)],
            Events = [new(1, "melee", midpoint + (midpoint - V2.Zero).Normalized * 0.5f, [], "")] });
        var lightningOrigin = id == ArenaMapId.Frontline ? new V2(-150, -12) : new V2(4, 0);
        SnapshotValidation.Check(state with { Beams = [new(player.Id, lightningOrigin + new V2(0, -5), lightningOrigin + new V2(0, 5), [Element.Lightning], SpellShape.Lightning)] });
    }
});
Test("Snapshots rechazan entidades y eventos repetidos, eventos desordenados y carga o vida contradictorias", () =>
{
    var (w, a, _) = Setup();
    Select(w, a, Element.Earth); Send(w, a, "cast"); Run(w, a, 10); Send(w, a, "release");
    var state = w.Snapshot(); var p = state.Players[0]; var shot = state.Projectiles.Single();
    foreach (var invalid in new[]
    {
        state with { Players = [p with { Alive = false }] },
        state with { Players = [p with { Charge = 1.1f, Casting = true }] },
        state with { Players = [p with { Health = 0, Alive = false, Casting = true }] },
        state with { Projectiles = [shot, shot] },
        state with { Projectiles = [], Walls = [new(1, a.Id, V2.Zero, new(0, 2), 10, 1, [Element.Shield]), new(1, a.Id, V2.Zero, new(0, 2), 10, 1, [Element.Shield])] },
        state with { Projectiles = [], Fields = [new(1, a.Id, V2.Zero, 2, 1, [Element.Fire], "burning_ground"), new(1, a.Id, V2.Zero, 2, 1, [Element.Fire], "burning_ground")] },
        state with { Events = [state.Events[0], state.Events[0]] },
        state with { Events = state.Events.Reverse().ToArray() }
    })
    {
        bool rejected = false; try { SnapshotValidation.Check(invalid); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Se aceptó estado contradictorio o identificadores que el renderer confunde.");
    }
    // Protocolos 8 anteriores podían conservar un valor de carga después de interrumpirla.
    SnapshotValidation.Check(state with { Players = [p with { Charge = 0.5f, Casting = false }] });
});
Test("Direcciones con ruta e IPv6 entre corchetes; se rechazan credenciales en URL", () =>
{
    Check(ArenaConnection.Address("[::1]").Port == Protocol.GamePort, "Se duplicaron corchetes IPv6.");
    Check(ArenaConnection.Address("[::1]:29015").Port == 29015, "Puerto IPv6 incorrecto.");
    Check(ArenaConnection.Address("juego.example/prefijo/play").Port == Protocol.GamePort, "Puerto incorrecto con ruta.");
    foreach (string invalid in new[] { "ws://usuario:clave@juego.example/play", "ws://juego.example/play?password=clave", "http://juego.example" })
    {
        bool rejected = false; try { ArenaConnection.Address(invalid); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Se aceptó una dirección inválida.");
    }
});
if (!args.Contains("--only-network"))
{
    await PerformanceRegressionTests.RunTransport(AsyncTest);
    async Task ReplyOnce(object value, Func<string, Task> request)
    {
        var portPicker = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        portPicker.Start(); int port = ((System.Net.IPEndPoint)portPicker.LocalEndpoint).Port; portPicker.Stop();
        using var listener = new System.Net.HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        async Task Reply()
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(value));
            context.Response.ContentType = "application/json"; context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token); context.Response.Close();
        }
        var response = Reply();
        await request($"127.0.0.1:{port}"); await response;
    }
    await AsyncTest("HTTP: crear salas valida todos sus campos y conserva una respuesta válida", async () =>
    {
        var valid = new RoomInfo("audit", "Sala", GameMode.Arena, 0, 8, 0, true, 15, 75, false);
        await ReplyOnce(valid, async address => Check(await RoomBrowser.CreateAsync(address, new()) == valid, "Se rechazó una sala válida."));
        foreach (var invalid in new[] { valid with { Name = null! }, valid with { Map = (ArenaMapId)99 },
            valid with { Mode = (GameMode)99 }, valid with { MaxPlayers = 17 }, valid with { Players = 9 },
            valid with { Bots = 1 }, valid with { ScoreLimit = 0 }, valid with { Tickets = 0 }, valid with { Mode = GameMode.Duel } })
            await ReplyOnce(invalid, async address =>
            {
                bool rejected = false; try { await RoomBrowser.CreateAsync(address, new()); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Crear sala aceptó metadatos que no cumplen sus reglas.");
            });
    });
    await AsyncTest("HTTP: el directorio valida el servidor, las reglas y los identificadores únicos", async () =>
    {
        var serverInfo = new ServerInfo("Servidor", 27015, 27016, GameMode.Warfare, 0, 8, Protocol.Version, false);
        var room = new RoomInfo("audit", "Sala", GameMode.Warfare, 0, 8, 0, true, 15, 75, false);
        var valid = new RoomDirectory(serverInfo, [room]);
        await ReplyOnce(valid, async address => Check((await RoomBrowser.ListAsync(address)).Rooms.Single() == room, "Se rechazó un directorio válido."));
        foreach (var invalid in new[] { valid with { Server = serverInfo with { Name = null! } },
            valid with { Server = serverInfo with { Players = 9 } }, valid with { Server = serverInfo with { DiscoveryPort = 0 } },
            valid with { Rooms = [room with { ScoreLimit = 201 }] }, valid with { Rooms = [room with { Tickets = 1001 }] },
            valid with { Rooms = [room, room] }, valid with { Rooms = [room with { Mode = GameMode.Duel }] } })
            await ReplyOnce(invalid, async address =>
            {
                bool rejected = false; try { await RoomBrowser.ListAsync(address); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "El directorio aceptó metadatos incompletos o repetidos.");
            });
    });
    await AsyncTest("Respuestas HTTP con y sin Content-Length tienen un límite real", async () =>
    {
        using var sized = new StringContent(new string('a', 4097));
        using var streamed = new StreamContent(new UnknownLengthStream(new byte[4097]));
        Check(streamed.Headers.ContentLength == null, "La prueba requiere una respuesta sin Content-Length.");
        foreach (var content in new System.Net.Http.HttpContent[] { sized, streamed })
        {
            bool rejected = false; try { await RoomBrowser.ReadBoundedAsync(content, 4096); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Se leyó una respuesta demasiado grande.");
        }
        using var valid = new StringContent("correcta"); Check(await RoomBrowser.ReadBoundedAsync(valid, 16) == "correcta", "Se truncó una respuesta válida.");
    });
    await AsyncTest("Logs concurrentes: JSON, contraseñas ocultas, exportación y cierre repetido", async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "arcana-logs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var log = new DiagnosticLog(Path.Combine(directory, "logs"), "test", queueCapacity: 1024);
            string secret = "clave-privada-\"\\-123"; log.ProtectSecret(secret);
            var context = new Dictionary<string, object?> { ["room"] = "sala-1", ["password"] = secret,
                ["endpoint"] = new Uri("https://user:clave@juego.example/play?token=abc"), ["note"] = secret };
            await Task.WhenAll(Enumerable.Range(0, 4).Select(n => Task.Run(() =>
                { for (int i = 0; i < 25; i++) log.Write(DiagnosticLevel.Error, "test.concurrent", "fallo " + secret, new IOException("password=oculta"), context); })));
            await log.FlushAsync();
            string[] lines = Directory.GetFiles(log.DirectoryPath, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
            Check(lines.Length == 100, "Se perdieron registros sin saturar la cola.");
            foreach (string line in lines)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(line);
                Check(doc.RootElement.GetProperty("session").GetString() == log.SessionId && doc.RootElement.GetProperty("context").GetProperty("room").GetString() == "sala-1", "Falta el contexto del fallo.");
                Check(!line.Contains("clave-privada") && !line.Contains("oculta") && !line.Contains("token=abc") && !line.Contains("user:"), "Credencial presente en el log.");
            }
            File.WriteAllText(Path.Combine(log.DirectoryPath, "launcher.cfg"), secret);
            string report = await log.ExportReportAsync(Path.Combine(directory, "reports"), context);
            string second = await log.ExportReportAsync(Path.Combine(directory, "reports"));
            Check(report != second, "Dos exportaciones colisionaron.");
            using var zip = System.IO.Compression.ZipFile.OpenRead(report);
            Check(zip.GetEntry("report.json") != null && zip.Entries.All(e => e.Name == "report.json" || e.Name.EndsWith(".jsonl")), "El ZIP incluyó archivos ajenos a los logs.");
            await Task.WhenAll(log.DisposeAsync().AsTask(), log.DisposeAsync().AsTask());
        }
        finally { Directory.Delete(directory, true); }
    });
    await AsyncTest("Rotación y retención de logs; un fallo del disco no derriba el juego", async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "arcana-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using (var log = new DiagnosticLog(directory, "test", maxFileBytes: 2048, maxFiles: 3))
            {
                for (int i = 0; i < 30; i++) { log.Write(DiagnosticLevel.Information, "rotation", new string('a', 400)); await log.FlushAsync(); }
                var files = Directory.GetFiles(directory, "test-*.jsonl");
                Check(files.Length == 3 && files.All(p => new FileInfo(p).Length <= 2048), "La retención o el tamaño no se respetaron.");
            }
            string blocked = Path.Combine(directory, "no-es-carpeta"); File.WriteAllText(blocked, "archivo");
            await using var broken = new DiagnosticLog(Path.Combine(blocked, "logs"), "broken");
            broken.Write(DiagnosticLevel.Error, "disk", "El juego continúa aunque no se pueda escribir."); await broken.FlushAsync();
            Check(broken.LastError != null, "No se comunicó el fallo del disco.");
        }
        finally { Directory.Delete(directory, true); }
    });
}

string? server = args.FirstOrDefault(a => a.StartsWith("--server="))?[9..];
int discoveryPort = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--discovery-port="))?[17..], out int port) ? port : Protocol.DiscoveryPort;
async Task WaitFor(Func<bool> ready, string reason)
{
    for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); }
    throw new Exception(reason);
}
if (server != null && args.Contains("--series-lobby-only"))
{
    string secret = Environment.GetEnvironmentVariable("TEST_PASSWORD") ?? "";
    await AsyncTest("Serie PvP: sin sala automática, elección de equipos y comienzo manual", async () =>
    {
        var empty = await RoomBrowser.ListAsync(server);
        Check(empty.Rooms.Length == 0, "El servidor anuncia una sala que el usuario no creó.");
        var room = await RoomBrowser.CreateAsync(server, new()
        {
            Name = "Serie de prueba", ServerPassword = secret, Mode = GameMode.Warfare,
            Map = ArenaMapId.Classic, MaxPlayers = 4, Bots = 0, BestOf = 5, Tickets = 5
        });
        Check(room.BestOf == 5, "La sala no conserva su formato al mejor de cinco.");
        await using var a = new ArenaConnection();
        await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Anfitrión", -1, secret, roomId: room.Id);
        await b.ConnectAsync(server, "Invitado", -1, secret, roomId: room.Id);
        await WaitFor(() => a.Latest?.Players.Length == 2 && b.Latest?.Players.Length == 2,
            "No se replicaron los integrantes de la sala de espera.");
        Check(a.Latest!.WaitingForPlayers && b.Latest!.WaitingForPlayers && a.Latest.BestOf == 5,
            "La sala empezó la partida sin confirmar equipos.");
        long tick = a.Latest.Tick;
        await Task.Delay(450);
        Check(a.Latest!.Tick == tick, "Se está simulando una ronda antes de pulsar Comenzar.");
        a.Send(new() { Type = "lobby-start" });
        await Task.Delay(300);
        Check(a.Latest!.WaitingForPlayers, "Se inició la partida sin jugadores preparados.");
        a.Send(new() { Type = "lobby-team", Team = 1 });
        await WaitFor(() => a.Latest!.Players.Single(p => p.Id == a.PlayerId).Team == 1,
            "No se aplicó la selección de equipo en el servidor.");
        a.Send(new() { Type = "lobby-team", Team = 0 });
        await WaitFor(() => a.Latest!.Players.Single(p => p.Id == a.PlayerId).Team == 0,
            "No se pudo recuperar el equipo azul.");
        a.Send(new() { Type = "lobby-ready" });
        b.Send(new() { Type = "lobby-ready" });
        await WaitFor(() => a.Latest!.LobbyReadyPlayerIds?.Length == 2,
            "No aparecen los dos jugadores preparados.");
        Check(a.Latest!.WaitingForPlayers, "La partida comenzó antes de que el anfitrión autorizara el inicio.");
        a.Send(new() { Type = "lobby-start" });
        await WaitFor(() => a.Latest is { WaitingForPlayers: false, Tick: > 0 },
            "El anfitrión no pudo comenzar la partida.");
        Check(a.Latest!.SeriesWins is { Length: 2 } && a.Latest.SeriesWins[0] == 0 &&
            a.Latest.SeriesWins[1] == 0, "La serie empezó con rondas ganadas.");
        var activeRooms = await RoomBrowser.ListAsync(server);
        Check(activeRooms.Rooms.Single(r => r.Id == room.Id).InProgress,
            "La sala no anuncia que la serie está en curso.");
        await using var lateJoin = new ArenaConnection();
        bool refused = false;
        try { await lateJoin.ConnectAsync(server, "Tardío", -1, secret, roomId: room.Id); }
        catch (InvalidOperationException e) when (e.Message.Contains("en curso", StringComparison.OrdinalIgnoreCase))
        { refused = true; }
        Check(refused, "Se permitió entrar en una serie de cinco ya comenzada.");
        // Si el rival abandona, se descarta la serie parcial: el anfitrión
        // regresa a preparación y un sustituto puede entrar de forma normal.
        await b.DisposeAsync();
        await WaitFor(() => a.Latest is { WaitingForPlayers: true } waiting &&
            waiting.RewardStatus.Contains("canceló", StringComparison.OrdinalIgnoreCase),
            "El servidor no devolvió a la sala de preparación al desconectarse el rival.");
        Check(a.Latest!.SeriesWins is [0, 0], "Se conservaron victorias de una serie cancelada.");
        await using var substitute = new ArenaConnection();
        await substitute.ConnectAsync(server, "Sustituto", -1, secret, roomId: room.Id);
        await WaitFor(() => a.Latest!.Players.Length == 2 && substitute.Latest!.Players.Length == 2,
            "El sustituto no pudo unirse a la sala recuperada.");
        Check(a.Latest!.WaitingForPlayers, "La nueva serie empezó automáticamente al entrar un sustituto.");
        a.Send(new() { Type = "lobby-ready" });
        substitute.Send(new() { Type = "lobby-ready" });
        await WaitFor(() => a.Latest!.LobbyReadyPlayerIds?.Length == 2,
            "No se prepararon ambos jugadores tras la desconexión.");
        a.Send(new() { Type = "lobby-start" });
        await WaitFor(() => a.Latest is { WaitingForPlayers: false },
            "El anfitrión no pudo iniciar la serie recuperada.");
    });
    Console.WriteLine($"RESULTADO LOBBY: {passed} correctas, {failed} fallidas.");
    return failed == 0 ? 0 : 1;
}

if (server != null)
{
    string password = Environment.GetEnvironmentVariable("TEST_PASSWORD") ?? "";
    await FrontlineTests.RunNetwork(server, password, AsyncTest);
    await DefenseTests.RunNetwork(server, password, AsyncTest);
    await UpgradeTests.RunNetwork(server, password, AsyncTest);
    await LoadingTests.RunNetwork(server, password, AsyncTest);
    await AsyncTest("Crear salas con opciones, enumerarlas y aislar estado aunque los IDs coincidan", async () =>
    {
        var roomA = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba aislamiento A", ServerPassword = password,
            Mode = GameMode.Arena, MaxPlayers = 4, FriendlyFire = false, ScoreLimit = 9, Tickets = 45 });
        var roomB = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba aislamiento B", ServerPassword = password,
            Mode = GameMode.Warfare, Map = ArenaMapId.Citadel, MaxPlayers = 3, Bots = 0 });
        var directory = await RoomBrowser.ListAsync(server);
        Check(directory.Rooms.Any(r => r.Id == roomA.Id && r.ScoreLimit == 9 && !r.FriendlyFire) &&
            directory.Rooms.Any(r => r.Id == roomB.Id && r.Bots == 0 && r.Map == ArenaMapId.Citadel), "No se publicaron las opciones de sala.");
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Solo A", -1, password, roomId: roomA.Id);
        await b.ConnectAsync(server, "Solo B", -1, password, roomId: roomB.Id);
        Check(a.PlayerId == b.PlayerId, "La prueba debe cubrir IDs locales iguales en dos salas.");
        a.Send(new() { Type = "element", Element = Element.Fire });
        await WaitFor(() => a.Latest!.Players.First(p => p.Id == a.PlayerId).Selected.Length == 1, "No llegó la intención a su sala.");
        Check(a.Latest!.Mode == GameMode.Arena && a.Latest.Players.Length == 1 && b.Latest!.Mode == GameMode.Warfare &&
            b.Latest.Players.All(p => p.Name != "Solo A" && p.Selected.Length == 0), "Se filtró estado entre salas.");
        Check(a.Room?.Id == roomA.Id && b.Room?.Id == roomB.Id, "El saludo no identifica la sala.");
        Check(a.Latest.Map == ArenaMapId.Classic && b.Latest!.Map == ArenaMapId.Citadel && b.Room!.Map == ArenaMapId.Citadel &&
            b.Latest.Points.Select(p => p.Position).SequenceEqual(ArenaMap.Get(ArenaMapId.Citadel).ControlPoints) &&
            b.Latest.HoldingTeam == -1 && b.Latest.HoldSeconds == 0, "La red mezcló los mapas o perdió sus reglas de control.");
    });
    await AsyncTest("Ascensión: sala, saludo y snapshots replican el tercer mapa", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Ascensión en red", ServerPassword = password,
            Mode = GameMode.Duel, Map = ArenaMapId.Ascension, MaxPlayers = 2, ScoreLimit = 10 });
        var directory = await RoomBrowser.ListAsync(server);
        Check(directory.Rooms.Any(r => r.Id == room.Id && r.Map == ArenaMapId.Ascension), "El directorio perdió el mapa.");
        await using var connection = new ArenaConnection();
        await connection.ConnectAsync(server, "Ascendente", 0, password, roomId: room.Id);
        Check(connection.Room?.Map == ArenaMapId.Ascension && connection.Latest!.Map == ArenaMapId.Ascension &&
            connection.Latest.Players.Single().MaxHealth == 1000 && connection.Latest.HoldingTeam == -1 &&
            connection.Latest.Points.Select(p => p.Position).SequenceEqual(ArenaMap.Get(ArenaMapId.Ascension).ControlPoints),
            "El saludo o la geometría no coinciden con la sala.");
        await WaitFor(() => connection.Latest!.Players.Single().InvulnerableSeconds <= 0, "No terminó la protección inicial.");
        for (int i = 0; i < 3; i++)
        {
            float health = connection.Latest!.Players.Single().Health;
            for (int element = 0; element < 3; element++) connection.Send(new() { Type = "element", Element = Element.Arcane });
            connection.Send(new() { Type = "self" });
            await WaitFor(() => connection.Latest!.Players.Single().Health < health, "No se replicó el autodaño.");
            if (!connection.Latest!.Players.Single().Alive) break;
            double readyAt = connection.Latest.Time + 0.5;
            await WaitFor(() => connection.Latest!.Time >= readyAt, "La simulación no completó el enfriamiento.");
        }
        await WaitFor(() => connection.Latest!.Players.Single().Deaths == 1, "No se replicó la muerte.");
        var dead = connection.Latest!.Players.Single();
        Check(!dead.Alive && dead.MaxHealth == 1000 && dead.Respawn > 2 && dead.Respawn <= 3, "El servidor no aplicó la primera espera de 3 s.");
        SnapshotValidation.Check(connection.Latest);
    });
    await AsyncTest("Coliseo en red: dos clientes comparten las posiciones aleatorias y la recogida", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Objetos del Coliseo", ServerPassword = password,
            Mode = GameMode.Duel, Map = ArenaMapId.Ascension, MaxPlayers = 2, ScoreLimit = 10 });
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Recolector del Coliseo", 0, password, roomId: room.Id);
        await b.ConnectAsync(server, "Observador del Coliseo", 1, password, roomId: room.Id);
        Check(a.Latest!.Pickups is { Length: 14 } && a.Latest.Pickups.SequenceEqual(b.Latest!.Pickups!), "Los clientes sortearon posiciones diferentes.");
        var start = a.Latest.Players.Single(p => p.Id == a.PlayerId).Position;
        var item = a.Latest.Pickups!.OrderBy(i => V2.Distance(i.Position, start)).First();
        for (int i = 0; i < 1200 && !a.Latest.Players.Single(p => p.Id == a.PlayerId).Buffs!.Any(); i++)
        {
            a.Send(new() { Type = "input", MoveTo = true, TargetX = item.Position.X, TargetY = item.Position.Y });
            await Task.Delay(20);
        }
        a.Send(new() { Type = "input" });
        await WaitFor(() => b.Latest!.Players.Single(p => p.Id == a.PlayerId).Buffs!.Any(buff => buff.Kind == item.Kind), "El rival no recibió la mejora recogida.");
        Check(!b.Latest!.Pickups!.Single(i => i.Id == item.Id).Active && b.Latest.Pickups!.Single(i => i.Id == item.Id).Position == item.Position,
            "La recogida no se replicó en la posición del servidor."); SnapshotValidation.Check(a.Latest); SnapshotValidation.Check(b.Latest);
    });
    await AsyncTest("Reliquias en red: objetos, recogida autoritativa, acumulaciones y limpieza al morir", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Reliquias en red", ServerPassword = password,
            Mode = GameMode.Duel, Map = ArenaMapId.Relics, MaxPlayers = 2, ScoreLimit = 10 });
        await using var connection = new ArenaConnection();
        await connection.ConnectAsync(server, "Recolector en red", 0, password, roomId: room.Id);
        Check(connection.Room?.Map == ArenaMapId.Relics && connection.Latest!.Pickups is { Length: 14 }, "El saludo perdió el cuarto mapa u objetos.");
        var item = connection.Latest!.Pickups!.First(i => i.Kind == BuffKind.Power);
        await WaitFor(() =>
        {
            connection.Send(new() { Type = "input", MoveTo = true, TargetX = item.Position.X, TargetY = item.Position.Y,
                AimX = 0, AimY = 0 });
            return connection.Latest!.Players.Single().Buffs!.Any(b => b.Kind == BuffKind.Power && b.Stacks == 1);
        }, "El servidor no recogió el objeto al llegar.");
        connection.Send(new() { Type = "input" });
        Check(!connection.Latest!.Pickups!.Single(i => i.Id == item.Id).Active, "La red dejó disponible el objeto recogido.");
        for (int i = 0; i < 3; i++)
        {
            for (int e = 0; e < 3; e++) connection.Send(new() { Type = "element", Element = Element.Arcane });
            connection.Send(new() { Type = "self" }); await Task.Delay(550);
        }
        await WaitFor(() => !connection.Latest!.Players.Single().Alive, "No se replicó la muerte del recolector.");
        Check(connection.Latest!.Players.Single().Buffs!.Length == 0 && connection.Latest.Players.Single().MaxHealth == 1000,
            "La red conservó las mejoras tras morir.");
        SnapshotValidation.Check(connection.Latest);
    });
    await AsyncTest("Contraseña de sala, aforo y sala inexistente se validan", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Duelo privado", ServerPassword = password,
            Password = "sala-clave-é", Mode = GameMode.Duel, MaxPlayers = 2, Bots = 1 });
        await using var wrong = new ArenaConnection(); bool rejected = false;
        try { await wrong.ConnectAsync(server, "Sin clave", -1, password, roomId: room.Id, roomPassword: "incorrecta"); }
        catch (InvalidOperationException e) { rejected = e.Message.Contains("sala"); }
        Check(rejected, "Se admitió una contraseña de sala incorrecta.");
        if (password.Length > 0)
        {
            // La clave del SERVIDOR se comprueba de forma independiente de la SALA.
            await using var wrongServer = new ArenaConnection();
            bool rejectedServer = false;
            try { await wrongServer.ConnectAsync(server, "Clave servidor errónea", -1,
                "servidor-incorrecto", roomId: room.Id, roomPassword: "sala-clave-é"); }
            catch (InvalidOperationException error)
            {
                rejectedServer = error.Message.Contains("Contraseña del servidor incorrecta.", StringComparison.Ordinal);
            }
            Check(rejectedServer, "El servidor confundió las dos contraseñas.");
        }
        await using var a = new ArenaConnection(); await a.ConnectAsync(server, "Con clave", -1, password, roomId: room.Id, roomPassword: "sala-clave-é");
        Check(a.Latest!.Players.Length == 2, "No se añadió el bot del duelo.");
        await using var full = new ArenaConnection(); rejected = false;
        try { await full.ConnectAsync(server, "Tercero", -1, password, roomId: room.Id, roomPassword: "sala-clave-é"); }
        catch (InvalidOperationException e) { rejected = e.Message.Contains("llena"); }
        Check(rejected, "Se excedió el aforo.");
        await using var missing = new ArenaConnection(); rejected = false;
        try { await missing.ConnectAsync(server, "Perdido", -1, password, roomId: "no-existe"); }
        catch (InvalidOperationException e) { rejected = e.Message.Contains("existe"); }
        Check(rejected, "Se admitió una sala inexistente.");
    });
    await AsyncTest("Crear sala rechaza opciones inválidas y no expone contraseñas", async () =>
    {
        bool rejected = false;
        try { await RoomBrowser.CreateAsync(server, new() { Name = "Inválida", Mode = GameMode.Duel, MaxPlayers = 8, ServerPassword = password }); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Se admitió un duelo de ocho jugadores.");
        rejected = false;
        try { await RoomBrowser.CreateAsync(server, new() { Name = "Mapa inválido", Map = (ArenaMapId)99, ServerPassword = password }); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Se admitió una sala con un mapa inexistente.");
        using var http = new HttpClient();
        string directory = await http.GetStringAsync(RoomBrowser.Endpoint(server, "rooms"));
        Check(!directory.Contains("sala-clave") && (password.Length == 0 || !directory.Contains(password)), "Se expuso una contraseña en el directorio.");
        if (password.Length > 0)
        {
            rejected = false;
            try { await RoomBrowser.CreateAsync(server, new() { Name = "No autorizada", ServerPassword = "incorrecta" }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Se creó una sala sin la contraseña del servidor.");
        }
    });
    await AsyncTest("Dos clientes reales, selección sincronizada y movimiento autoritativo", async () =>
    {
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Prueba A", 0, password); await b.ConnectAsync(server, "Prueba B", 1, password);
        Check(a.PlayerId != b.PlayerId, "Los clientes comparten ID.");
        await WaitFor(() => a.Latest?.Players.Length >= 2, "No se replicó el segundo jugador.");
        a.Send(new() { Type = "element", Element = Element.Fire }); a.Send(new() { Type = "element", Element = Element.Fire }); a.Send(new() { Type = "element", Element = Element.Fire });
        await WaitFor(() => b.Latest?.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.Selected.Length == 3, "El rival no recibió los elementos.");
        var start = a.Latest!.Players.First(p => p.Id == a.PlayerId).Position;
        a.Send(new() { Type = "input", MoveX = 999, AimX = 0, AimY = 0 });
        await Task.Delay(220);
        var end = a.Latest!.Players.First(p => p.Id == a.PlayerId).Position;
        Check(V2.Distance(start, end) > 0.2 && V2.Distance(start, end) < 2.3, "El cliente impuso una velocidad inválida.");
        a.Send(new() { Type = "input", AimX = 0, AimY = 0 });
        a.Send(new() { Type = "cast" });
        await WaitFor(() => b.Latest!.Players.First(p => p.Id == a.PlayerId).Casting, "No se replicó el lanzamiento.");
        Check(b.Latest!.Players.First(p => p.Id == a.PlayerId).DisplayElements.SequenceEqual(new[] { Element.Fire, Element.Fire, Element.Fire }),
            "El rival perdió los elementos al empezar el canal.");
        a.Send(new() { Type = "release" });
        await WaitFor(() => !b.Latest!.Players.First(p => p.Id == a.PlayerId).Casting, "No se replicó la interrupción.");
        Check(b.Latest!.Players.First(p => p.Id == a.PlayerId).DisplayElements.Length == 0, "El rival conservó casillas de un canal terminado.");
    });


    await AsyncTest("La red replica capacidad y duración reales del escudo a ambos jugadores", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Prueba de escudo", ServerPassword = password, Mode = GameMode.Arena, MaxPlayers = 2 });
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Escudo A", -1, password, roomId: room.Id); await b.ConnectAsync(server, "Escudo B", -1, password, roomId: room.Id);
        a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "self" });
        await WaitFor(() => b.Latest!.Players.FirstOrDefault(p => p.Id == a.PlayerId)?.ShieldMax == 420, "El rival no recibió el escudo normal.");
        var shield = b.Latest!.Players.First(p => p.Id == a.PlayerId);
        Check(shield.Shield == 420 && shield.ShieldSeconds is > 0 and <= 12, "El escudo normal no se transmitió lleno o perdió su duración.");
        await Task.Delay(550); a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Earth }); a.Send(new() { Type = "self" });
        await WaitFor(() => a.Latest!.Players.First(p => p.Id == a.PlayerId).ShieldMax == 650 && b.Latest!.Players.First(p => p.Id == a.PlayerId).ShieldMax == 650, "No se replicó el cambio de capacidad a tierra.");
        shield = b.Latest!.Players.First(p => p.Id == a.PlayerId);
        Check(shield.Shield == 650 && shield.Ward.Contains(Element.Earth) && shield.ShieldSeconds > 0, "La guarda de tierra se transmitió con datos incorrectos.");
    });
    await AsyncTest("Reutilizar una intención conserva dos comandos; cierre concurrente es idempotente", async () =>
    {
        var connection = new ArenaConnection();
        await connection.ConnectAsync(server, "Cierre concurrente", -1, password);
        var message = new ClientMessage { Type = "element", Element = Element.Fire };
        connection.Send(message); connection.Send(message); message.Element = Element.Life;
        await WaitFor(() => connection.Latest!.Players.First(p => p.Id == connection.PlayerId).Selected.SequenceEqual(new[] { Element.Fire, Element.Fire }), "Se conservaron referencias de mensajes mutables.");
        var sender = Task.Run(async () => { for (int i = 0; i < 20; i++) { connection.Send(new() { Type = "input" }); await Task.Delay(2); } });
        await Task.WhenAll(sender, connection.DisposeAsync().AsTask(), connection.DisposeAsync().AsTask());
        Check(!connection.Connected && !connection.Send(new() { Type = "release" }), "Una conexión cerrada aceptó mensajes.");
    });
    await AsyncTest("La red replica tipos y objetivos y aplica la debilidad en el servidor", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Afinidades", ServerPassword = password, Mode = GameMode.Arena,
            MaxPlayers = 2, ScoreLimit = 7, Tickets = 33 });
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Piromante", 0, password, roomId: room.Id, wizardType: WizardType.Pyromancer);
        await b.ConnectAsync(server, "Tempestario", 1, password, roomId: room.Id, wizardType: WizardType.Stormcaller);
        await WaitFor(() => b.Latest!.Players.Any(p => p.Id == a.PlayerId && p.WizardType == WizardType.Pyromancer), "El rival no recibió el tipo.");
        Check(a.Latest!.ScoreLimit == 7 && a.Latest.StartingTickets == 33 &&
            b.Latest!.Players.First(p => p.Id == b.PlayerId).WizardType == WizardType.Stormcaller, "Se perdieron los objetivos o el tipo.");
        await Task.Delay(1300);
        a.Send(new() { Type = "element", Element = Element.Cold }); a.Send(new() { Type = "self" });
        await WaitFor(() => b.Latest!.Players.First(p => p.Id == a.PlayerId).Health < 1000, "No se aplicó el daño.");
        Check(Math.Abs(b.Latest!.Players.First(p => p.Id == a.PlayerId).Health - 970) < 0.01f, "El servidor no aplicó el +25 % de debilidad al frío.");
    });
    await AsyncTest("La red replica escudo regenerativo y minas de vida armadas", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Efectos", ServerPassword = password, Mode = GameMode.Warfare,
            MaxPlayers = 2, FriendlyFire = false });
        await using var a = new ArenaConnection(); await using var b = new ArenaConnection();
        await a.ConnectAsync(server, "Vida", 0, password, roomId: room.Id); await b.ConnectAsync(server, "Observador", 1, password, roomId: room.Id);
        a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Life }); a.Send(new() { Type = "self" });
        await WaitFor(() => b.Latest!.Players.Any(p => p.Id == a.PlayerId && p.RegenerationRate == 38), "No se recibió la regeneración.");
        await Task.Delay(500);
        var player = a.Latest!.Players.First(p => p.Id == a.PlayerId);
        a.Send(new() { Type = "input", AimX = player.Position.X + 5, AimY = player.Position.Y });
        await Task.Delay(80);
        a.Send(new() { Type = "element", Element = Element.Shield }); a.Send(new() { Type = "element", Element = Element.Life }); a.Send(new() { Type = "cast" });
        await WaitFor(() => b.Latest!.Fields.Any(f => f.Kind == "healing_mine" && f.Armed), "No se recibió la mina armada.");
        var mine = b.Latest!.Fields.Single(f => f.Kind == "healing_mine");
        Check(mine.TriggerRadius == 0.85f && mine.Radius == 2.4f, "Se perdió el radio de activación o de curación.");
        SnapshotValidation.Check(b.Latest!);
    });
    await AsyncTest("El servidor rechaza un tipo de mago desconocido en el saludo", async () =>
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket(); using var timeout = new CancellationTokenSource(5000);
        await socket.ConnectAsync(ArenaConnection.Address(server), timeout.Token);
        byte[] hello = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(new ClientMessage { Type = "hello", Name = "Inválido", Password = password, WizardType = (WizardType)999 }));
        await socket.SendAsync(hello.AsMemory(), System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096]; var reply = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        var message = Protocol.Deserialize<ServerMessage>(System.Text.Encoding.UTF8.GetString(buffer, 0, reply.Count));
        Check(message is { Type: "error" } && message.Error.Contains("mago"), "El servidor aceptó un tipo de mago desconocido.");
    });
    await AsyncTest("Cerrar durante el saludo cancela el arranque de la conexión", async () =>
    {
        var connection = new ArenaConnection();
        var connect = connection.ConnectAsync(server, "Arranque cancelado", -1, password);
        await connection.DisposeAsync();
        try { await connect; } catch (Exception e) when (e is OperationCanceledException or System.Net.WebSockets.WebSocketException or IOException or ObjectDisposedException) { }
        Check(!connection.Connected, "Se activó una conexión después de cerrarla.");
    });
    await AsyncTest("Salas vacías con bots se pausan; el primer jugador inicia la simulación", async () =>
    {
        var room = await RoomBrowser.CreateAsync(server, new() { Name = "Reposo con bots", MaxPlayers = 8, Bots = 7, ServerPassword = password });
        await Task.Delay(500);
        await using var connection = new ArenaConnection(); await connection.ConnectAsync(server, "Despertar", -1, password, roomId: room.Id);
        Check(connection.Latest!.Tick < 15, "Los bots simularon una sala vacía.");
        long first = connection.Latest.Tick;
        await WaitFor(() => connection.Latest!.Tick > first + 5, "La simulación no se reanudó al entrar.");
    });
    await AsyncTest("API de salas rechaza formato no JSON y JSON malformado sin error 500", async () =>
    {
        using var http = new HttpClient();
        using var text = new StringContent("no es json");
        using var wrongType = await http.PostAsync(RoomBrowser.Endpoint(server, "rooms"), text);
        Check((int)wrongType.StatusCode == 415, "El Content-Type inválido no recibió 415.");
        using var malformed = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
        using var invalidJson = await http.PostAsync(RoomBrowser.Endpoint(server, "rooms"), malformed);
        Check((int)invalidJson.StatusCode == 400, "El JSON malformado no recibió 400.");
        Check((await http.GetAsync(RoomBrowser.Endpoint(server, "health"))).IsSuccessStatusCode, "El error de un cliente derribó el servidor.");
    });
    await AsyncTest("Más de 32 jugadores pueden entrar en salas con plazas y volver a conectar", async () =>
    {
        var rooms = new List<RoomInfo>();
        for (int i = 0; i < 3; i++) rooms.Add(await RoomBrowser.CreateAsync(server,
            new() { Name = $"Aforo compartido {i}", ServerPassword = password, MaxPlayers = 16, Bots = 0 }));
        var connections = new List<ArenaConnection>();
        try
        {
            for (int i = 0; i < 33; i++)
            {
                var connection = new ArenaConnection(); connections.Add(connection);
                await connection.ConnectAsync(server, $"Jugador {i}", -1, password, roomId: rooms[i / 16].Id);
            }
            var directory = await RoomBrowser.ListAsync(server);
            Check(connections.All(c => c.Connected) && directory.Rooms.Where(r => rooms.Any(room => room.Id == r.Id)).Sum(r => r.Players) == 33,
                "El límite de saludos se aplicó a jugadores ya conectados.");
            foreach (var connection in connections) SnapshotValidation.Check(connection.Latest!);
        }
        finally { await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask())); }
        await WaitFor(() => connections.All(c => !c.Connected), "No se cerraron las conexiones de aforo.");
        await using var again = new ArenaConnection();
        await again.ConnectAsync(server, "Después del cierre", -1, password, roomId: rooms[2].Id);
        Check(again.Connected, "Cerrar los jugadores impidió un nuevo saludo.");
    });
    // La red puente de Docker sirve para IP directa. LAN por broadcast se comprueba con red host.
    if (!args.Contains("--skip-discovery")) await AsyncTest("Descubrimiento UDP LAN devuelve el servidor real", async () =>
    {
        var found = await LanDiscovery.FindAsync(discoveryPort);
        Check(found.Length > 0 && found.Any(s => s.Info.Protocol == Protocol.Version), "No se encontró el servidor por UDP.");
    });
    await AsyncTest("Saludo con versión incompatible es rechazado", async () =>
    {
        using var socket = new System.Net.WebSockets.ClientWebSocket(); using var timeout = new CancellationTokenSource(3000);
        await socket.ConnectAsync(ArenaConnection.Address(server), timeout.Token);
        var bytes = System.Text.Encoding.UTF8.GetBytes(Protocol.Serialize(new ClientMessage { Type = "hello", Protocol = 999, Password = password }));
        await socket.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096]; var response = await socket.ReceiveAsync(buffer, timeout.Token);
        var result = Protocol.Deserialize<ServerMessage>(System.Text.Encoding.UTF8.GetString(buffer, 0, response.Count));
        Check(result?.Type == "error", "Se admitió una versión incompatible.");
    });
    if (password.Length > 0) await AsyncTest("Contraseña incorrecta es rechazada", async () =>
    {
        await using var connection = new ArenaConnection(); bool rejected = false;
        try { await connection.ConnectAsync(server, "No autorizado", -1, "contraseña incorrecta"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Se admitió una contraseña incorrecta.");
    });
}
string? package = args.FirstOrDefault(a => a.StartsWith("--package="))?[10..];
if (package != null) await AsyncTest("Paquete autónomo: alojar, crear, entrar y detener desde LocalServerHost", async () =>
{
    using var host = new LocalServerHost();
    string? inheritedMap = Environment.GetEnvironmentVariable("ARENA_MAP");
    try
    {
        Environment.SetEnvironmentVariable("ARENA_MAP", "Relics");
        await host.StartAsync(package, Path.Combine(Path.GetTempPath(), "arcana-package-test"), new("Paquete de prueba", 29015, 29016, "paquete-clave"));
    }
    finally { Environment.SetEnvironmentVariable("ARENA_MAP", inheritedMap); }
    Check(host.Running, "No arrancó el servidor incluido.");
    Check((await RoomBrowser.ListAsync(host.Address)).Rooms.Length == 0,
        "Alojar muestra una sala automática antes de que el usuario configure la suya.");
    var room = await RoomBrowser.CreateAsync(host.Address, new() { Name = "Sala desde paquete", Bots = 1, ServerPassword = "paquete-clave" });
    await using var player = new ArenaConnection();
    await player.ConnectAsync(host.Address, "Doble clic", -1, "paquete-clave", roomId: room.Id);
    Check(player.Latest!.Players.Length == 2, "No se pudo jugar en el servidor incluido.");
    var lan = await LanDiscovery.FindAsync(29016);
    Check(lan.Any(s => s.Info.Name == "Paquete de prueba"), "El servidor incluido no se anuncia en LAN.");
    await player.DisposeAsync(); host.Stop(); Check(!host.Running, "El servidor quedó activo tras detenerlo.");
});

if (package != null) await AsyncTest("Alojamiento: puerto ocupado, doble inicio, cancelación y logs conservados", async () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "arcana-host-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        using var host = new LocalServerHost(); var options = new LocalServerOptions("Arranque de prueba", 29035, 29036, "host-regression-clave");
        var pending = host.StartAsync(package, directory, options); host.Stop();
        bool canceled = false; try { await pending; } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && !host.Running, "El servidor sobrevivió a cancelar su arranque.");
        await host.StartAsync(package, directory, options);
        bool blocked = false; try { await host.StartAsync(package, directory, options); } catch (InvalidOperationException) { blocked = true; }
        Check(blocked && host.Running, "El doble inicio reemplazó al servidor activo.");
        using (var competitor = new LocalServerHost())
        {
            blocked = false;
            try { await competitor.StartAsync(package, directory, options); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked && host.Running && !competitor.Running, "Se admitió un puerto ocupado o se detuvo el otro servidor.");
        }
        var first = await RoomBrowser.ListAsync(host.Address); Check(!string.IsNullOrWhiteSpace(first.Server.InstanceId), "El servidor carece de identidad de arranque.");
        host.Stop();
        var oldLogs = Directory.GetFiles(Path.Combine(directory, "logs"), "server-*.jsonl");
        Check(oldLogs.Length > 0, "El servidor incluido no escribió logs persistentes.");
        await host.StartAsync(package, directory, options);
        var second = await RoomBrowser.ListAsync(host.Address); Check(first.Server.InstanceId != second.Server.InstanceId, "Se reutilizó la identidad del proceso.");
        host.Stop();
        Check(oldLogs.All(File.Exists), "Reiniciar borró el diagnóstico anterior.");
        foreach (string file in Directory.GetFiles(Path.Combine(directory, "logs"), "server-*.jsonl"))
            Check(!File.ReadAllText(file).Contains(options.Password), "El log del servidor expuso su contraseña.");
    }
    finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
});
Console.WriteLine($"RESULTADO: {passed} correctas, {failed} fallidas.");
return failed == 0 ? 0 : 1;

sealed class UnknownLengthStream(byte[] bytes) : Stream
{
    private readonly MemoryStream _inner = new(bytes);
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _inner.ReadAsync(buffer, token);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}
