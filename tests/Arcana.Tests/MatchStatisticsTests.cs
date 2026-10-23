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
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException) { return; } throw new Exception("Se aceptó un informe inválido."); }
    public static void Run(Action<string, Action> test)
    {
        test("Estadísticas: separa fuego directo, áreas, aura y quemadura sin duplicar totales", () =>
        {
            var (w, a, b) = Arena(); b.Health = b.MaxHealth = 500; b.Shield = 20;
            foreach (var (attack, damage) in new[] { (StatisticAttack.Spray, 40f), (StatisticAttack.Nova, 15f), (StatisticAttack.FireAura, 10f), (StatisticAttack.Burn, 5f) })
                Call(w, "DamageAs", attack, b, damage, a.Id, new[] { Element.Fire });
            var r = MatchStatisticsReport.Parse(w.MatchStatistics().ToJson());
            int fire = (int)Element.Fire;
            double Part(StatisticDamageSource source, StatisticMeasure measure) => r.Sum(StatisticGroup.HumanElementSource, measure, (int)source * 9 + fire);
            Check(Part(StatisticDamageSource.Direct, StatisticMeasure.Damage) == 20 && Part(StatisticDamageSource.Direct, StatisticMeasure.ShieldDamage) == 20 &&
                Part(StatisticDamageSource.Area, StatisticMeasure.Damage) == 15 && Part(StatisticDamageSource.Aura, StatisticMeasure.Damage) == 10 &&
                Part(StatisticDamageSource.Burn, StatisticMeasure.Damage) == 5, "El daño pasivo se atribuye al ataque directo.");
            foreach (var measure in new[] { StatisticMeasure.Damage, StatisticMeasure.ShieldDamage })
                Check(r.Sum(StatisticGroup.HumanElementSource, measure) == r.Sum(StatisticGroup.HumanElement, measure) &&
                    r.Sum(StatisticGroup.Total, measure) == r.Sum(StatisticGroup.HumanElement, measure), "El desglose duplica o pierde daño.");
        });
        test("Estadísticas: las áreas mixtas conservan la ponderación real y el entorno tiene su propio origen", () =>
        {
            var (w, a, b) = Arena(); b.Health = b.MaxHealth = 500;
            Call(w, "HitAs", StatisticAttack.Meteor, b, new SpellRecipe([Element.Fire, Element.Arcane]), .1f, a.Id, V2.Zero);
            var r = w.MatchStatistics();
            foreach (var element in new[] { Element.Fire, Element.Arcane })
                Check(r.Sum(StatisticGroup.HumanElementSource, StatisticMeasure.Damage, 9 + (int)element) ==
                    r.Sum(StatisticGroup.HumanElement, StatisticMeasure.Damage, (int)element), "La mezcla del área cambia de elemento.");
            Call(w, "DamageAs", StatisticAttack.FireAura, b, 3f, -1, new[] { Element.Fire });
            r = w.MatchStatistics();
            Check(r.Sum(StatisticGroup.EnvironmentElementSource, StatisticMeasure.Damage, 18 + (int)Element.Fire) == 3,
                "El daño del entorno se atribuye al humano.");
            Reject(() => (r with { Metrics = [new(StatisticGroup.HumanElementSource, 36, StatisticMeasure.Damage, 1)] }).Validate());
        });
        test("Gráficas: la escala automática hace visibles los valores pequeños y mantiene ejes reversibles", () =>
        {
            var scale = new MatchStatisticsScale([0, 1, 1000, 100000000]);
            Check(scale.Logarithmic && scale.Normalize(0) == 0 && scale.Normalize(100000000) == 1 && scale.Normalize(1) > .03,
                "El valor pequeño sigue aplastado o el cero no parte del eje.");
            foreach (double v in new[] { 0, 1, 1000, 100000000 })
                Check(Math.Abs(scale.ValueAt(scale.Normalize(v)) - v) <= Math.Max(1e-8, v * 1e-10), "Los rótulos del eje no corresponden al valor.");
            Check(!new MatchStatisticsScale([4, 5, 10]).Logarithmic && !new MatchStatisticsScale([1, 100000000], false).Logarithmic,
                "No respeta la escala lineal.");
            var empty = new MatchStatisticsScale([0, 0], true);
            Check(empty.Maximum == 1 && empty.Normalize(0) == 0 && double.IsFinite(empty.ValueAt(.5)), "La selección vacía produce un eje inválido.");
            var tiny = new MatchStatisticsScale([.00001, 100000000]);
            Check(tiny.Normalize(.00001) > .02 && tiny.ValueAt(0) == 0, "El daño fraccional desaparece.");
        });
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
        test("Estadísticas: supervivencia guarda solo estado y abre un tramo nuevo sin telemetría local", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); var a = w.AddPlayer("Mago")!;
            for (int i = 0; i < 60; i++) w.Step();
            var save = w.SaveSurvival(a.Id, Guid.NewGuid().ToString("N")); var report = w.MatchStatistics();
            Check(!save.State.TryGetProperty("Statistics", out _), "El punto local contiene un informe de telemetría.");
            var restored = GameWorld.RestoreSurvival(save);
            Check(restored.Tick == w.Tick && restored.MatchStatistics().RunId != report.RunId && restored.MatchStatistics().Continuation && restored.MatchStatistics().Revision == 0, "Continuar pierde el estado del juego o duplica el acumulado anterior.");
        });
        test("Estadísticas: guardados antiguos con informe siguen abriendo sin reescribir telemetría", () =>
        {
            var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4 }); var a = w.AddPlayer("Mago")!; w.Step();
            var save = w.SaveSurvival(a.Id, Guid.NewGuid().ToString("N"));
            var node = System.Text.Json.Nodes.JsonNode.Parse(save.State.GetRawText())!.AsObject();
            node["Statistics"] = JsonSerializer.SerializeToNode(w.MatchStatistics(MatchRecordStatus.Suspended));
            var old = save with { State = JsonSerializer.SerializeToElement(node) };
            var restored = GameWorld.RestoreSurvival(old);
            Check(restored.MatchStatistics().Continuation && restored.MatchStatistics().Revision == 0 && restored.Tick == w.Tick, "No admite el punto anterior con telemetría.");
            var next = restored.SaveSurvival(a.Id, Guid.NewGuid().ToString("N"));
            Check(!next.State.TryGetProperty("Statistics", out _), "Continuar un punto antiguo volvió a escribir telemetría.");
        });
        test("Estadísticas: reintentos en memoria conservan una revisión nueva durante el envío", () =>
        {
            var (w, _, _) = Arena(); var outbox = new MatchStatisticsOutbox(); var r = w.MatchStatistics(MatchRecordStatus.Left); outbox.Enqueue(r);
            var next = r with { Revision = r.Revision + 60, Seconds = r.Seconds + 1, Status = MatchRecordStatus.Completed };
            outbox.Flush((sent, _) => { outbox.Enqueue(next); return Task.CompletedTask; }).GetAwaiter().GetResult();
            int count = 0; Check(!outbox.Enqueue(r), "Una revisión antigua reemplazó el final.");
            outbox.Flush((sent, _) => { count++; Check(sent.Complete && sent.Revision == next.Revision, "Se perdió la nueva revisión."); return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(count == 1 && outbox.PendingCount == 0, "La confirmación perdió o duplicó una revisión.");
        });
        test("Estadísticas: la cola solo vive en memoria y aísla los contadores del mundo", () =>
        {
            var (w, _, _) = Arena(); var report = w.MatchStatistics(MatchRecordStatus.Completed); var outbox = new MatchStatisticsOutbox();
            Check(!outbox.Enqueue(w.MatchStatistics()), "Se encoló una partida en curso.");
            string expected = report.ToJson(); outbox.Enqueue(report);
            report.Metrics[0] = report.Metrics[0] with { Value = 999999 };
            outbox.Flush((sent, _) => { Check(sent.ToJson() == expected, "La simulación modificó el informe ya cerrado."); return Task.CompletedTask; }).GetAwaiter().GetResult();
            Check(outbox.PendingCount == 0 && new MatchStatisticsOutbox().PendingCount == 0, "La cola mantiene un historial o recupera archivos.");
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
        test("Estadísticas: envío directo a Firestore sin sesión, cookie ni relé", () =>
        {
            var (w, _, _) = Arena(); var report = w.MatchStatistics(MatchRecordStatus.Completed);
            var handler = new Handler(request =>
            {
                Check(request.Method == HttpMethod.Post && request.RequestUri!.Scheme == "https" && request.RequestUri.Host == "firestore.googleapis.com" &&
                    request.RequestUri.AbsolutePath == "/v1/projects/demo-arcana/databases/(default)/documents/matchStatistics", "El informe pasó por un servidor intermedio.");
                Check(request.Headers.Authorization == null && !request.Headers.Contains("Cookie"), "Se envió identidad.");
                Check(request.RequestUri!.Query.Contains("documentId=" + report.RunId + "-" + report.Revision + "-1"), "La clave no es idempotente.");
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var fields = body.RootElement.GetProperty("fields");
                Check(fields.EnumerateObject().Count() == 10 && fields.GetProperty("runId").GetProperty("stringValue").GetString() == report.RunId &&
                    fields.GetProperty("status").GetProperty("integerValue").GetString() == "1" && fields.GetProperty("complete").GetProperty("booleanValue").GetBoolean(), "Cabecera Firestore incorrecta.");
                Check(MatchStatisticsReport.Parse(fields.GetProperty("payload").GetProperty("stringValue").GetString()!).ToJson() == report.ToJson(), "Alteró el daño acumulado.");
                return new(HttpStatusCode.OK);
            });
            using var http = new HttpClient(handler); using var transport = new MatchStatisticsTransport("demo-arcana", http);
            transport.Upload(report).GetAwaiter().GetResult(); Check(handler.Calls == 1, "No envió el informe.");
            bool rejected = false;
            try { transport.Upload(w.MatchStatistics()).GetAwaiter().GetResult(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && handler.Calls == 1, "Intentó subir una partida todavía en curso.");
        });
        test("Estadísticas: Firestore confirma duplicados y conserva la cola ante permisos, cuota y red", () =>
        {
            var (w, _, _) = Arena(); var report = w.MatchStatistics(MatchRecordStatus.Completed);
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.Conflict })
            {
                var outbox = new MatchStatisticsOutbox(); outbox.Enqueue(report);
                using var http = new HttpClient(new Handler(_ => new(status) { Content = new StringContent("{\"error\":{\"status\":\"ABORTED\"}}") }));
                using var transport = new MatchStatisticsTransport("demo-arcana", http);
                try { outbox.Flush(transport.Upload).GetAwaiter().GetResult(); throw new Exception("Se confirmó un envío fallido."); }
                catch (IOException) { }
                Check(outbox.PendingCount == 1, "El fallo borró el informe pendiente.");
                using var success = new HttpClient(new Handler(_ => new(HttpStatusCode.Conflict) { Content = new StringContent("{\"error\":{\"status\":\"ALREADY_EXISTS\"}}") }));
                using var retry = new MatchStatisticsTransport("demo-arcana", success);
                outbox.Flush(retry.Upload).GetAwaiter().GetResult();
                Check(outbox.PendingCount == 0, "El reintento duplicado no confirmó la cola en memoria.");
            }
        });
        test("Estadísticas: lector comprueba permiso, omite informes corruptos y conserva la paginación", () =>
        {
            var (w, _, _) = Arena(); var report = w.MatchStatistics(MatchRecordStatus.Completed);
            using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("statisticsReaders")
                ? "{\"fields\":{\"enabled\":{\"booleanValue\":true}}}"
                : JsonSerializer.Serialize(new { documents = new object[] { new { fields = new { payload = new { stringValue = w.MatchStatistics().ToJson() } } }, new { fields = new { } }, new { fields = new { payload = new { stringValue = "{broken" } } },
                    new { fields = new { payload = new { integerValue = "1" } } }, new { fields = new { payload = new { stringValue = report.ToJson() } } } }, nextPageToken = "next-page" })) }));
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
