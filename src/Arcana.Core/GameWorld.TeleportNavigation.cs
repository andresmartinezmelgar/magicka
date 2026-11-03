namespace Arcana.Core;

public sealed partial class GameWorld
{
    // Compara recorridos a pie y saltos bidireccionales sobre las rutas reales,
    // incluidos los pasillos y muros. Puede encadenar las dos parejas de portales.
    private V2[] FindTeleportPath(V2 from, V2 goal)
    {
        var points = new List<V2> { from, goal };
        foreach (var pair in Map.Teleports)
        {
            if (!Walkable(pair.A) || !Walkable(pair.B)) continue;
            points.Add(pair.A); points.Add(pair.B);
        }
        if (points.Count == 2) return Map.FindPath(from, goal, ClearWalls, allowPartial: true);
        var costs = Enumerable.Repeat(float.PositiveInfinity, points.Count).ToArray();
        var previous = Enumerable.Repeat(-1, points.Count).ToArray();
        var visited = new bool[points.Count];
        var paths = new Dictionary<(int From, int To), V2[]>();
        var lengths = new Dictionary<(int From, int To), float>();
        bool Portal(int a, int b) => a >= 2 && b == (a ^ 1);
        float WalkingCost(int a, int b)
        {
            if (lengths.TryGetValue((a, b), out float saved)) return saved;
            var path = Map.FindPath(points[a], points[b], ClearWalls);
            float length = path.Length == 0 ? float.PositiveInfinity : 0;
            var at = points[a];
            foreach (var point in path) { length += V2.Distance(at, point); at = point; }
            paths[(a, b)] = path;
            paths[(b, a)] = path.Length == 0 ? [] : path[..^1].Reverse().Append(points[a]).ToArray();
            lengths[(a, b)] = lengths[(b, a)] = length;
            return length;
        }
        costs[0] = 0;
        // Una cota inicial permite descartar conexiones que ya son más largas
        // que caminar hasta el destino, sin ejecutar otra búsqueda de pasillos.
        costs[1] = WalkingCost(0, 1); previous[1] = 0;
        for (int turn = 0; turn < points.Count; turn++)
        {
            int at = -1;
            for (int i = 0; i < points.Count; i++)
                if (!visited[i] && float.IsFinite(costs[i]) && (at < 0 || costs[i] < costs[at])) at = i;
            if (at < 0 || at == 1) break;
            visited[at] = true;
            for (int next = 1; next < points.Count; next++)
            {
                if (visited[next] || next == at) continue;
                // Dos metros de coste evitan elegir un salto para un ahorro
                // insignificante. El enfriamiento real lo conserva TickFrontline.
                bool portal = Portal(at, next);
                float minimum = portal ? 2 : V2.Distance(points[at], points[next]);
                if (costs[at] + minimum >= Math.Min(costs[next], costs[1])) continue;
                float cost = costs[at] + (portal ? 2 : WalkingCost(at, next));
                if (cost >= costs[next] || cost >= costs[1]) continue;
                costs[next] = cost; previous[next] = at;
            }
        }
        if (!float.IsFinite(costs[1])) return Map.FindPath(from, goal, ClearWalls, allowPartial: true);
        var nodes = new List<int>();
        for (int at = 1; at >= 0; at = previous[at]) nodes.Add(at);
        nodes.Reverse();
        var result = new List<V2>();
        for (int i = 1; i < nodes.Count; i++)
        {
            int a = nodes[i - 1], b = nodes[i];
            // Solo se entrega el tramo hasta la entrada. Nunca se suaviza una
            // ruta atravesando el salto: el portal normal traslada al bot y
            // elimina su ruta para recalcular desde la salida.
            if (Portal(a, b)) break;
            result.AddRange(paths[(a, b)]);
        }
        return result.ToArray();
    }
}
