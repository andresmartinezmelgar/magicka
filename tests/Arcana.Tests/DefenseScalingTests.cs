using Arcana.Core;
using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class DefenseScalingTests
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static void Near(float actual, float expected, string reason) =>
        Check(Math.Abs(actual - expected) < Math.Max(.002f, Math.Abs(expected) * .00001f), reason + $" ({actual} / {expected})");
    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static object Get(object owner, string name) => Field(owner, name).GetValue(owner)!;
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    private static object? Call(GameWorld world, string name, params object[] args) =>
        typeof(GameWorld).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static (GameWorld World, Wizard Human) World(int bots = 0)
    {
        var w = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            SurvivalMap = SurvivalMapSpec.Legacy, MaxPlayers = 4, Bots = bots, Seed = 71 });
        var p = w.AddPlayer("Defensor")!; p.Position = new(-25, 0); p.Invulnerable = 100000;
        Set(w, "_bossClock", 10000f); Set(w, "_spawnClock", 10000f);
        return (w, p);
    }
    private static object Spawn(GameWorld world, MinionKind kind)
    {
        Check((bool)Call(world, "TrySpawnMinion", kind)!, "No se pudo preparar el enemigo.");
        return ((IDictionary)Get(world, "_minions")).Values.Cast<object>().OrderBy(m => ((Wizard)Get(m, "Body")).Id).Last();
    }
    private static Wizard Body(object enemy) => (Wizard)Get(enemy, "Body");
    private static int Prisms(GameWorld world) => world.Snapshot().Defense!.Minions.Count(m => m.Kind == MinionKind.Prism);
    private static void Update(GameWorld world) => Call(world, "UpdateDefensePartyScaling");
    private sealed class PrismRandom() : Random(71)
    {
        // Dificultad 3: la primera franja de la tabla corresponde al prisma.
        public override int Next(int maximum) => maximum == 100 ? 0 : base.Next(maximum);
    }
    private static void NaturalAttempts(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Call(world, "SpawnMinion");
            foreach (var enemy in ((IDictionary)Get(world, "_minions")).Values.Cast<object>())
                if ((MinionKind)Get(enemy, "Kind") != MinionKind.Prism) Body(enemy).Health = 0;
            Call(world, "CleanupDefense");
        }
    }

    public static void Run(Action<string, Action> test)
    {
        test("Escalado: vida y daño del enemigo existente cambian sin restaurar el porcentaje perdido", () =>
        {
            var (w, p) = World(); var enemy = Spawn(w, MinionKind.Melee); var body = Body(enemy);
            float soloHealth = body.MaxHealth; body.Health *= .4f;
            var other = w.AddPlayer("Segundo")!; other.Position = new(-30, 0);
            Update(w);
            Near(body.MaxHealth, soloHealth * 1.2f, "No aumentó la vida con el segundo defensor.");
            Near(body.Health / body.MaxHealth, .4f, "Entrar curó al enemigo.");
            Near((float)Get(enemy, "Strength"), 1.1f, "No aumentó la potencia.");
            body.Position = p.Position + new V2(1, 0); p.Invulnerable = 0; float health = p.Health;
            Set(enemy, "Attack", 0f); Call(w, "TickMinion", enemy, GameWorld.StepSeconds);
            Near(health - p.Health, DefenseRules.MinionMeleeAttack(MinionKind.Melee) * 1.1f, "El golpe real no usa el nuevo daño.");
            int spawned = w.Snapshot().Defense!.Bestiary.Single(b => b.Kind == MinionKind.Melee).Spawned;
            w.RemovePlayer(other.Id); Update(w);
            Near(body.MaxHealth, soloHealth, "Se conservó la vida del grupo que salió.");
            Near(body.Health / body.MaxHealth, .4f, "Salir restauró el porcentaje perdido.");
            Near((float)Get(enemy, "Strength"), 1, "Se conservó el daño del grupo anterior.");
            Check(w.Snapshot().Defense!.Bestiary.Single(b => b.Kind == MinionKind.Melee).Spawned == spawned,
                "Recalcular el bestiario contó otra aparición.");
        });
        test("Escalado: carga, desconexión, muerte, reaparición y eliminación usan defensores activos", () =>
        {
            var (w, _) = World(); var enemy = Spawn(w, MinionKind.Melee); float solo = Body(enemy).MaxHealth;
            var other = w.AddPlayer("Segundo")!;
            w.SetDefensePlayerActive(other.Id, false); Update(w);
            Near(Body(enemy).MaxHealth, solo, "Cargar aumenta la dificultad antes de participar.");
            w.SetDefensePlayerActive(other.Id, true); Update(w);
            Near(Body(enemy).MaxHealth, solo * 1.2f, "Terminar la carga no reactiva el escalado.");
            w.SuspendPlayer(other.Id); Update(w);
            Near(Body(enemy).MaxHealth, solo, "La reserva de reconexión mantiene la dificultad.");
            w.SetDefensePlayerActive(other.Id, true); other.Health = 0; other.Respawn = 3; Update(w);
            Near(Body(enemy).MaxHealth, solo, "Un defensor caído sigue contando.");
            other.Health = other.MaxHealth; other.Respawn = 0; Update(w);
            Near(Body(enemy).MaxHealth, solo * 1.2f, "Reaparecer no recupera el escalado.");
            other.Deaths = DefenseRules.PlayerLives; Update(w);
            Near(Body(enemy).MaxHealth, solo, "Un eliminado sigue aumentando la dificultad.");
        });
        test("Escalado: los aliados IA vivos cuentan y los caídos dejan de hacerlo", () =>
        {
            var (w, _) = World(3); Update(w);
            var enemy = Spawn(w, MinionKind.Boss); var body = Body(enemy);
            Near(body.MaxHealth, DefenseRules.MinionHealth(MinionKind.Boss, 1, 4), "El coloso ignora a los aliados activos.");
            Near((float)Get(enemy, "Strength"), 1.3f, "La potencia no reconoce cuatro defensores.");
            foreach (var bot in w.Players.Values.Where(p => p.Bot)) { bot.Health = 0; bot.Respawn = 5; }
            Update(w);
            Near(body.MaxHealth, DefenseRules.MinionHealth(MinionKind.Boss, 1, 1), "Los bots caídos siguen contando.");
        });
        test("Prismas: máximo dos o tres, de uno en uno y con pausa tras morir", () =>
        {
            for (int defenders = 1; defenders <= 4; defenders++)
            {
                var (w, _) = World();
                for (int i = 1; i < defenders; i++) w.AddPlayer("Aliado " + i);
                Update(w); Set(w, "_defenseDifficulty", 3); Set(w, "_random", new PrismRandom());
                int limit = defenders <= 2 ? 2 : 3;
                for (int i = 0; i < limit; i++)
                {
                    Set(w, "_prismSpawnCooldown", 0f); NaturalAttempts(w, 1);
                    Check(Prisms(w) == i + 1, "No aparece un único prisma al terminar su intervalo.");
                    float gap = (float)Get(w, "_prismSpawnCooldown");
                    Check(gap is >= 45 and <= 75, "El intervalo no está entre 45 y 75 segundos.");
                    NaturalAttempts(w, 30); Check(Prisms(w) == i + 1, "Las apariciones se acumulan en una ráfaga.");
                }
                Set(w, "_prismSpawnCooldown", 0f); NaturalAttempts(w, 50);
                Check(Prisms(w) == limit, "Se superó el máximo del grupo.");
                var victim = ((IDictionary)Get(w, "_minions")).Values.Cast<object>()
                    .First(m => (MinionKind)Get(m, "Kind") == MinionKind.Prism);
                Body(victim).Health = 0;
                Call(w, "DefenseDeath", Body(victim), w.Players.Values.First(p => !p.Bot).Id);
                Near((float)Get(w, "_prismSpawnCooldown"), 30, "La espera no empieza en el instante de la muerte.");
                NaturalAttempts(w, 1); // La generación ocurre antes de limpiar muertos en el bucle normal.
                Call(w, "CleanupDefense");
                Near((float)Get(w, "_prismSpawnCooldown"), 30, "Matar al prisma no retrasa su reemplazo.");
                NaturalAttempts(w, 50); Check(Prisms(w) == limit - 1, "El prisma reaparece inmediatamente.");
            }
        });
        test("Prismas: bajar a dos defensores retira el excedente sin botín ni bajas", () =>
        {
            var (w, _) = World(3); Update(w);
            for (int i = 0; i < 3; i++) Spawn(w, MinionKind.Prism);
            var before = w.Snapshot().Defense!;
            foreach (var bot in w.Players.Values.Where(p => p.Bot).Take(2)) bot.Health = 0;
            Update(w); var after = w.Snapshot().Defense!;
            Check(Prisms(w) == 2 && after.TotalKills == before.TotalKills && after.Drops.Length == before.Drops.Length,
                "El excedente no se retiró o concedió recompensas.");
            Check(after.Bestiary.Single(b => b.Kind == MinionKind.Prism).Spawned == 3, "Cambiar el grupo inventa apariciones.");
        });
        test("Prismas: cambiar de zona no los recoloca constantemente junto al jugador", () =>
        {
            var (w, _) = World(); var prism = Body(Spawn(w, MinionKind.Prism));
            prism.Position = new(70, 40); var original = prism.Position;
            var normal = Body(Spawn(w, MinionKind.Melee)); normal.Position = new(65, 35);
            for (int i = 0; i < 6; i++) Call(w, "ReturnDistantMinion");
            Check(prism.Position == original && normal.Position != new V2(65, 35), "Se recoloca al prisma o no se ejercitó la recolocación normal.");
            Set(w, "_prismSpawnCooldown", 0f); Set(w, "_defenseDifficulty", 3); Set(w, "_random", new PrismRandom());
            var runtime = ((IDictionary)Get(w, "_minions"))[prism.Id]!;
            Call(w, "RetireDefenseMinion", runtime); NaturalAttempts(w, 30);
            Check(Prisms(w) == 0, "Retirar un prisma por rendimiento provocó un reemplazo inmediato.");
        });
        test("Guardado: conserva escalado e intervalo; partidas anteriores reciben una espera inicial", () =>
        {
            var (w, p) = World(1); Update(w); var enemy = Spawn(w, MinionKind.Prism);
            Body(enemy).Health *= .4f; Set(w, "_prismSpawnCooldown", 61.5f);
            var save = w.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save);
            Near((float)Get(restored, "_prismSpawnCooldown"), 61.5f, "Continuar reinició la espera del prisma.");
            var after = restored.Snapshot().Defense!.Minions.Single();
            Near(after.Health, Body(enemy).Health, "Continuar alteró la herida del enemigo.");
            Near(after.MaxHealth, Body(enemy).MaxHealth, "Continuar duplicó el escalado.");
            var state = JsonNode.Parse(save.State.GetRawText())!.AsObject(); state.Remove("PrismSpawnCooldown");
            foreach (var node in state["Minions"]!.AsArray())
            {
                var minion = node!.AsObject(); minion.Remove("PartySize");
                minion["Strength"] = minion["Strength"]!.GetValue<float>() / DefenseRules.PartyDamageMultiplier(2);
                var body = minion["Body"]!.AsObject();
                body["MaxHealth"] = body["MaxHealth"]!.GetValue<float>() * 1.1f / 1.2f;
                body["Health"] = body["Health"]!.GetValue<float>() * 1.1f / 1.2f;
                var gear = body["Equipment"]!.AsObject();
                gear["Power"] = gear["Power"]!.GetValue<float>() / DefenseRules.PartyDamageMultiplier(2);
            }
            var legacy = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(state) });
            Near((float)Get(legacy, "_prismSpawnCooldown"), 45, "Un guardado antiguo reaparece sin espera.");
            Near(legacy.Snapshot().Defense!.Minions.Single().Health / legacy.Snapshot().Defense!.Minions.Single().MaxHealth,
                .4f, "Migrar un guardado antiguo restauró vida perdida.");
            Near(legacy.Snapshot().Defense!.Minions.Single().MaxHealth, Body(enemy).MaxHealth, "La migración duplica el escalado anterior.");
        });
        test("Guardado antiguo: ocho prismas se reducen al límite sin regalar bajas ni botín", () =>
        {
            var (w, p) = World();
            for (int i = 0; i < 8; i++) Spawn(w, MinionKind.Prism);
            var save = w.SaveSurvival(p.Id, Guid.NewGuid().ToString("N"));
            var state = JsonNode.Parse(save.State.GetRawText())!.AsObject(); state.Remove("PrismSpawnCooldown");
            foreach (var minion in state["Minions"]!.AsArray()) minion!.AsObject().Remove("PartySize");
            var restored = GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(state) });
            Check(Prisms(restored) == 2 && restored.Snapshot().Defense!.TotalKills == 0 &&
                restored.Snapshot().Defense!.Drops.Length == 0, "Se conservan los ocho prismas o se premia su retirada.");
            Check(restored.Snapshot().Defense!.Bestiary.Single(b => b.Kind == MinionKind.Prism).Spawned == 8,
                "La retirada altera la cantidad histórica de apariciones.");
        });
    }
}
