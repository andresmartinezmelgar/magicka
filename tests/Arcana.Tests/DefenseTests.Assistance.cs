using Arcana.Core;
using System.Collections;
using System.Reflection;

internal static partial class DefenseTests
{
    private static void RunAssistance(Action<string, Action> test)
    {
        test("Defensa: la tienda congela todo con un humano y bots, permite compras y reanuda", () =>
        {
            var w = World(bots: 2); var p = w.AddPlayer("Humano")!;
            Gold(w, p, 2000); var enemy = Enemy(w); enemy.Position = new(-27, 0);
            Check(Command(w, p, "defense_shop", 1), "No abrió tienda.");
            var before = w.Snapshot(); Check(before.Defense!.Paused && before.Defense.HumanCount == 1, "Los bots impidieron pausar.");
            Advance(w, 900);
            Check(Protocol.Serialize(before) == Protocol.Serialize(w.Snapshot()), "La pausa avanzó vida, relojes, enemigos o experiencia.");
            Check(Command(w, p, "defense_passive", 0) && Player(w, p).Passives[0] == 1, "No pudo comprar con la simulación pausada.");
            Check(!Command(w, p, "defense_passive", 4), "Aceptó una habilidad inexistente.");
            var second = w.AddPlayer("Otro humano")!; Check(!w.DefensePaused, "La tienda pausó a dos humanos.");
            Advance(w, 5); Check(w.Tick > before.Tick, "No avanzó al entrar otro humano.");
            w.RemovePlayer(second.Id); Check(w.DefensePaused, "No volvió a pausar al quedar un humano.");
            Check(Command(w, p, "defense_shop", 0) && !w.DefensePaused, "No cerró la tienda.");
            long tick = w.Tick; Advance(w, 5); Check(w.Tick == tick + 5, "No reanudó.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: consultar pantallas con dos humanos activa defensa sin pausar ni dar inmunidad", () =>
        {
            var w = World(bots: 1); var p = w.AddPlayer("Consulta")!; var ally = w.AddPlayer("Compañero")!;
            p.Position = new(-8, 0); ally.Position = new(20, 0); p.Invulnerable = 0;
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(3, 0);
            p.Selection.Add(Element.Fire); Gold(w, p, 1000);
            var before = p.Position;
            Check(Command(w, p, "defense_shop", 1), "No abrió la interfaz."); Advance(w, 5);
            Check(!w.DefensePaused && w.Tick >= 5 && !p.Bot && w.Snapshot().Defense!.HumanCount == 2,
                "La ayuda cambió la identidad humana o pausó al grupo.");
            Check(p.Position != before && p.Shield > 0 && p.Invulnerable == 0, "La ayuda no esquiva/protege o concede inmunidad.");
            Check(p.Selection.Elements.SequenceEqual(new[] { Element.Fire }) && Player(w, p).Gold == 1000,
                "La ayuda cambia la selección o gasta oro.");
            Check(!Command(w, p, "cast"), "Se permiten ataques manuales con la interfaz abierta.");
            float shield = p.Shield;
            Invoke(w, "Hit", p, new SpellRecipe([Element.Arcane]), 1f, enemy.Id, new V2(1, 0), 1f);
            Check(p.Shield < shield || p.Health < p.MaxHealth, "Los enemigos ignoran al jugador asistido.");
            Check(Command(w, p, "defense_shop", 0), "No devolvió el control.");
            Check(p.Input.MoveX == 0 && p.Input.MoveY == 0, "La ayuda deja movimiento residual.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: la asistencia cura con un hechizo real y no acumula participación", () =>
        {
            var w = World(); var p = w.AddPlayer("Consulta")!; w.AddPlayer("Otro humano");
            p.Health = 300; p.Invulnerable = 0;
            Check(Command(w, p, "defense_shop", 1), "No abrió la interfaz.");
            w.Step();
            Check(p.Shield > 0 && p.Health == 300, "La asistencia cura antes de levantar el escudo.");
            Advance(w, 30);
            Check(p.Health > 300 && w.Snapshot().Events.Any(e => e.Kind == "self" && e.Elements.Contains(Element.Life)),
                "La curación no procede de un hechizo de vida.");
            Advance(w, 90);
            Check(w.DefenseActiveSeconds(p.Id) == 0, "La IA genera participación de jugador activo.");
        });
        test("Supervivencia: sin bots, la consulta mantiene escudo y retirada antes que ataques", () =>
        {
            var w = World(); var p = w.AddPlayer("Inventario")!; var ally = w.AddPlayer("Humano")!;
            p.Position = new(-8, 0); ally.Position = new(20, 0); p.Invulnerable = 0;
            p.Selection.Add(Element.Fire); Gold(w, p, 1000);
            Check(Command(w, p, "defense_shop", 1), "No abrió el inventario."); w.Step();
            Check(!w.Players.Values.Any(a => a.Bot) && !w.DefensePaused && p.Shield > 0,
                "El inventario sin bots pausa la partida o deja al humano sin escudo aunque no haya enemigos.");
            p.Shield = 1; p.ShieldTime = .1f; p.ActionCooldown = 0;
            p.Channel = new SpellRecipe([Element.Arcane]);
            Invoke(w, "ThinkDefenseAssistance", p, GameWorld.StepSeconds);
            Check(p.Channel == null && p.Shield > 1 && p.ShieldTime > 2,
                "Un ataque o su recarga impide reponer primero el escudo.");
            p.ActionCooldown = 0; p.Shield = 1100; p.ShieldMax = 2000; p.ShieldTime = 12;
            Invoke(w, "ThinkDefenseAssistance", p, GameWorld.StepSeconds);
            Check(p.Shield == 1100, "La asistencia sustituye un escudo que aún absorbe más por otro más débil.");
            var enemy = Enemy(w); enemy.Position = p.Position + new V2(3, 0);
            var before = p.Position; var away = p.Position - enemy.Position; w.Step();
            Check(V2.Dot(p.Position - before, away) > 0 && p.Channel == null,
                "La asistencia ataca o se acerca al monstruo en vez de ganar distancia.");
            Check(p.Selection.Elements.SequenceEqual(new[] { Element.Fire }) && Player(w, p).Gold == 1000 && !p.Bot && p.Invulnerable == 0,
                "La asistencia altera selección, oro, identidad o daño normal.");
            Check(Command(w, p, "defense_shop", 0) && p.Input.MoveX == 0 && p.Input.MoveY == 0 && p.Channel == null,
                "Cerrar el inventario deja movimiento o ataques automáticos.");
            SnapshotValidation.Check(w.Snapshot());
        });
        test("Supervivencia: dos humanos en menús no pausan; al quedar uno se pausa", () =>
        {
            var w = World(); var a = w.AddPlayer("A")!; var b = w.AddPlayer("B")!;
            Command(w, a, "defense_shop", 1); Command(w, b, "defense_shop", 1);
            Advance(w, 3); Check(!w.DefensePaused && w.Tick == 3, "Se pausó con dos humanos en menús.");
            w.RemovePlayer(b.Id); long tick = w.Tick; Advance(w, 3);
            Check(w.DefensePaused && w.Tick == tick, "No pausó al quedar un humano.");
            Command(w, a, "defense_shop", 0); w.Step();
            Check(!w.DefensePaused && w.Tick == tick + 1, "No reanudó al cerrar la interfaz.");
        });
    }
}
