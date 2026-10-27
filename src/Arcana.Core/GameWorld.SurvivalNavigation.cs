namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed record SurvivalFlow(int Goal, int[] Next);
    private readonly Dictionary<int, SurvivalFlow> _survivalFlows = [];
    private Dictionary<(int X, int Y), int>? _survivalCells;

    // Un campo compartido por objetivo sustituye una búsqueda A* por cada minion.
    // Los muros temporales se resuelven al moverse y se pueden destruir.
    private V2 SurvivalDirection(Wizard body, Wizard target)
    {
        if (ClearMovement(body.Position, target.Position)) return (target.Position - body.Position).Normalized;
        var (positions, neighbors) = Map.Navigation;
        _survivalCells ??= positions.Select((p, i) => (p, i)).ToDictionary(v => ((int)v.p.X, (int)v.p.Y), v => v.i);
        int Closest(V2 point)
        {
            int originX = 1 - (int)MathF.Ceiling(Map.HalfWidth), originY = 1 - (int)MathF.Ceiling(Map.HalfHeight);
            int x = (int)(MathF.Round((point.X - originX) / 2) * 2 + originX);
            int y = (int)(MathF.Round((point.Y - originY) / 2) * 2 + originY);
            int best = -1; float distance = float.PositiveInfinity;
            for (int dx = -2; dx <= 2; dx += 2) for (int dy = -2; dy <= 2; dy += 2)
                if (_survivalCells.TryGetValue((x + dx, y + dy), out int node))
                {
                    float d = (positions[node] - point).LengthSquared;
                    if (d < distance && ClearMovement(point, positions[node])) { best = node; distance = d; }
                }
            return best;
        }
        int goal = Closest(target.Position), at = Closest(body.Position);
        if (goal < 0 || at < 0) return V2.Zero;
        if (!_survivalFlows.TryGetValue(target.Id, out var flow) || flow.Goal != goal)
        {
            var next = Enumerable.Repeat(-1, positions.Length).ToArray();
            var queue = new Queue<int>(); next[goal] = goal; queue.Enqueue(goal);
            while (queue.TryDequeue(out int node))
                foreach (int neighbor in neighbors[node])
                    if (next[neighbor] < 0) { next[neighbor] = node; queue.Enqueue(neighbor); }
            _survivalFlows[target.Id] = flow = new(goal, next);
        }
        if (flow.Next[at] < 0) return V2.Zero;
        int look = at;
        for (int i = 0; i < 8 && flow.Next[look] != look; i++)
        {
            int next = flow.Next[look];
            if (next < 0 || !ClearMovement(body.Position, positions[next])) break;
            look = next;
        }
        return (positions[look] - body.Position).Normalized;
    }
}
