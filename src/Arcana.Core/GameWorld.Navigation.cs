namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class Route
    {
        public V2 Goal;
        public V2[] Path = [];
        public int Next;
        public float Refresh;
    }
    private readonly Dictionary<int, Route> _routes = [];

    private V2 NavigationDirection(Wizard wizard, V2 goal, float dt)
    {
        if (ClearMovement(wizard.Position, goal)) return (goal - wizard.Position).Normalized;
        if (!_routes.TryGetValue(wizard.Id, out var route)) _routes[wizard.Id] = route = new();
        route.Refresh -= dt;
        if (route.Refresh <= 0 || V2.Distance(route.Goal, goal) > 3)
        {
            route.Goal = goal; route.Path = Map.FindPath(wizard.Position, goal, ClearWalls, allowPartial: true);
            route.Next = 0; route.Refresh = 0.8f;
        }
        while (route.Next < route.Path.Length && V2.Distance(wizard.Position, route.Path[route.Next]) < 0.6f) route.Next++;
        if (route.Next >= route.Path.Length) return V2.Zero;
        // Suaviza la ruta mirando al último nodo alcanzable sin cruzar ninguna esquina.
        for (int i = route.Next + 1; i < route.Path.Length; i++)
            if (ClearMovement(wizard.Position, route.Path[i])) route.Next = i;
            else break;
        return (route.Path[route.Next] - wizard.Position).Normalized;
    }
}
