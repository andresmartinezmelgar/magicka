namespace Arcana.Core;

public sealed partial class GameWorld
{
    private void ThinkBot(Wizard p, float dt)
    {
        Wizard? target = null; float closest = float.MaxValue;
        foreach (var candidate in _players.Values)
        {
            if (!candidate.Alive || !Hostile(p, candidate)) continue;
            float squared = (p.Position - candidate.Position).LengthSquared;
            if (squared < closest) { closest = squared; target = candidate; }
        }
        V2 goal = target?.Position ?? Map.ControlPoints[p.Id % Map.ControlPoints.Count];
        if (Options.Mode == GameMode.Warfare && (target == null || V2.Distance(p.Position, target.Position) > 12))
        {
            int frontier = ConquestToWin ? FrontierTarget(p.Team) : -1;
            ControlPoint? point = null; float nearest = float.PositiveInfinity;
            foreach (var candidate in _points)
            {
                if (ConquestToWin ? candidate.Id != frontier : candidate.Owner == p.Team && !candidate.Contested) continue;
                float range = V2.Distance(candidate.Position, p.Position);
                if (range < nearest) { nearest = range; point = candidate; }
            }
            goal = point?.Position ?? _points[p.Id % _points.Length].Position;
        }
        float distance = target == null ? 100 : V2.Distance(p.Position, target.Position);
        if (_pickups.Length > 0 && (target == null || distance > 9))
        {
            Pickup? item = null; float nearest = float.PositiveInfinity;
            foreach (var candidate in _pickups)
            {
                if (!(candidate.Respawn <= 0)) continue;
                float range = V2.Distance(p.Position, candidate.Position);
                if (range < nearest) { nearest = range; item = candidate; }
            }
            if (item != null && V2.Distance(p.Position, item.Position) < 12) goal = item.Position;
        }
        var direction = (goal - p.Position).Normalized;
        if (target != null && distance < 8) direction = direction.Perpendicular * MathF.Sin((float)Time * 0.65f + p.Id);
        if (target != null && distance < 3.5f) direction = (p.Position - target.Position).Normalized;
        if (Map.Id != ArenaMapId.Classic && (target == null || distance > 12 || !Visible(p.Position, target.Position, p.Id) || !Walkable(p.Position + direction * 1.4f)))
            direction = NavigationDirection(p, goal, dt);
        foreach (var o in Map.Obstacles)
            if (V2.Distance(p.Position + direction * 1.6f, o.Position) < o.Radius + 1)
                direction = (p.Position - o.Position).Normalized.Perpendicular * (p.Id % 2 == 0 ? 1 : -1);
        var aim = target?.Position ?? goal;
        if (p.BotWallTarget > 0 && p.Charging != null)
        {
            var wall = _walls.FirstOrDefault(w => w.Id == p.BotWallTarget && w.Health > 0);
            if (wall != null) aim = (wall.A + wall.B) / 2;
            else CancelCast(p);
        }
        p.Input = new() { Type = "input", MoveX = direction.X, MoveY = direction.Y, AimX = aim.X, AimY = aim.Y };
        p.LastInput = Time;
        UpdateFacing(p);
        p.BotDecision -= dt;
        if (p.BotDecision > 0 || p.Frozen > 0 || !Ready(p)) return;
        p.BotDecision = 0.6f + (float)_random.NextDouble() * 0.5f;
        if (p.Channel != null || p.Charging != null) Release(p);
        p.Selection.Clear();
        if (p.Health < p.MaxHealth * 0.6f || p.Burning > 0)
        {
            if (p.Burning > 0) p.Selection.Add(Element.Water);
            else for (int i = 0; i < 3; i++) p.Selection.Add(Element.Life);
            SelfCast(p); return;
        }
        if (ConquestToWin && (target == null || distance > 12 || !Visible(p.Position, target.Position, p.Id)) && TryAttackFrontlineWall(p, goal)) return;
        if (target == null || distance > 18 || !Visible(p.Position, target.Position, p.Id)) return;
        if (p.Shield <= 0 && _random.Next(8) == 0)
        {
            p.Selection.Add(Element.Shield); SelfCast(p); return;
        }
        if (p.Energy >= 75 && _random.Next(16) == 0) { Magick(p, 2); return; }
        int choice = distance > 9 ? _random.Next(2) == 0 ? 0 : 3 : _random.Next(BotRecipes.Length);
        foreach (var element in BotRecipes[choice]) p.Selection.Add(element);
        p.Facing = (aim - p.Position).Normalized;
        BeginCast(p);
    }
}
