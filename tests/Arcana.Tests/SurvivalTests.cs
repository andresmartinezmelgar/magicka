using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static class SurvivalTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static GameWorld World(int bots = 0, int seed = 37) => new(new WorldOptions
        { Mode = GameMode.Defense, Map = ArenaMapId.Bastion, MaxPlayers = 4, Bots = bots, Seed = seed });
    private static object Field(GameWorld w, string name) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
    private static void Set(GameWorld w, string name, object value) => typeof(GameWorld).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(w, value);
    private static object? Call(GameWorld w, string name, params object[] values) => typeof(GameWorld).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, values);
    private static DefenseSnapshot State(GameWorld w) => w.Snapshot().Defense!;
    private static void Advance(GameWorld w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
    private static void Kill(GameWorld w, Wizard target, int owner = 0)
    { target.Invulnerable = 0; Call(w, "Damage", target, 10000000f, owner, null!); }
    private static Wizard Spawn(GameWorld w, MinionKind kind = MinionKind.Melee)
    {
        Check((bool)Call(w, "TrySpawnMinion", kind)!, "No se pudo preparar el enemigo.");
        return ((Dictionary<int, Wizard>)Field(w, "_combatants"))[State(w).Minions.Last().Id];
    }
    public static void Run(Action<string, Action> test)
    {
        test("Anillos: mapa grande conectado, patios cerrados y portales accesibles", () =>
        {
            var map = ArenaMap.Get(ArenaMapId.Bastion);
            Check(map.HalfWidth == 96 && map.HalfHeight == 72 && map.Teleports.Count == 2, "Tamaño o portales incorrectos.");
            Check(!map.Walkable(new(28, 20)) && map.Walkable(V2.Zero) && !map.ClearPath(V2.Zero, new(76, 52)), "Los patios no impiden el paso.");
            foreach (var destination in map.Teleports.SelectMany(t => new[] { t.A, t.B }).Append(new V2(48, 32)))
            {
                Check(map.Walkable(destination), "Destino fuera del suelo.");
                var path = map.FindPath(V2.Zero, destination);
                Check(path.Length > 0 && V2.Distance(path[^1], destination) < 3, "Un pasillo queda desconectado.");
                var previous = V2.Zero;
                foreach (var point in path) { Check(map.ClearPath(previous, point), "Una ruta atraviesa un patio o pared."); previous = point; }
            }
            for (int i = 0; i < 4; i++) Check(map.Walkable(map.Spawn(0, i)), "Inicio no transitable.");
        });
        test("Supervivencia: radio de aparición móvil y alrededor de todos los humanos", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            a.Position = new(-76, -52); b.Position = new(76, 52);
            var regions = new HashSet<(bool, bool)>();
            for (int n = 0; n < 80; n++)
            {
                var enemy = Spawn(w); regions.Add((enemy.Position.X > 0, enemy.Position.Y > 0));
                Check(w.Map.Walkable(enemy.Position) && w.Players.Values.All(p => V2.Distance(p.Position, enemy.Position) >= DefenseRules.SafeRadius), "Apareció junto a un defensor.");
                Kill(w, enemy, a.Id); Call(w, "CleanupDefense");
                if (n == 39) { a.Position = new(0, 52); b.Position = new(0, -52); }
            }
            Check(regions.Count == 4, "No se usan todas las regiones del mapa.");
        });
        test("Supervivencia: primera muerte reaparece y segunda elimina sin revivir por magia", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; Set(w, "_spawnClock", 10000f);
            Check(State(w).Defenders.All(p => p.LivesRemaining == 2), "No empieza con dos vidas.");
            Kill(w, a); Check(!w.MatchEnded && a.Deaths == 1 && a.Respawn == 4.5f, "Primera muerte incorrecta.");
            Advance(w, 269); Check(!a.Alive, "Reapareció antes del plazo.");
            Advance(w, 2); Check(a.Alive, "No reapareció con su segunda vida.");
            Kill(w, a); Advance(w, 600);
            Check(!a.Alive && a.Deaths == 2 && !w.MatchEnded, "La segunda muerte añadió otra vida o terminó antes que el aliado.");
            Call(w, "Respawn", a); Call(w, "Heal", a, 100000f);
            b.Energy = 100; w.ApplyMessage(b.Id, new() { Type = "magick", Slot = 3, Sequence = b.LastSequence + 1 });
            Check(!a.Alive, "Una curación o reanimación ignoró el límite de vidas.");
            Kill(w, b); Advance(w, 271); Kill(w, b);
            Check(w.MatchEnded && State(w).Defenders.All(p => p.LivesRemaining == 0), "No terminó al agotarse los humanos.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: los bots no prolongan una partida con todos los humanos eliminados", () =>
        {
            var w = World(3); var human = w.AddPlayer("Humano")!; Set(w, "_spawnClock", 10000f);
            Kill(w, human); Advance(w, 271); Kill(w, human);
            Check(w.MatchEnded && w.Players.Values.Any(p => p.Bot && p.Alive), "Los bots impiden terminar.");
        });
        test("Supervivencia: desconexión del último humano con vidas concluye para los eliminados", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!; Set(w, "_spawnClock", 10000f);
            Kill(w, a); Advance(w, 271); Kill(w, a); w.RemovePlayer(b.Id);
            Check(w.MatchEnded, "Quedó una sala eterna sin humanos con vidas.");
        });
        test("Supervivencia: reconectar no reinicia las vidas agotadas", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; w.AddPlayer("B"); w.AttachDefenseAccount(a.Id, "uid"); Set(w, "_spawnClock", 10000f);
            Kill(w, a); Advance(w, 271); Kill(w, a); w.RemovePlayer(a.Id);
            var reconnect = w.AddPlayer("A")!; w.AttachDefenseAccount(reconnect.Id, "uid"); Advance(w, 600);
            Check(!reconnect.Alive && reconnect.Deaths == 2 && State(w).Defenders.Single(p => p.PlayerId == reconnect.Id).LivesRemaining == 0,
                "Reconectar concedió una tercera vida.");
        });
        test("Supervivencia: colosos variables, únicos, escalados y registrados en la guía", () =>
        {
            var intervals = Enumerable.Range(1, 10).Select(seed => State(World(seed: seed)).NextBossIn).ToArray();
            Check(intervals.All(t => t is >= 120 and <= 180) && intervals.Distinct().Count() > 5, "El primer intervalo no es variable.");
            var w = World(); var p = w.AddPlayer("Humano")!; p.Invulnerable = 10000; Set(w, "_spawnClock", 10000f); Set(w, "_bossClock", 0f);
            w.Step(); var boss = State(w).Minions.Single();
            Check(boss.Kind == MinionKind.Boss && boss.MaxHealth >= DefenseRules.MinionHealth(MinionKind.Melee, 1, 1) * 10 &&
                State(w).Bestiary.Single().Kind == MinionKind.Boss, "El coloso no tiene más vida o no se descubre.");
            Set(w, "_bossClock", 0f); Advance(w, 120);
            Check(State(w).Minions.Count(m => m.Kind == MinionKind.Boss) == 1, "Se solapan varios colosos.");
            var body = ((Dictionary<int, Wizard>)Field(w, "_combatants"))[boss.Id]; Kill(w, body, p.Id); w.Step();
            Check(State(w).BossesKilled == 1 && State(w).NextBossIn is >= 90 and <= 150 && State(w).Defenders.Single().Experience == 60,
                "El siguiente intervalo, EXP o contador son incorrectos.");
            p.Invulnerable = 1.2f; SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: aceleración temporal visible, limitada y reversible", () =>
        {
            var w = World(); var p = w.AddPlayer("Humano")!; p.Invulnerable = 10000; Set(w, "_spawnClock", 10000f);
            var enemy = Spawn(w); enemy.Position = new(12, 0);
            var minion = ((IDictionary)Field(w, "_minions"))[enemy.Id]!;
            var clock = minion.GetType().GetField("SpeedClock")!;
            for (int i = 0; i < 80 && State(w).Minions.Single().SpeedBoostSeconds <= 0; i++) { clock.SetValue(minion, 0f); w.Step(); }
            var boosted = State(w).Minions.Single();
            Check(boosted.SpeedBoostSeconds is >= 2 and <= 4 && Math.Abs(enemy.Equipment.Speed - DefenseRules.MinionSpeed(MinionKind.Melee, 1) * 1.6f) < 0.001f,
                "El impulso no se aplica o no se replica.");
            clock.SetValue(minion, 10000f); Advance(w, 241);
            Check(State(w).Minions.Single().SpeedBoostSeconds == 0 && Math.Abs(enemy.Equipment.Speed - DefenseRules.MinionSpeed(MinionKind.Melee, 1)) < 0.001f, "La velocidad extra se hizo permanente.");
        });
        test("Supervivencia: persecución desde un sector lejano por pasillos", () =>
        {
            var w = World(); var p = w.AddPlayer("Humano")!; p.Position = new(48, 32); p.Invulnerable = 10000; Set(w, "_spawnClock", 10000f);
            var enemy = Spawn(w); enemy.Position = new(-48, -32);
            bool approached = false;
            for (int i = 0; i < 9000; i++)
            {
                w.Step(); Check(w.Map.Walkable(enemy.Position), "El perseguidor salió del pasillo.");
                if (V2.Distance(enemy.Position, p.Position) < 3) { approached = true; break; }
            }
            Check(approached, "El enemigo se quedó atascado en otro sector.");
        });
        test("Supervivencia: portales emparejados sin rebote, también para minions", () =>
        {
            var w = World(); var p = w.AddPlayer("Humano")!; Set(w, "_spawnClock", 10000f);
            var pair = w.Map.Teleports[0]; p.Position = pair.A; w.Step();
            Check(V2.Distance(p.Position, pair.B) < 0.01, "El jugador no cruzó.");
            Advance(w, 180); Check(V2.Distance(p.Position, pair.B) < 0.01, "El portal devolvió al jugador sin salir del círculo.");
            p.Position = V2.Zero; var enemy = Spawn(w); enemy.Position = pair.A; w.Step();
            Check(V2.Distance(enemy.Position, pair.B) < 0.2, "El minion no puede usar portales.");
        });
    }
}
