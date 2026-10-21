using Arcana.Core;
using System.Net;
using System.Reflection;
using System.Text.Json;

internal static class MatchStatisticsTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Call(GameWorld world, string name, params object?[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, args);
    private static void Damage(GameWorld world, Wizard target, float amount, Wizard owner, Element[]? elements = null) => Call(world, "Damage", target, amount, owner.Id, elements);
    private static (GameWorld World, Wizard A, Wizard B) Arena()
    {
        var w = new GameWorld(new() { Mode = GameMode.Arena }); var a = w.AddPlayer("IDENTITY_MUST_NOT_APPEAR", 0)!; var b = w.AddPlayer("OTHER_IDENTITY", 1)!;
        a.Invulnerable = b.Invulnerable = 0; w.Step(); return (w, a, b);
    }
    private static string DirectoryForTest() => Path.Combine(Path.GetTempPath(), "arcana-statistics-tests", Guid.NewGuid().ToString("N"));
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { return; } throw new Exception("Se aceptó un informe inválido."); }
    public static void Run(Action<string, Action> test)
    {
        test("Estadísticas: todos los modos generan un contrato cerrado sin identidad", () =>
        {
            foreach (var mode in Enum.GetValues<GameMode>())
            {
                var w = new GameWorld(new() { Mode = mode, MaxPlayers = 4, Map = mode == GameMode.Defense ? ArenaMapId.Bastion : ArenaMapId.Classic });
                w.AddPlayer("PRIVATE_NAME"); w.Step(); var r = w.MatchStatistics(); string json = r.ToJson();
                Check(!json.Contains("PRIVATE_NAME") && !json.Contains("uid", StringComparison.OrdinalIgnoreCase) && !json.Contains("email"), "El informe contiene identidad.");
                Check(MatchStatisticsReport.Parse(json).RunId == r.RunId && r.Mode == mode && r.Humans == 1, "No recuperó el contrato.");
            }
        });
        test("Estadísticas: vida, escudo, exceso y bajas efectivos sin duplicados", () =>
        {
            var (w, a, b) = Arena(); b.Health = 100; b.Shield = 50; Damage(w, b, 200, a); Damage(w, b, 200, a);
            var r = w.MatchStatistics();
            Check(r.Sum(StatisticGroup.Total, StatisticMeasure.Damage) == 100 && r.Sum(StatisticGroup.Total, StatisticMeasure.ShieldDamage) == 50 && r.Sum(StatisticGroup.Total, StatisticMeasure.Overkill) == 50, "Daño contable incorrecto.");
            Check(r.Sum(StatisticGroup.Total, StatisticMeasure.Kills) == 1 && r.Sum(StatisticGroup.Total, StatisticMeasure.Deaths) == 1, "Baja duplicada.");
        });
        test("Estadísticas: no atribuye daño que se perdió por precisión flotante", () =>
        {
            var (w, a, b) = Arena(); b.Health = b.MaxHealth = 100000000; Damage(w, b, 1, a);
            Check(w.MatchStatistics().Sum(StatisticGroup.Total, StatisticMeasure.Damage) == 0, "Registró daño sin perder vida.");
        });
        test("Estadísticas: mezcla contra prisma reparte únicamente el componente vulnerable", () =>
        {
            var (w, a, b) = Arena(); typeof(Wizard).GetProperty("VulnerableElement", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(b, Element.Fire);
            Call(w, "Hit", b, new SpellRecipe([Element.Fire, Element.Arcane]), 1f, a.Id, V2.Zero, 1f);
            var r = w.MatchStatistics();
            Check(r.Sum(StatisticGroup.Element, StatisticMeasure.Damage, (int)Element.Fire) > 0 && r.Sum(StatisticGroup.Element, StatisticMeasure.Damage, (int)Element.Arcane) == 0 && r.Sum(StatisticGroup.Element, StatisticMeasure.ImmuneHits, (int)Element.Arcane) == 1, "Se atribuyó daño al elemento inmune.");
        });
        test("Estadísticas: curación real y recasteo de escudo sin inflar capacidad", () =>
        {
            var (w, a, _) = Arena(); a.Health = a.MaxHealth - 5; a.Selection.Add(Element.Life); Call(w, "SelfCast", a);
            a.ActionCooldown = 0; a.Selection.Add(Element.Shield); Call(w, "SelfCast", a); float shield = a.Shield;
            a.ActionCooldown = 0; a.Selection.Add(Element.Shield); Call(w, "SelfCast", a);
            var r = w.MatchStatistics(); Check(r.Sum(StatisticGroup.Total, StatisticMeasure.Healing) == 5 && r.Sum(StatisticGroup.Total, StatisticMeasure.ShieldGranted) == shield, "Curación o escudo inflados.");
        });
        test("Estadísticas: nueva ronda conserva red y cambia identificador", () =>
        {
            var (w, _, _) = Arena(); w.SetStatisticsNetwork(); var r = w.MatchStatistics(); w.ResetForWaitingRoom(); w.Step();
            var next = w.MatchStatistics(); Check(next.RunId != r.RunId && next.Network && next.Revision == 1 && next.Sum(StatisticGroup.Total, StatisticMeasure.Spawns) == 2, "Ronda no aislada.");
        });
        test("Estadísticas: mover un bot de duelo y cambiar equipo en lobby actualiza la atribución", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Duel, Bots = 1, MaxPlayers = 2 });
            var bot = w.Players.Values.Single(); var human = w.AddPlayer("Mago", 0)!;
            Check(bot.Team == 1, "El duelo no movió el bot al equipo libre.");
            human.Invulnerable = bot.Invulnerable = 0; w.Step(); Damage(w, human, 25, bot);
            Check(w.MatchStatistics().Sum(StatisticGroup.Actor, StatisticMeasure.Damage, (int)bot.WizardType + 16 + 32) == 25, "El bot conserva su equipo anterior en las estadísticas.");
            var lobby = new GameWorld(new() { Mode = GameMode.Warfare }); var p = lobby.AddPlayer("Mago", 0)!;
            Check(lobby.SetPlayerTeam(p.Id, 1), "No cambió el equipo."); p.Invulnerable = 0; Damage(lobby, p, 10, p);
            Check(lobby.MatchStatistics().Sum(StatisticGroup.Actor, StatisticMeasure.DamageReceived, 32 + (int)p.WizardType) == 10, "La atribución humana conserva el equipo anterior.");
        });
        test("Estadísticas: supervivencia guarda acumulados y bifurca un guardado anterior", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); var a = w.AddPlayer("Mago")!;
            for (int i = 0; i < 60; i++) w.Step();
            var save = w.SaveSurvival(a.Id, Guid.NewGuid().ToString("N")); var report = w.MatchStatistics();
            var restored = GameWorld.RestoreSurvival(save); Check(restored.MatchStatistics().RunId == report.RunId && restored.MatchStatistics().Revision == report.Revision, "Perdió el acumulado.");
            restored.ReconcileMatchStatistics(report with { Revision = 120, Seconds = 2 });
            Check(restored.MatchStatistics().RunId != report.RunId && restored.MatchStatistics().Continuation && restored.MatchStatistics().Revision == 0, "Rebobinado duplica el acumulado.");
        });
        test("Estadísticas: guardados antiguos sin informe siguen abriendo", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); var a = w.AddPlayer("Mago")!; w.Step();
            var save = w.SaveSurvival(a.Id, Guid.NewGuid().ToString("N"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(save.State.GetRawText())!.AsObject(); Check(node.Remove("Statistics"), "No encontró el campo del guardado.");
            var old = save with { State = JsonSerializer.SerializeToElement(node) };
            Check(GameWorld.RestoreSurvival(old).MatchStatistics().Continuation, "No admite el guardado anterior.");
        });
        test("Estadísticas: cola confirma solo la revisión enviada y nunca rebaja un final", () =>
        {
            var (w, _, _) = Arena(); var archive = new MatchStatisticsArchive(DirectoryForTest()); var r = w.MatchStatistics(); archive.Save(r);
            var next = r with { Revision = r.Revision + 60, Seconds = r.Seconds + 1, Status = MatchRecordStatus.Completed };
            archive.Flush((sent, _) => { archive.Save(next); return Task.CompletedTask; }).GetAwaiter().GetResult();
            int count = 0; archive.Save(r); archive.Flush((sent, _) => { count++; Check(sent.Complete && sent.Revision == next.Revision, "Se perdió la nueva revisión."); return Task.CompletedTask; }).GetAwaiter().GetResult();
            archive.Flush((_, _) => { count++; return Task.CompletedTask; }).GetAwaiter().GetResult(); Check(count == 1 && archive.LocalReports().Single().Complete, "Cola o historial incorrectos.");
        });
        test("Estadísticas: interrupción y red caída conservan el archivo pendiente", () =>
        {
            var (w, _, _) = Arena(); string dir = DirectoryForTest(); var archive = new MatchStatisticsArchive(dir); archive.Save(w.MatchStatistics());
            archive = new(dir); archive.RecoverInterrupted();
            try { archive.Flush((_, _) => Task.FromException(new IOException())).GetAwaiter().GetResult(); } catch (IOException) { }
            Check(archive.LocalReports().Single().Status == MatchRecordStatus.Interrupted && Directory.GetFiles(Path.Combine(dir, "pending"), "*.json").Length == 1, "Perdió el informe sin conexión.");
        });
        test("Estadísticas: informe corrupto aislado no bloquea otros envíos", () =>
        {
            var (w, _, _) = Arena(); string dir = DirectoryForTest(); var archive = new MatchStatisticsArchive(dir); archive.Save(w.MatchStatistics());
            File.WriteAllText(Path.Combine(dir, "pending", new string('b', 32) + ".json"), "{invalid"); int sent = 0;
            archive.Flush((_, _) => { sent++; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(sent == 1 && Directory.GetFiles(Path.Combine(dir, "pending"), "*.invalid-*").Length == 1, "No aisló la corrupción.");
        });
        test("Estadísticas: rechaza identidad, claves arbitrarias, duplicados y fechas imposibles", () =>
        {
            var (w, _, _) = Arena(); var r = w.MatchStatistics(); string json = r.ToJson();
            Reject(() => MatchStatisticsReport.Parse(json[..^1] + ",\"email\":\"private@example.com\"}"));
            Reject(() => (r with { Day = "2026-02-31" }).Validate());
            Reject(() => (r with { Metrics = [new(StatisticGroup.Recipe, 9, StatisticMeasure.Damage, 1)] }).Validate());
            Reject(() => (r with { Metrics = [new(StatisticGroup.Total, 0, StatisticMeasure.Damage, double.NaN)] }).Validate());
            Reject(() => (r with { Metrics = [r.Metrics[0], r.Metrics[0]] }).Validate());
        });
        test("Estadísticas: envío anónimo sin cabecera de sesión ni cookie", () =>
        {
            var (w, _, _) = Arena(); var handler = new Handler(request =>
            {
                Check(request.RequestUri!.AbsolutePath == "/analytics/matches" && request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "Se envió identidad.");
                return new(HttpStatusCode.Accepted);
            });
            using var http = new HttpClient(handler); using var transport = new MatchStatisticsTransport("demo-arcana", http, _ => Task.FromResult("ws://127.0.0.1:12345/play"));
            transport.Upload(w.MatchStatistics()).GetAwaiter().GetResult(); Check(handler.Calls == 1, "No envió el informe.");
        });
        test("Estadísticas: lector comprueba permiso y pagina informes acotados", () =>
        {
            var (w, _, _) = Arena(); var report = w.MatchStatistics();
            using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("statisticsReaders")
                ? "{\"fields\":{\"enabled\":{\"booleanValue\":true}}}"
                : JsonSerializer.Serialize(new { documents = new[] { new { fields = new { payload = new { stringValue = report.ToJson() } } } }, nextPageToken = "next-page" })) }));
            using var reader = new FirebaseProfileReader("demo-arcana", http);
            Check(reader.CanReadMatchStatistics("reader", "TOKEN").GetAwaiter().GetResult(), "Rol ignorado.");
            var page = reader.ReadMatchStatistics("TOKEN").GetAwaiter().GetResult(); Check(page.Reports.Single().RunId == report.RunId && page.NextPageToken == "next-page", "Página incorrecta.");
        });
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Task.FromResult(response(request)); }
    }
}
