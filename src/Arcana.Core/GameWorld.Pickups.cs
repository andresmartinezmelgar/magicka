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
    private void TickPickups(float dt)
    {
        foreach (var item in _pickups)
        {
            item.Respawn = Math.Max(0, item.Respawn - dt);
            if (item.Respawn > 0) continue;
            // El servidor resuelve un único ganador, también si dos magos llegan en el mismo paso.
            var player = _players.Values.Where(p => p.Alive && V2.Distance(p.Position, item.Position) < RelicBuffs.PickupRadius)
                .OrderBy(p => (p.Position - item.Position).LengthSquared).ThenBy(p => p.Id).FirstOrDefault();
            if (player == null) continue;
            RelicBuffs.Add(player, item.Kind); item.Respawn = RelicBuffs.RespawnSeconds;
            Emit("pickup", item.Position, [], player.Name + ": " + RelicBuffs.Name(item.Kind) + " ×" + player.BuffStacks[(int)item.Kind]);
        }
    }
    private float DamagePower(int owner) => _players.TryGetValue(owner, out var source) ? RelicBuffs.Power(source) : 1;
    private float HealingPower(int owner) => _players.TryGetValue(owner, out var source) ? RelicBuffs.Healing(source) : 1;
}
