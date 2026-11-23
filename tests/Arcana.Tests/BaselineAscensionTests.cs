using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineAscensionTests
{
    public static void Run(Action<string, Action> Test)
    {
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
            BaselineTestSupport.Run(world, ally, 30); Send(world, ally, "release");
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
    }
}
