using System.Diagnostics;
using System.Numerics;

namespace Arcana.Core;

internal static partial class SurvivalLayout
{
    private static ArenaDefinition CreateRandomized(SurvivalMapSpec spec)
    {
        var clock = Stopwatch.StartNew();
        void Budget()
        {
            if (clock.ElapsedMilliseconds >= SurvivalMapSpec.GenerationBudgetMilliseconds)
                throw new InvalidDataException("La generación del mapa de supervivencia agotó sus 10 segundos.");
        }
        // El reloj solo aborta: nunca elige otra geometría en un cliente más lento.
        // Los reintentos y su orden dependen exclusivamente de esta semilla.
        var random = new SurvivalRandom(spec.Seed);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Budget(); var terrain = new RandomizedTerrain(random, Budget);
            if (!terrain.PlaceRooms()) continue;
            terrain.ConnectRooms(); Budget();
            var map = Build(terrain.PackFloors(), 160, 120, terrain.Portals(), spec,
                "Laberinto · supervivencia", terrain.Structure, Budget);
            Budget(); map.PrepareNavigation(); Budget();
            bool navigable = Navigable(map, Budget); Budget();
            if (navigable) return map;
        }
        throw new InvalidDataException("No se pudo generar un mapa de supervivencia transitable.");
    }

    private static bool Navigable(ArenaDefinition map, Action budget)
    {
        for (int i = 0; i < 4; i++) if (!map.Walkable(map.Spawn(0, i))) return false;
        var (positions, neighbors) = map.Navigation;
        if (positions.Length == 0) return false;
        var visited = new bool[positions.Length]; var queue = new Queue<int>();
        visited[0] = true; queue.Enqueue(0); int reached = 0;
        while (queue.TryDequeue(out int at))
        {
            if ((++reached & 255) == 0) budget();
            foreach (int next in neighbors[at])
                if (!visited[next]) { visited[next] = true; queue.Enqueue(next); }
        }
        if (reached != positions.Length) return false;
        foreach (var point in map.Teleports.SelectMany(p => new[] { p.A, p.B }).Append(V2.Zero))
        {
            budget();
            if (!map.Inside(point, 2) || !positions.Any(p => (p - point).LengthSquared <= 8 && map.ClearPath(point, p)))
                return false;
        }
        return true;
    }

    private sealed partial class RandomizedTerrain(SurvivalRandom random, Action budget)
    {
        private const int Columns = 80, Rows = 60, CellSize = 2, Margin = 2;
        private readonly bool[,] _floor = new bool[Columns, Rows];
        private readonly List<Room> _rooms = [];
        private int _area, _roomShapes, _corridorShapes, _connections, _loops;
        private sealed record Room(int X, int Y, int RadiusX, int RadiusY, int Shape, int Orientation);
        internal SurvivalMapStructure Structure => new(_rooms.Count, _connections, _loops,
            BitOperations.PopCount((uint)_roomShapes), BitOperations.PopCount((uint)_corridorShapes));
        private static V2 Position(int x, int y) => new(x * CellSize - 80, y * CellSize - 60);

        internal bool PlaceRooms()
        {
            // La cantidad resulta de llenar una densidad aleatoria con tamaños aleatorios.
            // No hay cuadrícula de salas, cupos por fila ni un total de salas a construir.
            AddRoom(new(Columns / 2, Rows / 2, random.Next(6, 11), random.Next(6, 10), random.Next(6), random.Next(4)));
            int target = (Columns - Margin * 2) * (Rows - Margin * 2) * random.Next(18, 36) / 100;
            for (int attempt = 0; attempt < 640 && (_area < target || _rooms.Count < 5); attempt++)
            {
                if ((attempt & 15) == 0) budget();
                int rx = random.Next(4, 11), ry = random.Next(4, 10);
                var room = new Room(random.Next(rx + Margin, Columns - rx - Margin + 1),
                    random.Next(ry + Margin, Rows - ry - Margin + 1), rx, ry, random.Next(6), random.Next(4));
                if (_rooms.Any(r => (r.X - room.X) * (r.X - room.X) + (r.Y - room.Y) * (r.Y - room.Y) < 100)) continue;
                var cells = RoomCells(room).ToArray();
                if (cells.Count(p => _floor[p.X, p.Y]) * 5 > cells.Length) continue;
                AddRoom(room);
            }
            // Cuatro destinos distintos para los dos pares de portales, fuera del inicio.
            return _rooms.Count >= 5;
        }

        private static IEnumerable<(int X, int Y)> RoomCells(Room room)
        {
            int rx = room.RadiusX, ry = room.RadiusY;
            bool Ellipse(int dx, int dy, int ax, int ay) =>
                dx * dx * ay * ay + dy * dy * ax * ax <= 4 * ax * ax * ay * ay;
            for (int x = room.X - rx; x < room.X + rx; x++)
            for (int y = room.Y - ry; y < room.Y + ry; y++)
            {
                int dx = (2 * (x - room.X) + 1) * ((room.Orientation & 1) == 0 ? 1 : -1);
                int dy = (2 * (y - room.Y) + 1) * ((room.Orientation & 2) == 0 ? 1 : -1);
                bool shape = room.Shape switch
                {
                    0 => true,
                    1 => Ellipse(dx, dy, rx, ry),
                    2 => Math.Abs(dx) <= rx || Math.Abs(dy) <= ry,
                    3 => Math.Abs(dx) * ry + Math.Abs(dy) * rx <= 2 * rx * ry,
                    4 => Ellipse(dx + rx / 2, dy + ry / 2, rx * 3 / 4, ry * 3 / 4) ||
                         Ellipse(dx - rx / 2, dy - ry / 2, rx * 3 / 4, ry * 3 / 4),
                    _ => dx <= rx / 2 || dy <= ry / 2
                };
                // Núcleo seguro de 12 m, integrado en cualquier silueta de sala.
                if (shape || Math.Abs(dx) < 6 && Math.Abs(dy) < 6) yield return (x, y);
            }
        }

        private void AddRoom(Room room)
        {
            _rooms.Add(room); _roomShapes |= 1 << room.Shape;
            foreach (var cell in RoomCells(room)) Fill(cell.X, cell.Y);
        }

        private void Fill(int x, int y)
        {
            if (x < Margin || y < Margin || x >= Columns - Margin || y >= Rows - Margin || _floor[x, y]) return;
            _floor[x, y] = true; _area++;
        }

        internal List<FloorSection> PackFloors()
        {
            // Rectángulos disjuntos: ni miles de baldosas dibujables ni superficies contadas dos veces.
            var result = new List<FloorSection>();
            var active = new Dictionary<(int Start, int End), int>();
            for (int y = 0; y < Rows; y++)
            {
                budget(); var next = new Dictionary<(int Start, int End), int>();
                for (int x = 0; x < Columns;)
                {
                    if (!_floor[x, y]) { x++; continue; }
                    int start = x++; while (x < Columns && _floor[x, y]) x++;
                    var run = (start, x);
                    if (active.TryGetValue(run, out int index))
                    {
                        var old = result[index];
                        result[index] = old with { Center = old.Center + new V2(0, CellSize / 2f), Height = old.Height + CellSize };
                    }
                    else
                    {
                        index = result.Count;
                        result.Add(new(new((start + x) * CellSize / 2f - 80, y * CellSize + CellSize / 2f - 60),
                            (x - start) * CellSize, CellSize));
                    }
                    next.Add(run, index);
                }
                active = next;
            }
            return result;
        }

        internal TeleportPair[] Portals()
        {
            var available = Enumerable.Range(1, _rooms.Count - 1).ToList();
            var result = new List<TeleportPair>();
            while (result.Count < 2)
            {
                (int A, int B, int Score) best = (-1, -1, -1);
                for (int a = 0; a < available.Count; a++) for (int b = a + 1; b < available.Count; b++)
                {
                    var from = _rooms[available[a]]; var to = _rooms[available[b]];
                    int dx = from.X - to.X, dy = from.Y - to.Y;
                    int score = (dx * dx + dy * dy) * random.Next(80, 121);
                    if (score > best.Score) best = (available[a], available[b], score);
                }
                var left = _rooms[best.A]; var right = _rooms[best.B];
                result.Add(new(Position(left.X, left.Y), Position(right.X, right.Y)));
                available.Remove(best.A); available.Remove(best.B);
            }
            return result.ToArray();
        }
    }
}
