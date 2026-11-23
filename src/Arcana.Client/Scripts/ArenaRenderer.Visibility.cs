using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private WorldSnapshot VisibleState(WorldSnapshot snapshot)
    {
        if (snapshot.Defense == null) return snapshot;
        var bounds = (_survivalCenter, _survivalHalfX, _survivalHalfY);
        bool sameSource = ReferenceEquals(snapshot, _visibleSource);
        if (sameSource && bounds == _visibleBounds && _visibleState != null) return _visibleState;
        var players = VisibleItems(snapshot.Players, _visibleState?.Players,
            _visiblePlayerFilter ??= p => NearSurvival(p.Position));
        var projectiles = VisibleItems(snapshot.Projectiles, _visibleState?.Projectiles,
            _visibleProjectileFilter ??= p => NearSurvival(p.Position));
        var walls = VisibleItems(snapshot.Walls, _visibleState?.Walls,
            _visibleWallFilter ??= p => NearSurvivalSegment(p.A, p.B));
        var fields = VisibleItems(snapshot.Fields, _visibleState?.Fields,
            _visibleFieldFilter ??= p => NearSurvival(p.Position, p.Radius + 4));
        var beams = VisibleItems(snapshot.Beams, _visibleState?.Beams,
            _visibleBeamFilter ??= p => NearSurvivalSegment(p.From, p.To));
        _visibleSource = snapshot; _visibleBounds = bounds;
        if (sameSource && _visibleState != null && ReferenceEquals(players, _visibleState.Players) &&
            ReferenceEquals(projectiles, _visibleState.Projectiles) && ReferenceEquals(walls, _visibleState.Walls) &&
            ReferenceEquals(fields, _visibleState.Fields) && ReferenceEquals(beams, _visibleState.Beams)) return _visibleState;
        if (ReferenceEquals(players, snapshot.Players) && ReferenceEquals(projectiles, snapshot.Projectiles) &&
            ReferenceEquals(walls, snapshot.Walls) && ReferenceEquals(fields, snapshot.Fields) && ReferenceEquals(beams, snapshot.Beams))
            return _visibleState = snapshot;
        _visibleState = snapshot with
        {
            Players = players, Projectiles = projectiles, Walls = walls, Fields = fields, Beams = beams
        };
        return _visibleState;
    }

    private static T[] VisibleItems<T>(T[] source, T[]? previous, Predicate<T> visible) where T : class
    {
        int count = 0;
        bool unchanged = previous != null;
        foreach (var item in source)
        {
            if (!visible(item)) continue;
            if (unchanged && (count >= previous!.Length || !ReferenceEquals(previous[count], item))) unchanged = false;
            count++;
        }
        // Solo se comparten arrays de snapshots inmutables; nunca un buffer que se sobrescriba después.
        if (count == source.Length) return source;
        if (unchanged && count == previous!.Length) return previous!;
        if (count == 0) return [];
        var result = new T[count]; int index = 0;
        foreach (var item in source) if (visible(item)) result[index++] = item;
        return result;
    }

    private void RemoveMissing<T, TState>(Dictionary<int, T> nodes, TState[] present, Func<TState, int> id) where T : Node
    {
        _present.Clear(); _removed.Clear();
        foreach (var item in present) _present.Add(id(item));
        foreach (int key in nodes.Keys) if (!_present.Contains(key)) _removed.Add(key);
        foreach (int key in _removed) { nodes[key].QueueFree(); nodes.Remove(key); }
    }
}
