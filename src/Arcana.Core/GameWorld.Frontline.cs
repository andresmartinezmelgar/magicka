namespace Arcana.Core;

public sealed partial class GameWorld
{
    private Random? _environmentRandom;
    private float _environmentClock;

    private int FrontierTarget(int team)
    {
        for (int offset = 0; offset < _points.Length; offset++)
        {
            int index = team == 0 ? offset : _points.Length - 1 - offset;
            if (_points[index].Owner != team) return index;
        }
        return -1;
    }

    private int TerritoryCount(int team)
    {
        int target = FrontierTarget(team);
        return target < 0 ? _points.Length : team == 0 ? target : _points.Length - 1 - target;
    }

    private V2 TeamSpawn(int team, int index) => Map.Spawn(team, index, ConquestToWin ? TerritoryCount(team) : 0);

    private void ResetFrontline() => _environmentClock = 0;

    private void TickFrontline(float dt)
    {
        if (!ConquestToWin && !IsDefense) return;
        foreach (var player in IsDefense ? _combatants.Values : _players.Values)
        {
            player.TeleportCooldown = Math.Max(0, player.TeleportCooldown - dt);
            if (player.TeleportNeedsExit)
            {
                if (Map.Teleports.Any(pair => V2.Distance(player.Position, pair.A) < 1.5f || V2.Distance(player.Position, pair.B) < 1.5f)) continue;
                player.TeleportNeedsExit = false;
            }
            if (!player.Alive || player.Frozen > 0 || player.TeleportCooldown > 0) continue;
            foreach (var pair in Map.Teleports)
            {
                V2 destination;
                if (V2.Distance(player.Position, pair.A) < 1.25f) destination = pair.B;
                else if (V2.Distance(player.Position, pair.B) < 1.25f) destination = pair.A;
                else continue;
                // Un muro en la salida bloquea el traslado; nunca coloca al jugador dentro de él.
                if (!Walkable(destination) || _players.Values.Any(p => p.Id != player.Id && p.Alive &&
                    V2.Distance(p.Position, destination) < ArenaMap.WizardRadius * 2)) break;
                Emit("blink", player.Position, [], "");
                player.Position = destination; player.TeleportCooldown = 2;
                player.TeleportNeedsExit = true;
                player.Knockback = V2.Zero; player.Input.MoveTo = false;
                ResetSlide(player);
                _routes.Remove(player.Id);
                Emit("blink_arrive", destination, [], "");
                break;
            }
        }
        if (!ConquestToWin || _players.Count == 0) return;
        var random = _environmentRandom ??= new Random(Options.EnvironmentSeed ?? Random.Shared.Next());
        if (_environmentClock <= 0) _environmentClock = 10 + (float)random.NextDouble() * 8;
        _environmentClock -= dt;
        if (_environmentClock > 0) return;
        if (random.Next(2) == 0) SpawnFrontlineMeteor(random);
        else SpawnFrontlineWall(random);
        _environmentClock = 12 + (float)random.NextDouble() * 10;
    }

    private void SpawnFrontlineMeteor(Random random)
    {
        if (_fields.Count >= 40) return;
        var active = _players.Values.Where(p => p.Alive && Math.Abs(p.Position.X) < Map.HalfWidth - 30).ToArray();
        for (int attempt = 0; attempt < 24; attempt++)
        {
            var anchor = active.Length > 0 ? active[random.Next(active.Length)].Position : _points[random.Next(_points.Length)].Position;
            var at = anchor + new V2((float)random.NextDouble() * 14 - 7, (float)random.NextDouble() * 10 - 5);
            if (!Walkable(at) || Math.Abs(at.X) > Map.HalfWidth - 30 ||
                Enumerable.Range(0, 2).Any(team => V2.Distance(at, TeamSpawn(team, 0)) < SpellBalance.MeteorRadius + 5)) continue;
            _fields.Add(new Field { Id = _nextEntity++, Owner = 0, Position = at, Radius = SpellBalance.MeteorRadius,
                Lifetime = SpellBalance.MeteorLifetime, Delay = SpellBalance.MeteorDelay, Kind = "meteor",
                Recipe = new SpellRecipe([Element.Earth, Element.Fire, Element.Fire]) });
            Emit("environment", at, [], "Meteoro en camino");
            return;
        }
    }

    private void SpawnFrontlineWall(Random random)
    {
        if (_walls.Count(w => w.Owner == 0) >= 12) return;
        int blue = FrontierTarget(0), red = FrontierTarget(1);
        float left = _points[Math.Max(0, blue)].Position.X, right = _points[Math.Max(0, red)].Position.X;
        var candidates = Map.EventGates.Where(g => g.A.X >= Math.Min(left, right) - 30 && g.A.X <= Math.Max(left, right) + 30)
            .OrderBy(_ => random.Next()).ToArray();
        foreach (var gate in candidates)
        {
            // Dos décimas de margen en los extremos; los segmentos mantienen el límite de red de 5 unidades.
            var direction = (gate.B - gate.A).Normalized;
            var a = gate.A + direction * 0.2f; var b = gate.B - direction * 0.2f;
            int count = (int)MathF.Ceiling(V2.Distance(a, b) / 4.4f);
            if (_walls.Count + count > 40 || _walls.Count(w => w.Owner == 0) + count > 12 ||
                _walls.Any(w => Geometry.DistanceToSegment((a + b) / 2, w.A, w.B) < 3) ||
                _points.Any(p => Geometry.DistanceToSegment(p.Position, a, b) < 3.5f) ||
                _players.Values.Any(p => p.Alive && Geometry.DistanceToSegment(p.Position, a, b) < 1.2f) ||
                Enumerable.Range(0, 2).Any(team => Geometry.DistanceToSegment(TeamSpawn(team, 0), a, b) < 6)) continue;
            var recipe = new SpellRecipe([Element.Shield, Element.Earth]);
            for (int i = 0; i < count; i++)
                _walls.Add(new Wall { Id = _nextEntity++, Owner = 0, A = V2.Lerp(a, b, i / (float)count),
                    B = V2.Lerp(a, b, (i + 1) / (float)count), Health = 1300, Lifetime = 30, Recipe = recipe });
            _routes.Clear();
            Emit("environment", (a + b) / 2, [Element.Earth], "Un muro bloquea el paso: destrúyelo");
            return;
        }
    }

    private bool TryAttackFrontlineWall(Wizard player, V2 goal)
    {
        foreach (var wall in _walls.Where(w => w.Owner == 0 && w.Health > 0).OrderBy(w => V2.Distance(player.Position, (w.A + w.B) / 2)))
        {
            var center = (wall.A + wall.B) / 2;
            if (V2.Distance(player.Position, center) > 14 ||
                Geometry.SegmentCapsule(player.Position, goal, wall.A, wall.B, ArenaMap.WizardRadius + 0.18f) == null) continue;
            var direction = (center - player.Position).Normalized;
            RayEnd(player.Position, direction, 14, out var hit);
            if (hit != wall) continue;
            player.Facing = direction;
            player.Input.AimX = center.X; player.Input.AimY = center.Y;
            player.Selection.Add(Element.Earth); player.Selection.Add(Element.Earth); player.Selection.Add(Element.Earth);
            BeginCast(player);
            if (player.Charging != null) player.BotWallTarget = wall.Id;
            return true;
        }
        return false;
    }
}
