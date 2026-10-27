namespace Arcana.Core;

public sealed partial class GameWorld
{
    private sealed class Pickup
    {
        public required int Id;
        public required V2 Position;
        public BuffKind Kind;
        public float Respawn;
    }
    private readonly Pickup[] _pickups;
    private readonly Random _pickupRandom;
    private Pickup[] CreatePickups()
    {
        if (!Map.HasPickups) return [];
        var items = new List<Pickup>();
        for (int i = 0; i < RelicBuffs.SpawnCount; i++)
        {
            var position = Map.RandomPickups ? FindPickupPosition(items.Select(p => p.Position), null) : Map.ItemSpawns[i];
            items.Add(new() { Id = i + 1, Position = position, Kind = (BuffKind)(i / 2) });
        }
        return items.ToArray();
    }
    private V2 FindPickupPosition(IEnumerable<V2> occupied, V2? previous)
    {
        var positions = occupied.ToArray();
        bool Fits(V2 p) => Map.Inside(p, 1.5f) && Map.Walkable(p) &&
            Map.Obstacles.All(o => V2.Distance(p, o.Position) > o.Radius + 1.5f) &&
            Map.ControlPoints.All(c => V2.Distance(p, c) > 4) &&
            Enumerable.Range(0, 8).All(i => V2.Distance(p, Map.Spawn(i / 4, i % 4)) > 5) &&
            positions.All(at => V2.Distance(p, at) > 3) && (previous == null || V2.Distance(p, previous.Value) > 4) &&
            _players.Values.All(w => !w.Alive || V2.Distance(w.Position, p) > 2) &&
            _walls.All(w => Geometry.DistanceToSegment(p, w.A, w.B) > 1.5f);
        for (int i = 0; i < 1024; i++)
        {
            var p = new V2(((float)_pickupRandom.NextDouble() * 2 - 1) * (Map.HalfWidth - 1.5f),
                ((float)_pickupRandom.NextDouble() * 2 - 1) * (Map.HalfHeight - 1.5f));
            if (Fits(p)) return p;
        }
        // Búsqueda finita si un combate ha ocupado muchas posiciones con muros o jugadores.
        for (float x = -Map.HalfWidth + 2; x < Map.HalfWidth - 2; x += 2)
        for (float y = -Map.HalfHeight + 2; y < Map.HalfHeight - 2; y += 2)
            if (Fits(new(x, y))) return new(x, y);
        return previous ?? Map.Spawn(0, 0);
    }
    private void RelocatePickup(Pickup item) => item.Position = FindPickupPosition(
        _pickups.Where(p => p.Id != item.Id).Select(p => p.Position), item.Position);
    private void TickPickups(float dt)
    {
        foreach (var item in _pickups)
        {
            bool returning = item.Respawn > 0;
            item.Respawn = Math.Max(0, item.Respawn - dt);
            if (item.Respawn > 0) continue;
            if (returning && Map.RandomPickups) RelocatePickup(item);
            // El servidor resuelve un único ganador, también si dos magos llegan en el mismo paso.
            Wizard? player = null; float closest = float.PositiveInfinity;
            foreach (var candidate in _players.Values)
            {
                if (!candidate.Alive || !(V2.Distance(candidate.Position, item.Position) < RelicBuffs.PickupRadius)) continue;
                float distance = (candidate.Position - item.Position).LengthSquared;
                if (distance < closest || distance == closest && (player == null || candidate.Id < player.Id))
                { player = candidate; closest = distance; }
            }
            if (player == null) continue;
            _statistics.Add(StatisticGroup.Relic, (int)item.Kind, StatisticMeasure.Collected, 1);
            RelicBuffs.Add(player, item.Kind); item.Respawn = RelicBuffs.RespawnSeconds;
            Emit("pickup", item.Position, [], player.Name + ": " + RelicBuffs.Name(item.Kind) + " ×" + player.BuffStacks[(int)item.Kind]);
        }
    }
    private float DamagePower(int owner) => TryCombatSource(owner, out var source) ? RelicBuffs.Power(source) * source.Equipment.Power * DefenseDamagePower(owner) : 1;
    private float HealingPower(int owner) => TryCombatSource(owner, out var source) ? RelicBuffs.Healing(source) * source.Equipment.Healing : 1;
}
