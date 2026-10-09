namespace Arcana.Core;

public enum ArenaMapId { Classic, Citadel, Ascension, Relics, Frontline, Practice }
public enum ArenaTheme { Forest, City, Ruins, Sanctuary }
public sealed record FloorSection(V2 Center, float Width, float Height);
public sealed record MapEdge(V2 A, V2 B);
public sealed record TeleportPair(V2 A, V2 B);

/// <summary>Geometría compartida: suelo, límites, pilares, puntos y rutas coinciden en servidor y cliente.</summary>
public sealed class ArenaDefinition
{
    public ArenaMapId Id { get; }
    public string Name { get; }
    public float HalfWidth { get; }
    public float HalfHeight { get; }
    public IReadOnlyList<FloorSection> Floors { get; }
    public IReadOnlyList<MapEdge> Boundary { get; }
    public IReadOnlyList<Obstacle> Obstacles { get; }
    public IReadOnlyList<V2> ControlPoints { get; }
    public IReadOnlyList<V2> ItemSpawns { get; }
    public IReadOnlyList<TeleportPair> Teleports { get; }
    public IReadOnlyList<MapEdge> EventGates { get; }
    public bool SequentialCapture => Id == ArenaMapId.Frontline;
    public bool IsPractice => Id == ArenaMapId.Practice;
    public bool SupportsMode(GameMode mode) => !SequentialCapture || mode == GameMode.Warfare;
    public bool HasPickups => Id is ArenaMapId.Ascension or ArenaMapId.Relics;
    public bool RandomPickups => Id == ArenaMapId.Ascension;
    public ArenaTheme Theme => Id switch { ArenaMapId.Classic => ArenaTheme.Forest, ArenaMapId.Citadel => ArenaTheme.City,
        ArenaMapId.Ascension or ArenaMapId.Frontline => ArenaTheme.Ruins, _ => ArenaTheme.Sanctuary };
    public float Area => Floors.Sum(f => f.Width * f.Height);
    public int HoldSeconds => Id == ArenaMapId.Citadel ? 15 : 0;
    public float KillHealthBonus => Id == ArenaMapId.Ascension ? 10 : 0;
    public float RespawnDeathPenalty => Id == ArenaMapId.Ascension ? 0.5f : 0;
    public float RespawnSeconds(GameMode mode, int deaths) => IsPractice ? 1 : Math.Min(10,
        (mode == GameMode.Duel ? 2.5f : 4) + RespawnDeathPenalty * Math.Max(0, deaths));
    private readonly float _spawnX;
    private readonly FloorSection[] _floors;
    private readonly MapEdge[] _boundary;
    private readonly Obstacle[] _obstacles;
    private readonly Lazy<(V2[] Positions, int[][] Neighbors)> _navigation;

    internal ArenaDefinition(ArenaMapId id, string name, float halfWidth, float halfHeight,
        FloorSection[] floors, V2[] outline, Obstacle[] obstacles, V2[] points, float spawnX, V2[]? itemSpawns = null,
        TeleportPair[]? teleports = null, MapEdge[]? eventGates = null)
    {
        Id = id; Name = name; HalfWidth = halfWidth; HalfHeight = halfHeight; _spawnX = spawnX;
        _floors = floors; _obstacles = obstacles;
        Floors = Array.AsReadOnly(_floors); Obstacles = Array.AsReadOnly(_obstacles); ControlPoints = Array.AsReadOnly(points);
        ItemSpawns = Array.AsReadOnly(itemSpawns ?? []);
        Teleports = Array.AsReadOnly(teleports ?? []); EventGates = Array.AsReadOnly(eventGates ?? []);
        _boundary = outline.Select((p, i) => new MapEdge(p, outline[(i + 1) % outline.Length])).ToArray();
        Boundary = Array.AsReadOnly(_boundary);
        _navigation = new(BuildNavigation);
    }

    public V2 Spawn(int team, int index, int capturedPoints = 0)
    {
        if (IsPractice) return PracticeLayout.Respawn;
        if (SequentialCapture && capturedPoints > 0)
        {
            int count = Math.Clamp(capturedPoints, 1, ControlPoints.Count);
            var point = ControlPoints[team == 0 ? count - 1 : ControlPoints.Count - count];
            return point + new V2(team == 0 ? -7 : 7, (index % 4 - 1.5f) * 1.8f);
        }
        return new(team == 0 ? -_spawnX : _spawnX, (index % 4 - 1.5f) * 2.2f);
    }
    public bool Inside(V2 p, float radius = 0)
    {
        if (!p.IsFinite) return false;
        bool floor = false;
        for (int i = 0; i < _floors.Length; i++)
        {
            var section = _floors[i];
            if (MathF.Abs(p.X - section.Center.X) <= section.Width / 2 && MathF.Abs(p.Y - section.Center.Y) <= section.Height / 2)
            { floor = true; break; }
        }
        if (!floor) return false;
        foreach (var edge in _boundary)
            if (!(Geometry.DistanceToSegment(p, edge.A, edge.B) > radius)) return false;
        return true;
    }
    public bool Walkable(V2 p)
    {
        if (!Inside(p, ArenaMap.WizardRadius)) return false;
        foreach (var obstacle in _obstacles)
            if (!(V2.Distance(p, obstacle.Position) >= obstacle.Radius + ArenaMap.WizardRadius)) return false;
        return true;
    }
    public float? BoundaryHit(V2 from, V2 to, float radius = 0)
    {
        float? best = null;
        foreach (var edge in _boundary)
            if (Geometry.SegmentCapsule(from, to, edge.A, edge.B, radius) is float t && (best == null || t < best)) best = t;
        return best;
    }
    public bool ClearPath(V2 from, V2 to)
    {
        if (!Walkable(from) || !Walkable(to) || BoundaryHit(from, to, ArenaMap.WizardRadius) != null) return false;
        foreach (var obstacle in _obstacles)
        {
            if (Geometry.SegmentCircle(from, to, obstacle.Position, obstacle.Radius + ArenaMap.WizardRadius) != null) return false;
        }
        return true;
    }

    private (V2[] Positions, int[][] Neighbors) BuildNavigation()
    {
        var cells = new Dictionary<(int X, int Y), int>(); var positions = new List<V2>();
        int nx = (int)MathF.Ceiling(HalfWidth), ny = (int)MathF.Ceiling(HalfHeight);
        for (int x = -nx + 1; x < nx; x += 2)
        for (int y = -ny + 1; y < ny; y += 2)
        {
            var p = new V2(x, y);
            if (!Walkable(p)) continue;
            cells[(x, y)] = positions.Count; positions.Add(p);
        }
        var neighbors = positions.Select(_ => new List<int>()).ToArray();
        foreach (var (cell, index) in cells)
            foreach (var (dx, dy) in new[] { (-2, 0), (2, 0), (0, -2), (0, 2) })
                if (cells.TryGetValue((cell.X + dx, cell.Y + dy), out int next) && ClearPath(positions[index], positions[next]))
                    neighbors[index].Add(next);
        return (positions.ToArray(), neighbors.Select(n => n.ToArray()).ToArray());
    }

    // La cuadrícula estática se prepara al cargar el mundo, antes de sus ticks.
    internal void PrepareNavigation() => _ = _navigation.Value;

    /// <summary>A*: rutas por pasillos y alrededor de pilares; no permite atajos por los patios sin suelo.</summary>
    public V2[] FindPath(V2 from, V2 goal, Func<V2, V2, bool>? clearSegment = null, bool allowPartial = false)
    {
        if (!Walkable(from) || !Walkable(goal)) return [];
        bool Clear(V2 a, V2 b) => ClearPath(a, b) && (clearSegment == null || clearSegment(a, b));
        if (Clear(from, goal)) return [goal];
        var (positions, neighbors) = _navigation.Value;
        int Nearest(V2 p)
        {
            int best = -1; float distance = float.PositiveInfinity;
            for (int i = 0; i < positions.Length; i++)
            {
                float candidate = (positions[i] - p).LengthSquared;
                if (candidate >= distance) continue;
                best = i; distance = candidate;
            }
            // Primero el nodo geométricamente más próximo. Recorrer la cuadrícula
            // de oeste a este hacía miles de pruebas de colisión por cada ruta.
            if (best < 0 || Clear(p, positions[best])) return best;
            var candidates = new PriorityQueue<int, (float Distance, int Index)>(positions.Length);
            for (int i = 0; i < positions.Length; i++)
                candidates.Enqueue(i, ((positions[i] - p).LengthSquared, i));
            while (candidates.TryDequeue(out int next, out _))
                if (next != best && Clear(p, positions[next])) return next;
            return -1;
        }
        int start = Nearest(from), end = Nearest(goal);
        if (start < 0 || end < 0 && !allowPartial) return [];
        var costs = Enumerable.Repeat(float.PositiveInfinity, positions.Length).ToArray();
        var previous = Enumerable.Repeat(-1, positions.Length).ToArray(); var visited = new bool[positions.Length];
        var open = new PriorityQueue<int, float>(); costs[start] = 0; open.Enqueue(start, 0);
        int closest = start; float closestDistance = V2.Distance(positions[start], goal);
        V2[] Reconstruct(int node, bool complete)
        {
            var path = new List<V2>();
            if (complete) path.Add(goal);
            for (; node >= 0; node = previous[node]) path.Add(positions[node]);
            path.Reverse(); return path.ToArray();
        }
        while (open.TryDequeue(out int at, out _))
        {
            if (visited[at]) continue;
            visited[at] = true;
            float remaining = V2.Distance(positions[at], goal);
            if (remaining < closestDistance) { closest = at; closestDistance = remaining; }
            if (at == end)
            {
                return Reconstruct(at, true);
            }
            foreach (int next in neighbors[at])
            {
                if (clearSegment != null && !clearSegment(positions[at], positions[next])) continue;
                float cost = costs[at] + V2.Distance(positions[at], positions[next]);
                if (cost >= costs[next]) continue;
                costs[next] = cost; previous[next] = at;
                open.Enqueue(next, cost + V2.Distance(positions[next], goal));
            }
        }
        // Una pared temporal puede cortar el pasillo entero. Acércate hasta el último
        // lugar alcanzable para poder atacarla, manteniendo intactas las colisiones.
        return allowPartial ? Reconstruct(closest, false) : [];
    }
}

internal static class ArenaMaps
{
    private static readonly ArenaDefinition Practice = CreatePractice();
    private static ArenaDefinition CreatePractice()
    {
        float height = Math.Max(48, 28 + (PracticeLayout.Barriers.Count - 1) / 7 * 12);
        return new(ArenaMapId.Practice, "Laboratorio de hechizos", 60, height,
            [new(V2.Zero, 120, height * 2)], [new(-60, -height), new(60, -height), new(60, height), new(-60, height)],
            [], [], 48);
    }
    private static readonly ArenaDefinition Frontline = CreateFrontline();
    private static ArenaDefinition CreateFrontline()
    {
        // Ocho tramos encadenados, con quiebros y ensanchamientos: 480 unidades de recorrido.
        FloorSection[] floors = [new(new(-210, 0), 60, 24), new(new(-150, -12), 60, 20),
            new(new(-90, 6), 60, 28), new(new(-30, -8), 60, 24), new(new(30, 8), 60, 24),
            new(new(90, -6), 60, 28), new(new(150, 12), 60, 20), new(new(210, 0), 60, 24)];
        var outline = floors.SelectMany(f => new V2[] {
            new(f.Center.X - f.Width / 2, f.Center.Y - f.Height / 2),
            new(f.Center.X + f.Width / 2, f.Center.Y - f.Height / 2) }).Concat(
            floors.Reverse().SelectMany(f => new V2[] {
                new(f.Center.X + f.Width / 2, f.Center.Y + f.Height / 2),
                new(f.Center.X - f.Width / 2, f.Center.Y + f.Height / 2) })).ToArray();
        return new(ArenaMapId.Frontline, "Frente de conquista", 240, 22, floors, outline,
            [new(new(-90, -2), 1.4f, 2.8f), new(new(-90, 14), 1.4f, 2.8f),
             new(new(-30, -15), 1.2f, 2.4f), new(new(30, 15), 1.2f, 2.4f),
             new(new(90, -14), 1.4f, 2.8f), new(new(90, 2), 1.4f, 2.8f)],
            [new(-168, -12), new(-84, 6), V2.Zero, new(84, -6), new(168, 12)], 230,
            teleports: [new(new(-192, -7), new(-138, -17)), new(new(-42, -14), new(42, 14)),
                new(new(138, 17), new(192, 7))],
            eventGates: [new(new(-180, -12), new(-180, -2)), new(new(-120, -8), new(-120, -2)),
                new(new(-60, -8), new(-60, 4)), new(new(0, -4), new(0, 4)),
                new(new(60, -4), new(60, 8)), new(new(120, 2), new(120, 8)), new(new(180, 2), new(180, 12))]);
    }
    private static readonly ArenaDefinition Classic = new(ArenaMapId.Classic, "Arena clásica", 22, 16,
        [new(V2.Zero, 44, 32)], [new(-22, -16), new(22, -16), new(22, 16), new(-22, 16)],
        ArenaMap.Obstacles.ToArray(), ArenaMap.ControlPoints.ToArray(), 18);
    // Tres salas de 22×64 y dos pasillos de 22×32: 3×1408 + 2×704 = 5632 = 4×(44×32).
    private static readonly ArenaDefinition Citadel = new(ArenaMapId.Citadel, "Ciudadela de los pasillos", 55, 32,
        [new(new(-44, 0), 22, 64), new(V2.Zero, 22, 64), new(new(44, 0), 22, 64),
         new(new(-22, 0), 22, 32), new(new(22, 0), 22, 32)],
        [new(-55, -32), new(-33, -32), new(-33, -16), new(-11, -16), new(-11, -32),
         new(11, -32), new(11, -16), new(33, -16), new(33, -32), new(55, -32),
         new(55, 32), new(33, 32), new(33, 16), new(11, 16), new(11, 32),
         new(-11, 32), new(-11, 16), new(-33, 16), new(-33, 32), new(-55, 32)],
        [new(new(-44, -8), 1.3f, 2.6f), new(new(-44, 8), 1.3f, 2.6f),
         new(new(0, -12), 1.4f, 2.6f), new(new(0, 12), 1.4f, 2.6f),
         new(new(44, -8), 1.3f, 2.6f), new(new(44, 8), 1.3f, 2.6f),
         new(new(-22, 0), 1.2f, 2), new(new(22, 0), 1.2f, 2)],
        [new(-44, -21), V2.Zero, new(44, 21)], 50);
    // Coliseo en cruz: zona central ancha y dos graderíos abiertos, con cuatro pilares simétricos.
    private static readonly ArenaDefinition Ascension = new(ArenaMapId.Ascension, "Coliseo de la ascensión", 36, 24,
        [new(V2.Zero, 72, 24), new(new(0, -18), 40, 12), new(new(0, 18), 40, 12)],
        [new(-36, -12), new(-20, -12), new(-20, -24), new(20, -24), new(20, -12), new(36, -12),
         new(36, 12), new(20, 12), new(20, 24), new(-20, 24), new(-20, 12), new(-36, 12)],
        [new(new(-12, -7), 1.5f, 3), new(new(-12, 7), 1.5f, 3),
         new(new(12, -7), 1.5f, 3), new(new(12, 7), 1.5f, 3)],
        [new(-24, 0), V2.Zero, new(24, 0)], 31);
    private static readonly ArenaDefinition Relics = new(ArenaMapId.Relics, "Santuario de las reliquias", 36, 28,
        [new(V2.Zero, 72, 56)], [new(-36, -28), new(36, -28), new(36, 28), new(-36, 28)],
        [new(new(-10, -10), 1.4f, 2.8f), new(new(10, -10), 1.4f, 2.8f),
         new(new(-10, 10), 1.4f, 2.8f), new(new(10, 10), 1.4f, 2.8f),
         new(new(0, -18), 1.2f, 2.4f), new(new(0, 18), 1.2f, 2.4f)],
        [new(-18, -10), V2.Zero, new(18, 10)], 31,
        new[] { 0, -7, 7, -14, 14, -21, 21 }.SelectMany(y => new V2[] { new(-22, y), new(22, y) }).ToArray());
    public static ArenaDefinition Get(ArenaMapId id) => id switch
    {
        ArenaMapId.Classic => Classic, ArenaMapId.Citadel => Citadel, ArenaMapId.Ascension => Ascension, ArenaMapId.Relics => Relics,
        ArenaMapId.Frontline => Frontline, ArenaMapId.Practice => Practice,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
