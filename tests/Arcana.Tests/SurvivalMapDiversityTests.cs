using Arcana.Core;

internal static class SurvivalMapDiversityTests
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Geometry(ArenaDefinition map) => Protocol.Serialize(new { map.Floors, map.Boundary, map.Teleports });

    internal static void Run(Action<string, Action> test)
    {
        test("Mapa libre: varían el número de salas, las formas, los ciclos y los tipos de pasillo", () =>
        {
            var rooms = new HashSet<int>(); var loops = new HashSet<int>();
            int mostRoomShapes = 0, mostCorridorShapes = 0;
            for (int i = 0; i < 64; i++)
            {
                var map = ArenaMap.Get(ArenaMapId.Bastion, new(i));
                var structure = map.SurvivalStructure ?? throw new Exception("Falta el diagnóstico del generador libre.");
                rooms.Add(structure.Rooms); loops.Add(structure.Loops);
                mostRoomShapes = Math.Max(mostRoomShapes, structure.RoomShapeKinds);
                mostCorridorShapes = Math.Max(mostCorridorShapes, structure.CorridorShapeKinds);
                Check(structure.Connections == structure.Rooms - 1 + structure.Loops,
                    "El árbol no conecta todas las salas o se perdieron ciclos.");
                Check(map.Teleports.SelectMany(p => new[] { p.A, p.B }).Distinct().Count() == 4,
                    "Dos portales comparten destino.");
            }
            Check(rooms.Count >= 3 && rooms.Any(n => n != 9), "La cantidad de salas sigue prefijada.");
            Check(loops.Count >= 2 && mostRoomShapes >= 3 && mostCorridorShapes >= 4,
                "Las semillas repiten las formas o los recorridos.");
            Check(SurvivalMapSpec.GenerationBudgetMilliseconds == 10_000, "Cambió el presupuesto solicitado.");
        });
        test("Mapa libre: toda la superficie útil pertenece a una sola región, sin solapamientos del suelo", () =>
        {
            foreach (int seed in new[] { int.MinValue, -1, 0, 27, 271, int.MaxValue })
            {
                var map = ArenaMap.Get(ArenaMapId.Bastion, new(seed));
                var cells = new HashSet<(int X, int Y)>();
                foreach (var floor in map.Floors)
                {
                    int left = (int)(floor.Center.X - floor.Width / 2), bottom = (int)(floor.Center.Y - floor.Height / 2);
                    for (int x = left; x < left + floor.Width; x += 2)
                    for (int y = bottom; y < bottom + floor.Height; y += 2)
                        Check(cells.Add((x, y)), "El suelo empaquetado se solapa.");
                }
                Check(cells.Count * 4 == map.Area, "Se cuenta dos veces el área transitable.");
                var visited = new HashSet<(int X, int Y)>(); var queue = new Queue<(int X, int Y)>();
                var origin = cells.First(); visited.Add(origin); queue.Enqueue(origin);
                while (queue.TryDequeue(out var at))
                    foreach (var next in new[] { (at.X - 2, at.Y), (at.X + 2, at.Y), (at.X, at.Y - 2), (at.X, at.Y + 2) })
                        if (cells.Contains(next) && visited.Add(next)) queue.Enqueue(next);
                Check(visited.Count == cells.Count, "El contorno tiene una isla aislada.");
            }
        });
        test("Mapa libre: la versión 1 sigue reconstruyendo el mapa de nueve salas y conserva el guardado", () =>
        {
            var spec = new SurvivalMapSpec(417, 1);
            var world = new GameWorld(new() { Mode = GameMode.Defense, Map = ArenaMapId.Bastion,
                SurvivalMap = spec, MaxPlayers = 1 });
            var human = world.AddPlayer("Guardado anterior")!;
            var map = world.Map;
            Check(map.SurvivalStructure == null && map.Floors.Count is >= 29 and <= 33 &&
                map.Floors.Take(9).All(f => f.Width >= 22 && f.Height >= 18), "Se sustituyó el generador de la preliminar.");
            var restored = GameWorld.RestoreSurvival(world.SaveSurvival(human.Id, Guid.NewGuid().ToString("N")));
            Check(restored.Map.SurvivalMap == spec && Geometry(restored.Map) == Geometry(map),
                "Continuar una partida v1 cambia su terreno.");
            Check(Geometry(ArenaMap.Get(ArenaMapId.Bastion, spec with { Version = SurvivalMapSpec.CurrentVersion })) != Geometry(map),
                "Las dos versiones usan el mismo generador.");
        });
        test("Mapa libre: las semillas extremas se reconstruyen exactamente después de desalojar la caché", () =>
        {
            foreach (int seed in new[] { int.MinValue, int.MaxValue, -1 })
            {
                var spec = new SurvivalMapSpec(seed); var first = ArenaMap.Get(ArenaMapId.Bastion, spec);
                string geometry = Geometry(first); var structure = first.SurvivalStructure;
                for (int i = 800_000; i < 800_020; i++) ArenaMap.Get(ArenaMapId.Bastion, new(i));
                var rebuilt = ArenaMap.Get(ArenaMapId.Bastion, spec);
                Check(!ReferenceEquals(first, rebuilt) && Geometry(rebuilt) == geometry && rebuilt.SurvivalStructure == structure,
                    "El presupuesto de tiempo o la caché cambian el mapa.");
            }
        });
    }
}
