using Arcana.Core;
using System.Text.Json;
using static BaselineTestSupport;

internal static class BaselineRelicTests
{
    public static void Run(Action<string, Action> Test)
    {
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
            a.Health = 600; a.Energy = 30; BaselineTestSupport.Run(w, a, 60);
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
    }
}
