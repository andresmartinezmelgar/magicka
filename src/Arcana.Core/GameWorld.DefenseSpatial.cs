namespace Arcana.Core;

public sealed partial class GameWorld
{
    private readonly Dictionary<(int X, int Y), List<DefenseMinion>> _defenseCells = [];
    private readonly List<DefenseMinion> _defenseNeighbors = [];
    private static (int X, int Y) DefenseCell(V2 p) => ((int)MathF.Floor(p.X / 2), (int)MathF.Floor(p.Y / 2));

    private void BuildDefenseCells()
    {
        foreach (var cell in _defenseCells.Values) cell.Clear();
        int order = 0;
        foreach (var minion in _minions.Values)
        { minion.NeighborhoodOrder = order++; DefenseCellList(DefenseCell(minion.Body.Position)).Add(minion); }
    }
    private List<DefenseMinion> DefenseCellList((int X, int Y) key)
    {
        if (!_defenseCells.TryGetValue(key, out var list)) _defenseCells[key] = list = [];
        return list;
    }
    private V2 DefenseSeparation(DefenseMinion minion)
    {
        _defenseNeighbors.Clear(); var cell = DefenseCell(minion.Body.Position);
        for (int x = cell.X - 1; x <= cell.X + 1; x++)
            for (int y = cell.Y - 1; y <= cell.Y + 1; y++)
                if (_defenseCells.TryGetValue((x, y), out var list)) _defenseNeighbors.AddRange(list);
        // Conserva el orden de suma de la simulación anterior, también al cruzar celdas.
        _defenseNeighbors.Sort(static (a, b) => a.NeighborhoodOrder.CompareTo(b.NeighborhoodOrder));
        V2 separation = V2.Zero;
        foreach (var other in _defenseNeighbors)
        {
            if (other.Body.Id == minion.Body.Id || !other.Body.Alive) continue;
            var away = minion.Body.Position - other.Body.Position;
            if (away.LengthSquared is > 0.0001f and < 1.2f) separation += away.Normalized * (1.1f - away.Length);
        }
        return separation;
    }
}
