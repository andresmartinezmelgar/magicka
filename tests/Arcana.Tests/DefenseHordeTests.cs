using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class DefenseHordeTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameWorld World() => new(new WorldOptions { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, Seed = 214, MaxPlayers = 4 });
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Invoke(GameWorld w, string name, params object[] args) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
    private static DefenseSnapshot State(GameWorld w) => w.Snapshot().Defense!;
    private static (Wizard Body, object Runtime) Enemy(GameWorld w, MinionKind kind, V2 position)
    {
        Check((bool)Invoke(w, "TrySpawnMinion", kind)!, "No se pudo crear el enemigo.");
        var m = ((IDictionary)Field(w, "_minions"))[State(w).Minions.Last().Id]!;
        var body = (Wizard)Field(m, "Body"); body.Position = position;
        return (body, m);
    }
    private static bool Fps(GameWorld w, Wizard p, float fps) => w.ApplyMessage(p.Id, new()
        { Type = "defense_performance", FramesPerSecond = fps, Sequence = p.LastSequence + 1 });
    private static void Step(GameWorld w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

    public static void Run(Action<string, Action> test)
    {
        test("Supervivencia: FPS sostenidos, reducción inmediata y recuperación gradual sin oscilación", () =>
        {
            var budget = new DefensePerformanceBudget();
            Check(budget.Limit == 96, "Límite inicial incorrecto.");
            Check(budget.Observe(20) && budget.Limit == 48, "No redujo para el dispositivo lento.");
            budget.Observe(60); budget.Observe(60);
            Check(budget.Limit == 48, "Aumentó sin tres ventanas estables.");
            budget.Observe(20); budget.Observe(60); budget.Observe(60);
            Check(budget.Limit == 48, "Una caída no reinició la recuperación.");
            budget.Observe(60); Check(budget.Limit == 64, "Saltó varias bandas de golpe.");
            for (int i = 0; i < 30; i++) budget.Observe(120);
            Check(budget.Limit == DefenseRules.MaxMinions, "El dispositivo fluido no permite la horda máxima.");
            foreach (float fps in new[] { float.NaN, float.PositiveInfinity, -1, 0, 241 })
                Check(!budget.Observe(fps) && budget.Limit == DefenseRules.MaxMinions, "Aceptó FPS inválidos.");
        });
        test("Supervivencia: la sala respeta el dispositivo más lento y los FPS no cuentan como actividad", () =>
        {
            var w = World(); var fast = w.AddPlayer("Rápido")!; var slow = w.AddPlayer("Lento")!;
            fast.Invulnerable = slow.Invulnerable = 1000;
            Check(Fps(w, slow, 20), "Rechazó FPS válidos.");
            Check(State(w).PerformanceLimit == 48, "No cuenta el dispositivo lento.");
            Check(!Fps(w, slow, 60), "Aceptó informes sin intervalo.");
            for (int i = 0; i < 9; i++) { Step(w, 301); Fps(w, fast, 60); Fps(w, slow, 20); }
            Check(State(w).PerformanceLimit == 48, "La máquina rápida impuso su límite.");
            Check(State(w).Defenders.All(p => p.ActiveSeconds == 0), "La telemetría generó tiempo activo y recompensas.");
            w.RemovePlayer(slow.Id);
            Check(State(w).PerformanceLimit == DefenseRules.MaxMinions, "No liberó el límite al salir el lento.");
            fast.Invulnerable = 0;
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: al bajar FPS se retiran solo enemigos lejanos sin bajas, experiencia ni botín", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(-25, 0);
            for (int i = 0; i < 45; i++) Enemy(w, MinionKind.Melee, new(76, 52));
            var close = Enemy(w, MinionKind.Tank, new(-23, 0)).Body;
            var boss = Enemy(w, MinionKind.Boss, new(76, 52)).Body;
            Check(Fps(w, p, 15), "Informe lento rechazado.");
            Check(State(w).Minions.Length == State(w).MinionLimit && State(w).Minions.Any(m => m.Id == close.Id) &&
                State(w).Minions.Any(m => m.Id == boss.Id), "Retiró una amenaza cercana o el coloso.");
            Check(State(w).TotalKills == 0 && State(w).Drops.Length == 0 && State(w).Defenders.Single().Experience == 0,
                "Retirar enemigos dio premios.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: diez tipos válidos por red y variantes nuevas progresivas", () =>
        {
            var w = World(); w.AddPlayer("Mago"); Set(w, "_defenseDifficulty", 10);
            foreach (var kind in Enum.GetValues<MinionKind>()) Enemy(w, kind, new(0, 0));
            var snapshot = w.Snapshot() with { Time = 900 };
            var decoded = Protocol.Deserialize<ServerMessage>(Protocol.Serialize(new ServerMessage { Type = "snapshot", Snapshot = snapshot }))!.Snapshot!;
            Check(decoded.Defense!.Bestiary.Length == 10 && decoded.Defense.Minions.Select(m => m.Kind).Distinct().Count() == 10,
                "Se perdieron tipos en la red o guía.");
            SnapshotValidation.Check(decoded);
            var early = World(); early.AddPlayer("Mago");
            for (int i = 0; i < 80; i++) Invoke(early, "SpawnMinion");
            Check(State(early).Minions.All(m => m.Kind == MinionKind.Melee), "Apareció un enemigo especial en el primer nivel.");
            var late = World(); late.AddPlayer("Mago"); Set(late, "_defenseDifficulty", 10);
            for (int i = 0; i < DefenseRules.MaxMinions; i++) Invoke(late, "SpawnMinion");
            foreach (var kind in new[] { MinionKind.Runner, MinionKind.Brute, MinionKind.Bomber, MinionKind.Shaman })
                Check(State(late).Minions.Any(m => m.Kind == kind), "El generador no incluye " + kind);
        });
        test("Supervivencia: el generador respeta el tope de FPS aun en dificultad avanzada", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(76, 52); p.Invulnerable = 1000;
            Set(w, "_defenseElapsed", 1800.0);
            Check(Fps(w, p, 15), "No aceptó el presupuesto lento.");
            Step(w, 2400);
            Check(State(w).PerformanceLimit == 32 && State(w).MinionLimit == 32 && State(w).Minions.Length is >= 24 and <= 32,
                "El generador ignoró los FPS o dejó de generar enemigos.");
        });
        test("Supervivencia: bruto y detonador avisan, permiten escapar y se interrumpen al congelar", () =>
        {
            foreach (var kind in new[] { MinionKind.Brute, MinionKind.Bomber })
            {
                var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(-25, 0); p.Invulnerable = 0;
                var (body, runtime) = Enemy(w, kind, new(-24, 0)); Set(runtime, "Attack", 0f);
                Invoke(w, "TickMinion", runtime, 1 / 60f);
                Check((float)Field(runtime, "Windup") > 0 && p.Health == p.MaxHealth, "Golpeó sin aviso.");
                body.Frozen = 2; Invoke(w, "TickMinion", runtime, 0.1f);
                Check((float)Field(runtime, "Windup") == 0 && p.Health == p.MaxHealth && body.Alive, "Congelar no canceló el ataque.");
                body.Frozen = 0; Set(runtime, "Attack", 0f); Invoke(w, "TickMinion", runtime, 1 / 60f);
                p.Position = new(-30, 0); Invoke(w, "TickMinion", runtime, 1.2f);
                Check(p.Health == p.MaxHealth, "El área persiguió al jugador que escapó.");
                if (kind == MinionKind.Bomber)
                    Check(!body.Alive && State(w).TotalKills == 1, "El detonador no murió o su baja quedó inconsistente.");
            }
        });
        test("Supervivencia: ataque de área respeta escudos y el chamán cura aliados con límite y alcance", () =>
        {
            var w = World(); var p = w.AddPlayer("Mago")!; p.Position = new(-25, 0); p.Invulnerable = 0; p.Shield = 100;
            var brute = Enemy(w, MinionKind.Brute, new(-24, 0));
            Invoke(w, "ResolveDefenseAreaAttack", brute.Runtime);
            Check(p.Health == p.MaxHealth && p.Shield == 30, "El golpe de área ignoró el escudo.");
            var shaman = Enemy(w, MinionKind.Shaman, new(-24, 2));
            var wounded = Enemy(w, MinionKind.Melee, new(-24, 3)); wounded.Body.Health = 20;
            var distant = Enemy(w, MinionKind.Melee, new(76, 52)); distant.Body.Health = 1;
            p.Health = 100; Set(shaman.Runtime, "SupportCooldown", 0f);
            Invoke(w, "TickDefenseShaman", shaman.Runtime, 0.1f);
            Check(wounded.Body.Health == 110 && distant.Body.Health == 1 && p.Health == 100, "Curó fuera del alcance o al jugador.");
            wounded.Body.Health = wounded.Body.MaxHealth - 1; Set(shaman.Runtime, "SupportCooldown", 0f);
            Invoke(w, "TickDefenseShaman", shaman.Runtime, 0.1f);
            Check(wounded.Body.Health == wounded.Body.MaxHealth, "La curación supera la vida máxima.");
        });
    }
}
