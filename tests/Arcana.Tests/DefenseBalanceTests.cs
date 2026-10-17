using Arcana.Core;
using System.Collections;
using System.Reflection;
using System.Text.Json;

internal static class DefenseBalanceTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object Field(GameWorld world, string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(world)!;
    private static void Set(GameWorld world, string name, object value) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(world, value);
    private static void Call(GameWorld world, string name, params object[] values) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(world, values);
    private static GameWorld World(int bots = 0, DefenseFlow flow = DefenseFlow.Continuous, int seed = 37) => new(new WorldOptions
        { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = bots, DefenseFlow = flow, Seed = seed });
    private static Wizard Enemy(GameWorld world, V2 position)
    {
        Call(world, "SpawnMinion");
        var enemy = ((Dictionary<int, Wizard>)Field(world, "_combatants")).Values.Where(p => p.Team == 1).OrderBy(p => p.Id).Last();
        enemy.Position = position; return enemy;
    }
    private static void Send(GameWorld world, Wizard player, ClientMessage message)
    { message.Sequence = player.LastSequence + 1; world.ApplyMessage(player.Id, message); }
    private static void Cast(GameWorld world, Wizard player, params Element[] elements)
    {
        Send(world, player, new() { Type = "clear" });
        foreach (var element in elements) Send(world, player, new() { Type = "element", Element = element });
        Send(world, player, new() { Type = "cast" });
    }
    private sealed record KillSample(int Defenders, string Spell, float EnemyHealth, double Seconds);
    private static KillSample KillTime(int defenders, Element[] elements)
    {
        var world = World(defenders - 1); var human = world.AddPlayer("Humano")!;
        foreach (var bot in world.Players.Values.Where(p => p.Bot)) { bot.Health = 0; bot.Respawn = 10000; }
        world.Step(); Set(world, "_spawnClock", 10000f); // Solo el objetivo de medición, sin nuevas apariciones.
        human.Position = new(0, 0);
        var target = Enemy(world, new(3, 0));
        typeof(Wizard).GetProperty("Equipment")!.SetValue(target, target.Equipment with { Speed = 0 });
        var minion = ((IDictionary)Field(world, "_minions"))[target.Id]!;
        minion.GetType().GetField("Attack")!.SetValue(minion, 10000f);
        Send(world, human, new() { Type = "input", AimX = 3 });
        Cast(world, human, elements);
        float initial = target.MaxHealth; int ticks = 0;
        while (target.Alive && ticks < 3600) { human.LastInput = world.Time; world.Step(); ticks++; }
        return new(defenders, string.Join('+', elements), initial, ticks / 60.0);
    }
    private sealed record SurvivalSample(string Flow, int Defenders, int Seed, double Seconds, int LivesRemaining, int Kills, int PeakMinions);
    private static SurvivalSample Survive(int bots, DefenseFlow flow, int seed = 37, int seconds = 180)
    {
        var world = World(bots, flow, seed); var human = world.AddPlayer("Defensor")!;
        int peak = 0, ticks = 0;
        for (; ticks < seconds * 60 && !world.MatchEnded; ticks++)
        {
            var enemies = ((Dictionary<int, Wizard>)Field(world, "_combatants")).Values.Where(p => p.Team == 1 && p.Alive);
            if (human.Alive && ticks % 12 == 0)
            {
                var target = enemies.OrderBy(p => V2.Distance(p.Position, human.Position)).FirstOrDefault();
                Send(world, human, new() { Type = "input", AimX = target?.Position.X ?? 0, AimY = target?.Position.Y ?? 0 });
                if (human.ActionCooldown <= 0 && human.Health < human.MaxHealth * 0.45f)
                {
                    Send(world, human, new() { Type = "clear" });
                    for (int i = 0; i < 3; i++) Send(world, human, new() { Type = "element", Element = Element.Life });
                    Send(world, human, new() { Type = "self" });
                }
                else if (target != null && V2.Distance(target.Position, human.Position) < 17 &&
                    (human.Channel == null || human.Channel.Healing)) Cast(world, human, Element.Arcane, Element.Arcane, Element.Fire);
                else if (target == null && human.Channel != null) Send(world, human, new() { Type = "release" });
            }
            world.Step(); peak = Math.Max(peak, enemies.Count());
        }
        var state = world.Snapshot(); SnapshotValidation.Check(state);
        return new(flow.ToString(), bots + 1, seed, ticks / 60.0, Math.Max(0, DefenseRules.PlayerLives - human.Deaths), state.Defense!.TotalKills, peak);
    }
    private static DefenseSnapshot State(GameWorld world) => world.Snapshot().Defense!;
    private static object[] Plans(GameWorld world) => ((IDictionary)Field(world, "_defenseBotPlans")).Values.Cast<object>().ToArray();
    private static int PlanId(object plan, string field) => (int)plan.GetType().GetField(field)!.GetValue(plan)!;
    private static V2 Goal(object plan) => (V2)plan.GetType().GetField("Goal")!.GetValue(plan)!;
    private static void Advance(GameWorld world, int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
    private static Wizard TrainingEnemy(GameWorld world, V2 position)
    {
        var target = Enemy(world, position); target.Health = target.MaxHealth = 100000;
        typeof(Wizard).GetProperty("Equipment")!.SetValue(target, target.Equipment with { Speed = 0 });
        var minion = ((IDictionary)Field(world, "_minions"))[target.Id]!;
        minion.GetType().GetField("Attack")!.SetValue(minion, 10000f); return target;
    }
    public static void Run(Action<string, Action> test)
    {
        test("Defensa: escalado inicial acotado con uno a cuatro humanos o bots", () =>
        {
            float solo = 0;
            for (int count = 1; count <= 4; count++)
            {
                var world = World(); var humans = Enumerable.Range(0, count).Select(n => world.AddPlayer("Humano " + n)!).ToArray();
                world.Step(); float health = Enemy(world, new(0, 0)).MaxHealth;
                if (count == 1) solo = health;
                Check(health is >= 300 and <= 450 && health <= solo * 1.31f, "La vida inicial vuelve a dispararse con aliados.");
                var bots = World(count - 1); bots.AddPlayer("Humano"); bots.Step();
                Check(Enemy(bots, new(0, 0)).MaxHealth == health, "El número de bots altera la escala de vida respecto a humanos.");
                if (count == 4)
                {
                    Check(health / solo * DefenseRules.PartyPressure(count) < 2.6f, "Multiplicó en exceso vida y cantidad.");
                    foreach (var human in humans.Skip(1)) world.RemovePlayer(human.Id);
                    world.Step(); Check(Enemy(world, new(1, 0)).MaxHealth == solo, "Se quedó la dificultad del grupo que abandonó.");
                }
            }
        });
        test("Defensa: ataques reales derrotan minions iniciales en pocos segundos", () =>
        {
            foreach (int count in Enumerable.Range(1, 4))
            foreach (var spell in new[] { new[] { Element.Fire, Element.Fire, Element.Fire }, new[] { Element.Arcane, Element.Arcane, Element.Fire } })
            {
                var result = KillTime(count, spell);
                Check(result.Seconds is > 0.2 and < 4, $"{count} defensores, {result.Spell}: {result.Seconds:0.00} segundos.");
            }
        });
        test("Defensa: límite inicial de presión, tiempo gradual y tanques tardíos", () =>
        {
            var world = World(); world.AddPlayer("Humano")!.Position = new(76, 52); Advance(world, 2000);
            Check(State(world).Minions.Length is >= 20 and <= 24, "La horda inicial no alcanzó su nueva densidad o superó el límite inicial.");
            Check(DefenseRules.SpawnLimit(1, 4) == 48 && DefenseRules.SpawnLimit(40, 4) == DefenseRules.MaxMinions,
                "La horda no se adapta al grupo o excede el máximo de enemigos.");
            Check(DefenseRules.TimedDifficulty(89) == 1 && DefenseRules.TimedDifficulty(179) == 2, "El tiempo escaló demasiado deprisa.");
            for (int stage = 1; stage <= 4; stage++)
            {
                var sample = World(); sample.AddPlayer("Humano"); Set(sample, "_defenseDifficulty", stage);
                for (int i = 0; i < 30; i++) Enemy(sample, new(0, 0));
                Check(State(sample).Minions.All(m => m.Kind != MinionKind.Tank), "Apareció un tanque durante la preparación inicial.");
            }
        });
        test("Defensa: bots reparten amenazas, posiciones y mantienen la carga del rayo", () =>
        {
            var world = World(3); world.AddPlayer("Humano")!.Position = new(-25, 0);
            foreach (var bot in world.Players.Values.Where(p => p.Bot)) bot.Position = new(-25, 0);
            TrainingEnemy(world, new(-20, -2)); TrainingEnemy(world, new(-19, 0)); TrainingEnemy(world, new(-20, 2));
            world.Step(); var plans = Plans(world);
            Check(plans.Length == 3 && plans.Select(p => PlanId(p, "TargetId")).Distinct().Count() == 3, "Los tres bots persiguen al mismo objetivo.");
            Check(plans.All(p => world.Walkable(Goal(p))), "Asignó una posición fuera del mapa.");
            Advance(world, 240); var bots = world.Players.Values.Where(p => p.Bot).ToArray();
            Check(bots.All(p => p.Channel is { Healing: false } && p.BeamCharge > 0.95f), "Los bots reinician el rayo y pierden su carga.");
            for (int i = 0; i < bots.Length; i++) for (int j = i + 1; j < bots.Length; j++)
                Check(V2.Distance(bots[i].Position, bots[j].Position) > 2, "Los bots siguen apelotonados en combate.");
            SnapshotValidation.Check(world.Snapshot());
        });
        test("Defensa: un sanador por aliado herido mientras otros cubren amenazas", () =>
        {
            var world = World(3); var human = world.AddPlayer("Herido")!; human.Position = new(-25, 2); human.Health = 300;
            foreach (var bot in world.Players.Values.Where(p => p.Bot)) bot.Position = new(-25, 0);
            TrainingEnemy(world, new(-19, -2)); TrainingEnemy(world, new(-19, 0)); TrainingEnemy(world, new(-19, 2));
            world.Step(); var plans = Plans(world);
            Check(plans.Count(p => PlanId(p, "HealTargetId") == human.Id) == 1 && plans.Count(p => PlanId(p, "TargetId") != 0) == 2,
                "Todos dejaron la defensa para curar al mismo aliado.");
            Advance(world, 120); Check(human.Health > 300, "El sanador no curó al aliado real.");
        });
        test("Defensa: el bot que se cura no atrae a todos los sanadores", () =>
        {
            var world = World(3); world.AddPlayer("Humano"); Set(world, "_spawnClock", 10000f);
            var wounded = world.Players.Values.Where(p => p.Bot).OrderBy(p => p.Id).Skip(1).First(); wounded.Health = 300;
            TrainingEnemy(world, new(-20, 0)); world.Step();
            var healing = Plans(world).Where(p => PlanId(p, "HealTargetId") != 0).ToArray();
            Check(healing.Count(p => PlanId(p, "HealTargetId") == wounded.Id) == 1, "Dos bots curan al bot que ya se está curando.");
            Check(Plans(world).Count(p => PlanId(p, "TargetId") != 0) == 2, "No mantuvo los otros dos puestos de ataque.");
        });
        test("Defensa: un objetivo compartido no agrupa posiciones y una baja libera la asignación", () =>
        {
            var world = World(3); world.AddPlayer("Humano"); Set(world, "_spawnClock", 10000f); var enemy = TrainingEnemy(world, new(-20, 0));
            world.Step(); var plans = Plans(world);
            Check(plans.All(p => PlanId(p, "TargetId") == enemy.Id) && plans.Select(Goal).Distinct().Count() == 3, "No comparten una amenaza única desde posiciones distintas.");
            enemy.Health = 0; Advance(world, 40);
            Check(Plans(world).All(p => PlanId(p, "TargetId") == 0), "Conservó el objetivo muerto.");
            Advance(world, 240); var bots = world.Players.Values.Where(p => p.Bot).ToArray();
            Check(bots.All(p => V2.Distance(p.Position, world.Players.Values.Single(p => !p.Bot).Position) < 18), "Los bots abandonaron al humano sin amenazas.");
            for (int i = 0; i < bots.Length; i++) for (int j = i + 1; j < bots.Length; j++)
                Check(V2.Distance(bots[i].Position, bots[j].Position) > 2, "No repartió los puestos de guardia.");
        });
    }
    public static void Profile(string path, bool verify)
    {
        var kills = Enumerable.Range(1, 4).SelectMany(n => new[] { new[] { Element.Fire }, new[] { Element.Fire, Element.Fire, Element.Fire },
            new[] { Element.Arcane, Element.Arcane, Element.Fire } }.Select(spell => KillTime(n, spell))).ToArray();
        var survival = new[] { DefenseFlow.Continuous }.SelectMany(flow => Enumerable.Range(0, 4).Select(n => Survive(n, flow))).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { kills, survival, note = "Simulación con hechizos reales, sin equipo inicial, sin oro añadido ni invulnerabilidad. El humano apunta al enemigo más cercano y se cura." }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var k in kills) Console.WriteLine($"DEFENSE_KILL_TIME: {k.Defenders} defensores · {k.Spell} · {k.EnemyHealth:0} vida · {k.Seconds:0.00} s");
        foreach (var s in survival) Console.WriteLine($"DEFENSE_SURVIVAL: {s.Flow} · {s.Defenders} defensores · {s.Seconds:0} s · vidas {s.LivesRemaining} · bajas {s.Kills} · pico {s.PeakMinions}");
        if (verify)
        {
            Check(kills.All(k => k.Seconds < (k.Spell == "Fire" ? 7 : 4)), "Un minion inicial tarda demasiado en caer.");
            Check(survival.All(s => s.Seconds >= 180 && s.LivesRemaining > 0), "El comienzo no permite aprender y combatir durante tres minutos.");
            Console.WriteLine("DEFENSE_BALANCE_OK");
        }
    }
}
