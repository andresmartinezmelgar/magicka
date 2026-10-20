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
    private static void Magick(GameWorld w, Wizard player, int slot, V2 target)
    {
        w.ApplyMessage(player.Id, new() { Type = "input", AimX = target.X, AimY = target.Y, Sequence = player.LastSequence + 1 });
        w.ApplyMessage(player.Id, new() { Type = "magick", Slot = slot, Sequence = player.LastSequence + 1 });
    }
    private static void WallAt(GameWorld world, V2 a, V2 b)
    {
        var type = typeof(GameWorld).Assembly.GetType("Arcana.Core.Wall")!;
        var wall = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetField("Id")!.SetValue(wall, 9999);
        type.GetField("A")!.SetValue(wall, a); type.GetField("B")!.SetValue(wall, b);
        type.GetField("Recipe")!.SetValue(wall, new SpellRecipe([Element.Shield, Element.Earth]));
        ((IList)Field(world, "_walls")).Add(wall);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Supervivencia: IA mojada no descarga rayos sobre sí misma", () =>
        {
            var choose = typeof(GameWorld).GetMethod("SafeDefenseBotAttack",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (float range in new[] { 4f, 7.5f, 13f })
                for (int turn = 0; turn < 14; turn++)
                {
                    var elements = (Element[])choose.Invoke(null, [range, turn, true])!;
                    Check(!elements.Contains(Element.Lightning) && new SpellRecipe(elements).Elements.Length == 3,
                        "La IA mojada conserva una descarga eléctrica peligrosa o receta incompleta.");
                }
        });
        test("Supervivencia: IA alterna conos, descargas, rocas, hielo y rayos a distancias válidas", () =>
        {
            var choose = typeof(GameWorld).GetMethod("DefenseBotAttack",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (float distance in new[] { 4.0f, 7.5f, 13f })
            {
                var shapes = new HashSet<SpellShape>();
                var recipes = new HashSet<string>();
                for (int turn = 0; turn < 7; turn++)
                {
                    var selected = (Element[])choose.Invoke(null, [distance, turn])!;
                    var recipe = new SpellRecipe(selected);
                    Check(recipe.Elements.SequenceEqual(selected) && selected.Length == 3,
                        "La IA intentó una combinación incompatible o incompleta.");
                    shapes.Add(recipe.Shape);
                    recipes.Add(string.Join(",", selected));
                    Check(distance <= recipe.Range + 0.001f || recipe.Shape == SpellShape.Rock,
                        "La IA eligió un chorro que no alcanza al objetivo.");
                }
                Check(recipes.Count >= 3 && shapes.Count >= 2,
                    "Los bots vuelven a depender de un único tipo de hechizo.");
                if (distance < 6)
                {
                    foreach (Element cone in new[] { Element.Fire, Element.Water, Element.Cold })
                        Check(Enumerable.Range(0, 7).Any(turn =>
                        {
                            var selected = (Element[])choose.Invoke(null, [distance, turn])!;
                            return selected.All(e => e == cone) && new SpellRecipe(selected).Shape == SpellShape.Spray;
                        }), "Falta uno de los conos elementales de corta distancia.");
                }
                if (distance > 9)
                    Check(!shapes.Contains(SpellShape.Spray), "La IA usa un cono demasiado lejos.");
            }
        });
        test("Supervivencia: Traslación respeta muros, detiene empujes y no cobra un destino bloqueado", () =>
        {
            var w = World(); var player = w.AddPlayer("Salto")!; player.Position = V2.Zero;
            WallAt(w, new(3, -4), new(3, 4)); player.Knockback = new(20, 5);
            player.ActionCooldown = 0.3f; player.Channel = new SpellRecipe([Element.Arcane]);
            Magick(w, player, 1, new(7, 0));
            Check(player.Position.X > 2 && player.Position.X < 3 - ArenaMap.WizardRadius && w.Walkable(player.Position), "El salto atravesó el muro.");
            Check(player.Knockback == V2.Zero && player.Channel == null && player.Energy == 65 && player.MagickCooldowns[1] == 12,
                "El ataque anterior bloqueó el salto, no se interrumpió o no cobró exactamente un lanzamiento.");
            var arrival = player.Position;
            player.ActionCooldown = 0; player.MagickCooldowns[1] = 0;
            Magick(w, player, 1, new(7, 0));
            Check(player.Position == arrival && player.Energy == 65 && player.MagickCooldowns[1] == 0, "Un salto bloqueado gastó energía o recarga.");
        });
        test("Supervivencia: Meteoro espera su aviso, daña minions y protege al lanzador y aliados", () =>
        {
            var w = World(); var player = w.AddPlayer("Meteoro")!; var ally = w.AddPlayer("Aliado")!;
            Set(w, "_spawnClock", 10000f); player.Position = V2.Zero; ally.Position = new(-1, 0);
            player.Invulnerable = ally.Invulnerable = 0;
            var enemy = Spawn(w); enemy.Position = new(1, 0); enemy.Health = enemy.MaxHealth = 10000; enemy.Frozen = 10;
            float own = player.Health, allied = ally.Health;
            Magick(w, player, 2, enemy.Position);
            Check(player.Energy == 25 && w.Snapshot().Fields.Single().Position == enemy.Position, "No respetó el destino ni el coste.");
            Advance(w, 80); Check(enemy.Health == 10000, "Impactó antes del aviso.");
            for (int i = 0; i < 10 && w.Snapshot().Fields.Length > 0; i++) w.Step();
            Check(enemy.Health < 10000 && player.Health == own && ally.Health == allied, "El meteoro no dañó al minion o hirió a un defensor.");
            Check(w.Snapshot().Fields.Length == 0 && w.Snapshot().Events.Any(e => e.Kind == "explosion" && e.Text == "Meteoro"), "No terminó el impacto.");
        });
        test("Supervivencia: Meteoro rechaza patios sin suelo sin gastar energía ni recarga", () =>
        {
            var w = World(); var player = w.AddPlayer("Suelo")!; player.Position = new(18, 0);
            var target = new V2(28, 10);
            Check(!w.Map.Walkable(MagickRules.MeteorTarget(player.Position, target)), "El caso requiere un patio sin suelo.");
            Magick(w, player, 2, target);
            Check(player.Energy == 100 && player.MagickCooldowns[2] == 0 && w.Snapshot().Fields.Length == 0, "Aceptó un destino sin suelo.");
        });
        test("Supervivencia: Reanimar libera al aliado de un muro y conserva el límite de dos vidas", () =>
        {
            var w = World(); var caster = w.AddPlayer("Reanimador")!; var ally = w.AddPlayer("Aliado")!;
            caster.Position = V2.Zero; ally.Position = new(2, 0); Set(w, "_spawnClock", 10000f);
            ally.BuffStacks[(int)BuffKind.Power] = 3; Kill(w, ally);
            WallAt(w, new(2, -2), new(2, 2));
            Check(!w.Walkable(ally.Position) && MagickRules.ReanimationTarget(w.Snapshot(), w.Snapshot().Players.Single(p => p.Id == caster.Id))?.Id == ally.Id,
                "El cliente no identifica al aliado caído.");
            Magick(w, caster, 3, ally.Position);
            Check(ally.Alive && ally.Health == 600 && w.Walkable(ally.Position) && ally.Deaths == 1 && ally.BuffStacks[(int)BuffKind.Power] == 3,
                "El aliado reanimado quedó atrapado, recibió otra vida o perdió sus mejoras.");
            Kill(w, ally); caster.Energy = 100; caster.ActionCooldown = caster.MagickCooldowns[3] = 0;
            Magick(w, caster, 3, ally.Position);
            Check(!ally.Alive && caster.Energy == 100 && caster.MagickCooldowns[3] == 0 &&
                MagickRules.ReanimationTarget(w.Snapshot(), w.Snapshot().Players.Single(p => p.Id == caster.Id)) == null,
                "Un aliado eliminado permite gastar energía o recuperar una tercera vida.");
            SnapshotValidation.Check(w.Snapshot());
        });
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
            w.Step(); var boss = State(w).Minions.Single(m => m.Kind == MinionKind.Boss);
            Check(boss.MaxHealth >= DefenseRules.MinionHealth(MinionKind.Melee, 1, 1) * 10 &&
                State(w).Bestiary.Any(e => e.Kind == MinionKind.Boss) &&
                State(w).Minions.Length > 1,
                "El coloso no tiene más vida o no aparece con sus refuerzos.");
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
