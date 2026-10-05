namespace Arcana.Core;

public sealed partial class GameWorld
{
    // Independiente de las decisiones de los bots y de las reliquias; cambia entre salas.
    private Random? _spawnRandom;

    private V2 ArenaSpawnPosition(Wizard player, V2? previous)
    {
        var random = _spawnRandom ??= new Random(Options.SpawnSeed ?? Random.Shared.Next());
        float Clearance(V2 at)
        {
            float result = float.PositiveInfinity;
            foreach (var other in _players.Values)
                if (other.Id != player.Id && other.Alive)
                    result = Math.Min(result, V2.Distance(at, other.Position) - 3);
            foreach (var field in _fields)
                if (field.Lifetime > 0 && field.Kind != "healing_mine")
                    result = Math.Min(result, V2.Distance(at, field.Position) - field.Radius - ArenaMap.WizardRadius);
            if (previous is { } before) result = Math.Min(result, V2.Distance(at, before) - 4);
            return result;
        }
        // Muestreo uniforme del mapa entero, sin asociar lado ni equipo al jugador.
        for (int attempt = 0; attempt < 128; attempt++)
        {
            var at = new V2(((float)random.NextDouble() * 2 - 1) * Map.HalfWidth,
                ((float)random.NextDouble() * 2 - 1) * Map.HalfHeight);
            if (Walkable(at) && Clearance(at) >= 0) return at;
        }
        // En una arena congestionada, conserva suelo libre y elige el mayor margen disponible.
        V2 best = player.Position; float bestClearance = float.NegativeInfinity; int ties = 0;
        for (float x = -Map.HalfWidth + 1; x < Map.HalfWidth; x++)
        for (float y = -Map.HalfHeight + 1; y < Map.HalfHeight; y++)
        {
            var at = new V2(x, y);
            if (!Walkable(at)) continue;
            float clearance = Clearance(at);
            if (clearance > bestClearance) { bestClearance = clearance; best = at; ties = 1; }
            else if (clearance == bestClearance && random.Next(++ties) == 0) best = at;
        }
        return best;
    }
}
