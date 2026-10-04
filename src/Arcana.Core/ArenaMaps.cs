namespace Arcana.Core;

public enum ArenaMapId { Classic, Citadel, Ascension }
public sealed record FloorSection(V2 Center, float Width, float Height);
public sealed record MapEdge(V2 A, V2 B);

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
    public float Area => Floors.Sum(f => f.Width * f.Height);
    public int HoldSeconds => Id == ArenaMapId.Citadel ? 15 : 0;
    public float KillHealthBonus => Id == ArenaMapId.Ascension ? 10 : 0;
    public float RespawnDeathPenalty => Id == ArenaMapId.Ascension ? 0.5f : 0;
    public float RespawnSeconds(GameMode mode, int deaths) => Math.Min(10,
        (mode == GameMode.Duel ? 2.5f : 4) + RespawnDeathPenalty * Math.Max(0, deaths));
    private readonly float _spawnX;
    private readonly Lazy<(V2[] Positions, int[][] Neighbors)> _navigation;

    internal ArenaDefinition(ArenaMapId id, string name, float halfWidth, float halfHeight,
        FloorSection[] floors, V2[] outline, Obstacle[] obstacles, V2[] points, float spawnX)
    {
        Id = id; Name = name; HalfWidth = halfWidth; HalfHeight = halfHeight; _spawnX = spawnX;
        Floors = Array.AsReadOnly(floors); Obstacles = Array.AsReadOnly(obstacles); ControlPoints = Array.AsReadOnly(points);
        Boundary = Array.AsReadOnly(outline.Select((p, i) => new MapEdge(p, outline[(i + 1) % outline.Length])).ToArray());
        _navigation = new(BuildNavigation);
    }

    public V2 Spawn(int team, int index) => new(team == 0 ? -_spawnX : _spawnX, (index % 4 - 1.5f) * 2.2f);
    public bool Inside(V2 p, float radius = 0) => p.IsFinite &&
        Floors.Any(f => MathF.Abs(p.X - f.Center.X) <= f.Width / 2 && MathF.Abs(p.Y - f.Center.Y) <= f.Height / 2) &&
        Boundary.All(e => Geometry.DistanceToSegment(p, e.A, e.B) > radius);
    public bool Walkable(V2 p) => Inside(p, ArenaMap.WizardRadius) &&
        Obstacles.All(o => V2.Distance(p, o.Position) >= o.Radius + ArenaMap.WizardRadius);
    public float? BoundaryHit(V2 from, V2 to, float radius = 0)
    {
        float? best = null;
        foreach (var edge in Boundary)
            if (Geometry.SegmentCapsule(from, to, edge.A, edge.B, radius) is float t && (best == null || t < best)) best = t;
        return best;
    }
    public bool ClearPath(V2 from, V2 to) => Walkable(from) && Walkable(to) &&
        BoundaryHit(from, to, ArenaMap.WizardRadius) == null &&
        Obstacles.All(o => Geometry.SegmentCircle(from, to, o.Position, o.Radius + ArenaMap.WizardRadius) == null);

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

    /// <summary>A*: rutas por pasillos y alrededor de pilares; no permite atajos por los patios sin suelo.</summary>
    public V2[] FindPath(V2 from, V2 goal)
    {
        if (!Walkable(from) || !Walkable(goal)) return [];
        if (ClearPath(from, goal)) return [goal];
        var (positions, neighbors) = _navigation.Value;
        int Nearest(V2 p) => Enumerable.Range(0, positions.Length).OrderBy(i => (positions[i] - p).LengthSquared)
            .FirstOrDefault(i => ClearPath(p, positions[i]), -1);
        int start = Nearest(from), end = Nearest(goal);
        if (start < 0 || end < 0) return [];
        var costs = Enumerable.Repeat(float.PositiveInfinity, positions.Length).ToArray();
        var previous = Enumerable.Repeat(-1, positions.Length).ToArray(); var visited = new bool[positions.Length];
        var open = new PriorityQueue<int, float>(); costs[start] = 0; open.Enqueue(start, 0);
        while (open.TryDequeue(out int at, out _))
        {
            if (visited[at]) continue;
            visited[at] = true;
            if (at == end)
            {
                var path = new List<V2> { goal };
                for (int node = end; node >= 0; node = previous[node]) path.Add(positions[node]);
                path.Reverse(); return path.ToArray();
            }
            foreach (int next in neighbors[at])
            {
                float cost = costs[at] + V2.Distance(positions[at], positions[next]);
                if (cost >= costs[next]) continue;
                costs[next] = cost; previous[next] = at;
                open.Enqueue(next, cost + V2.Distance(positions[next], positions[end]));
            }
        }
        return [];
    }
}

internal static class ArenaMaps
{
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
    public static ArenaDefinition Get(ArenaMapId id) => id switch
    {
        ArenaMapId.Classic => Classic, ArenaMapId.Citadel => Citadel, ArenaMapId.Ascension => Ascension,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
