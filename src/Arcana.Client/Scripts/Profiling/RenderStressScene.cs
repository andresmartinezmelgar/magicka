using Arcana.Core;

namespace Arcana.Client;

/// <summary>Escena artificial y reproducible de carga gráfica; no mide combate, red ni IA.</summary>
internal sealed class RenderStressScene
{
    private readonly WorldSnapshot _base;
    private readonly V2[] _positions;
    private readonly WallSnapshot[] _walls;
    private readonly ArenaDefinition _map;
    internal const int Players = 16, Projectiles = 128, Walls = 40, Fields = 40, Beams = 64, Events = 160;

    internal RenderStressScene(ArenaMapId mapId)
    {
        var world = new GameWorld(new() { Map = mapId, Mode = ArenaMap.Get(mapId).SequentialCapture ? GameMode.Warfare : GameMode.Arena,
            MaxPlayers = Players, SpawnSeed = 271, PickupSeed = 271 });
        for (int i = 0; i < Players; i++) world.AddPlayer("Mago " + (i + 1), i % 2, wizardType: (WizardType)(i % 5));
        var map = _map = world.Map;
        var candidates = new List<V2>();
        for (float x = -map.HalfWidth + 3; x < map.HalfWidth - 3; x += 3)
        for (float y = -map.HalfHeight + 3; y < map.HalfHeight - 3; y += 3)
            if (map.Walkable(new(x, y))) candidates.Add(new(x, y));
        _positions = candidates.OrderBy(p => p.LengthSquared).ToArray();
        var wallPositions = _positions.Where(p => map.Inside(p + new V2(-2.2f, 0)) && map.Inside(p + new V2(2.2f, 0)) &&
            map.BoundaryHit(p + new V2(-2.2f, 0), p + new V2(2.2f, 0)) == null).ToArray();
        if (_positions.Length < Players || wallPositions.Length == 0) throw new InvalidDataException("El mapa no admite la escena de rendimiento.");
        _base = world.Snapshot();
        _walls = Enumerable.Range(0, Walls).Select(i =>
        {
            var at = wallPositions[i % wallPositions.Length];
            Element[] elements = (i % 4) switch
            {
                0 => [Element.Shield, Element.Earth, Element.Earth], 1 => [Element.Shield, Element.Earth, Element.Fire],
                2 => [Element.Shield, Element.Water, Element.Cold], _ => [Element.Shield, Element.Lightning, Element.Lightning]
            };
            return new WallSnapshot(129 + i, 1, at + new V2(-2.2f, 0), at + new V2(2.2f, 0), 1300, 10, elements);
        }).ToArray();
    }

    internal WorldSnapshot Frame(int frame)
    {
        float phase = frame / 60f;
        var state = _base with
        {
            Tick = frame, Time = phase,
            Players = _base.Players.Select((p, i) => p with
            {
                Position = _positions[i], Facing = new V2(1, 0).Rotated(phase + i), InvulnerableSeconds = 0,
                Shield = 420, ShieldMax = 420, ShieldSeconds = 12, ShieldCharges = 3, Ward = [Element.Fire, Element.Cold],
                Selected = [Element.Arcane, Element.Arcane, Element.Fire], CastingElements = [Element.Arcane, Element.Arcane, Element.Fire],
                Casting = true, Charge = 1, Wet = 1, Burning = 1, Haste = 1
            }).ToArray(),
            Projectiles = Enumerable.Range(0, Projectiles).Select(i => new ProjectileSnapshot(i + 1, i % Players + 1,
                _positions[i % _positions.Length], new V2(1, 0).Rotated(phase + i), [Element.Earth, Element.Earth, Element.Fire], 0.5f)).ToArray(),
            Walls = _walls,
            Fields = Enumerable.Range(0, Fields).Select(i => new FieldSnapshot(169 + i, i % Players + 1,
                _positions[i % _positions.Length], i % 4 == 0 ? 4 : 2.4f, i % 4 == 0 ? 1.5f - frame % 90 / 60f : 4,
                i % 4 == 0 ? [Element.Earth, Element.Fire, Element.Arcane] : [Element.Shield, Element.Water, Element.Cold],
                (i % 4) switch { 0 => "meteor", 1 => "frost_ground", 2 => "water_pool", _ => "mine" }, 0.85f, true)).ToArray(),
            Beams = Enumerable.Range(0, Beams).Select(i =>
            {
                var at = _positions[i % _positions.Length];
                var shape = (i % 4) switch { 0 => SpellShape.Beam, 1 => SpellShape.Lightning, 2 => SpellShape.Spray, _ => SpellShape.Force };
                float range = shape switch { SpellShape.Beam => 17, SpellShape.Lightning => 8.5f, SpellShape.Spray => 6.5f, _ => 4 };
                var end = at + new V2(1, 0).Rotated(i * 0.7f) * range;
                if (_map.BoundaryHit(at, end) is float hit) end = V2.Lerp(at, end, Math.Max(0, hit - 0.001f));
                return new BeamSnapshot(i % Players + 1, at, end, [Element.Arcane, Element.Fire, Element.Lightning], shape, 3, "direct");
            }).ToArray(),
            Events = Enumerable.Range(0, Events).Select(i => new GameEvent((long)frame * Events + i + 1,
                (i % 3) switch { 0 => "nova", 1 => "explosion", _ => "impact" }, _positions[i % _positions.Length],
                [Element.Fire, Element.Fire, Element.Arcane], "", 4)).ToArray()
        };
        return state;
    }
}
