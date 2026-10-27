using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcana.Core;

internal static class SurvivalContinuityTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static (GameWorld World, Wizard Human) World(SurvivalMapSpec? spec = null)
    {
        var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
            MaxPlayers = 4, Seed = 730, SurvivalMap = spec });
        return (world, world.AddPlayer("Defensor")!);
    }
    private static string Geometry(ArenaDefinition map) => Protocol.Serialize(new { map.Floors, map.Boundary, map.Teleports });
    private static object? Call(GameWorld world, string method, params object?[] args) =>
        typeof(GameWorld).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(world, args);
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { return true; }
    }

    internal static void Run(Action<string, Action> test)
    {
        test("Red: un reflejo conserva el alcance del lanzador y los rayos directos siguen acotados", () =>
        {
            var world = new GameWorld(new() { Mode = GameMode.Arena });
            world.AddPlayer("Lanzador", 0); var reflector = world.AddPlayer("Reflector", 1)!;
            var ray = new BeamSnapshot(reflector.Id, new(-11, 0), new(11, 0), [Element.Arcane],
                SpellShape.Beam, 1, "reflected");
            var state = world.Snapshot();
            SnapshotValidation.Check(state with { Beams = [ray] });
            Check(Reject(() => SnapshotValidation.Check(state with { Beams = [ray with { Interaction = "direct" }] })),
                "Un rayo directo sin equipo obtuvo alcance prestado.");
            Check(Reject(() => SnapshotValidation.Check(state with { Beams = [ray with { To = new(13, 0) }] })),
                "Un reflejo superó el máximo de alcance del equipo.");
        });
        test("Mapa procedural: 64 semillas distintas, salas accesibles, portales seguros y límites menores", () =>
        {
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 64; seed++)
            {
                var map = ArenaMap.Get(ArenaMapId.Bastion, new(seed));
                Check(map.HalfWidth == 80 && map.HalfHeight == 60 && seen.Add(Geometry(map)), "Se repite o no se reduce el terreno.");
                for (int i = 0; i < 4; i++) Check(map.Walkable(map.Spawn(0, i)), "Aparición fuera del suelo.");
                foreach (var floor in map.Floors)
                {
                    Check(Math.Abs(floor.Center.X) + floor.Width / 2 < map.HalfWidth &&
                        Math.Abs(floor.Center.Y) + floor.Height / 2 < map.HalfHeight, "Una sala rebasa los límites.");
                    Check(map.FindPath(V2.Zero, floor.Center).Length > 0, "Sala o pasillo aislado.");
                }
                Check(map.Teleports.Count == 2, "Faltan las rutas de escape.");
                foreach (var at in map.Teleports.SelectMany(p => new[] { p.A, p.B }))
                    Check(map.Inside(at, 2) && map.FindPath(V2.Zero, at).Length > 0, "Portal inseguro o inaccesible.");
            }
        });
        test("Mapa procedural: reconstruir la semilla tras vaciar la caché conserva la geometría", () =>
        {
            var spec = new SurvivalMapSpec(int.MinValue);
            var first = ArenaMap.Get(ArenaMapId.Bastion, spec); string expected = Geometry(first);
            for (int i = 100; i < 120; i++) ArenaMap.Get(ArenaMapId.Bastion, new(i));
            var rebuilt = ArenaMap.Get(ArenaMapId.Bastion, spec);
            Check(!ReferenceEquals(first, rebuilt) && Geometry(rebuilt) == expected, "El mapa depende de la caché o del reloj.");
        });
        test("Mapa procedural: horda, bots y navegación sobreviven a cambiar de terreno al reiniciar", () =>
        {
            foreach (int seed in new[] { 19, 303, 711 })
            {
                var (world, human) = World(new(seed)); world.AddPlayer("Aliado", bot: true);
                for (int run = 0; run < 2; run++)
                {
                    human.Position = world.Map.Teleports[0].A;
                    for (int tick = 0; tick < 600; tick++)
                    {
                        human.Invulnerable = 1.2f; world.Step();
                        if (tick % 120 == 0)
                        {
                            var state = world.Snapshot(); SnapshotValidation.Check(state);
                            Check(state.Defense!.Minions.All(m => world.Map.Inside(m.Position)), "La horda salió del terreno.");
                        }
                    }
                    Check(world.Snapshot().Defense!.Minions.Length > 0, "No aparecen enemigos en el mapa nuevo.");
                    human.Health = 0; human.Deaths = DefenseRules.PlayerLives; Call(world, "CheckSurvivalEnd");
                    Check(world.RestartDefense(), "No se pudo iniciar otra partida en el mismo mundo.");
                }
            }
        });
        test("Mapa procedural: nueva partida y reinicio cambian el mapa sin alterar las apariciones", () =>
        {
            var (world, human) = World(); var (other, _) = World();
            Check(world.Map.SurvivalMap is { Version: 1 } && other.Map.SurvivalMap != world.Map.SurvivalMap,
                "Dos partidas nuevas usan la misma semilla.");
            human.Position = world.Map.Teleports[0].A;
            world.ApplyMessage(human.Id, new() { Type = "input", AimX = human.Position.X + 8,
                AimY = human.Position.Y, Sequence = human.LastSequence + 1 });
            world.ApplyMessage(human.Id, new() { Type = "element", Element = Element.Arcane, Sequence = human.LastSequence + 1 });
            world.ApplyMessage(human.Id, new() { Type = "cast", Sequence = human.LastSequence + 1 });
            world.Step();
            Check(world.Snapshot().Beams.Length > 0, "No se preparó el rayo de la partida anterior.");
            var before = world.Map.SurvivalMap; human.Health = 0; human.Deaths = DefenseRules.PlayerLives;
            Call(world, "CheckSurvivalEnd");
            var previousEvents = world.Snapshot().Events.Select(e => e.Id).ToHashSet();
            Check(world.RestartDefense() && world.Map.SurvivalMap != before && world.Map.Walkable(human.Position),
                "Reiniciar conserva el mapa anterior o deja al jugador fuera.");
            var restarted = world.Snapshot();
            Check(restarted.Beams.Length == 0 && restarted.Events.All(e => !previousEvents.Contains(e.Id)),
                "Reiniciar reproduce ataques o eventos del mapa anterior.");
            SnapshotValidation.Check(restarted);
        });
        test("Mapa procedural: snapshot JSON y delta reconstruyen exactamente el mapa del servidor", () =>
        {
            var (a, _) = World(new(71)); var (b, _) = World(new(92)); b.Step();
            var baseline = new ServerMessage { Type = "snapshot", Snapshot = a.Snapshot() };
            var current = new ServerMessage { Type = "snapshot", Snapshot = b.Snapshot() };
            var wire = Protocol.Deserialize<ServerMessage>(Protocol.Serialize(current))!;
            var delta = SnapshotDelta.Decode(SnapshotDelta.Encode(current, baseline), baseline);
            foreach (var snapshot in new[] { wire.Snapshot!, delta.Snapshot! })
            {
                SnapshotValidation.Check(snapshot);
                Check(snapshot.SurvivalMap == b.Map.SurvivalMap && Geometry(ArenaMap.Get(snapshot)) == Geometry(b.Map),
                    "El cliente usa otro terreno tras un keyframe o delta.");
            }
            Check(Reject(() => SnapshotValidation.Check(b.Snapshot() with { SurvivalMap = new(7, 999) })),
                "Se aceptó un generador desconocido.");
            Check(Reject(() => SnapshotValidation.Check(new GameWorld().Snapshot() with { SurvivalMap = new(7) })),
                "Un modo competitivo aceptó terreno de supervivencia.");
        });
        test("Continuidad: guardar y recuperar mantiene semilla, terreno, posición y azar de horda", () =>
        {
            var (world, human) = World(new(417));
            human.Position = world.Map.Teleports[0].A; human.Health = 617;
            var save = world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N"));
            var restored = GameWorld.RestoreSurvival(save);
            var damaged = JsonNode.Parse(save.State.GetRawText())!;
            damaged["SurvivalMap"]!["Version"] = 999;
            Check(Reject(() => GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(damaged) })),
                "Se recuperó un mapa generado con una versión desconocida.");
            damaged["SurvivalMap"] = null;
            Check(Reject(() => GameWorld.RestoreSurvival(save with { State = JsonSerializer.SerializeToElement(damaged) })),
                "Un guardado nuevo perdió su terreno sin detectarse.");
            Check(restored.Map.SurvivalMap == world.Map.SurvivalMap && Geometry(restored.Map) == Geometry(world.Map) &&
                restored.Players[human.Id].Position == human.Position && restored.Players[human.Id].Health == 617,
                "Recuperar generó otro mapa o reinició al jugador.");
            for (int i = 0; i < 120; i++) { world.Step(); restored.Step(); }
            Check(Protocol.Serialize(world.Snapshot().Defense!.Minions) == Protocol.Serialize(restored.Snapshot().Defense!.Minions),
                "Reconstruir el mapa consumió azar de la horda.");
            SnapshotValidation.Check(restored.Snapshot());
        });
        test("Continuidad: los guardados v1 sin semilla conservan el mapa antiguo", () =>
        {
            var (world, human) = World(SurvivalMapSpec.Legacy);
            human.Position = new(-76, -52);
            var save = world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N"));
            var state = JsonNode.Parse(save.State.GetRawText())!;
            state.AsObject().Remove("SurvivalMap"); state["Options"]!.AsObject().Remove("SurvivalMap");
            var restored = GameWorld.RestoreSurvival(save with { Version = 1, State = JsonSerializer.SerializeToElement(state) });
            Check(restored.Map.SurvivalMap == SurvivalMapSpec.Legacy && restored.Map.HalfWidth == 96 &&
                restored.Map.HalfHeight == 72 && restored.Map.Walkable(human.Position) &&
                restored.Players[human.Id].Position == human.Position, "Un guardado antiguo se trasladó a otro terreno.");
        });
        test("Autoguardado: cargas repetidas conservan el punto y finalizar invalida también el respaldo", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "arcana-autosave-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "survival.json");
            try
            {
                var store = new SurvivalSaveStore(path); var (world, human) = World(new(143));
                string run = Guid.NewGuid().ToString("N");
                store.Write(world.SaveSurvival(human.Id, run)); world.Step();
                store.Write(world.SaveSurvival(human.Id, run));
                Check(store.Load().World.Tick == world.Tick && store.Load().World.Tick == world.Tick && store.Exists,
                    "Recuperar consume el punto antes de poder autoguardar otra vez.");
                store.Discard();
                Check(!store.Exists && Reject(() => store.Load()) && !File.Exists(path + ".bak"),
                    "La partida terminada se recupera desde la copia.");
                store.Write(world.SaveSurvival(human.Id, run));
                File.WriteAllText(path, "interrumpido");
                Check(store.Exists && store.Load().RecoveredBackup && store.Load().World.Tick == world.Tick,
                    "El nuevo autoguardado no recupera un cierre durante la escritura.");
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
        test("Supervivencia: equipar una sola pieza mítica publica su apariencia y desequipar la elimina", () =>
        {
            var (world, human) = World(new(59));
            var item = EquipmentCatalogue.Generate(new(39), slot: EquipmentSlot.Hat, tier: ItemTier.Mythic);
            Call(world, "GrantDefenseItem", human, item);
            bool Equip() => world.ApplyMessage(human.Id, new() { Type = "defense_equip", ItemId = item.Id, Sequence = human.LastSequence + 1 });
            Check(Equip() && world.Snapshot().Players.Single().Equipment?.Hat == ItemTier.Mythic,
                "El inventario no transmite la pieza mítica al personaje.");
            Check(world.ApplyMessage(human.Id, new() { Type = "defense_loadout", Loadout = new(), Sequence = human.LastSequence + 1 }) &&
                world.Snapshot().Players.Single().Equipment?.Hat == null,
                "Desequipar deja activa la apariencia mítica.");
        });
    }
}
