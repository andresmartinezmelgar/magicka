using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MiniMap : Control
{
    public WorldSnapshot? State;
    public int LocalId;
    public override void _Draw()
    {
        if (State == null) return;
        var map = ArenaMap.Get(State.Map);
        float scale = Math.Min(Size.X / (map.HalfWidth * 2), Size.Y / (map.HalfHeight * 2));
        Vector2 Point(V2 p) => Size / 2 + new Vector2(p.X, p.Y) * scale;
        DrawRect(new(Vector2.Zero, Size), new("14232d"));
        foreach (var floor in map.Floors)
            DrawRect(new(Point(floor.Center) - new Vector2(floor.Width, floor.Height) * scale / 2,
                new Vector2(floor.Width, floor.Height) * scale), new("293c43"));
        foreach (var edge in map.Boundary) DrawLine(Point(edge.A), Point(edge.B), new("718784"), 1, true);
        foreach (var obstacle in map.Obstacles) DrawCircle(Point(obstacle.Position), Math.Max(1, obstacle.Radius * scale), new("526a70"));
        if (map.IsPractice)
        {
            DrawArc(Point(PracticeLayout.Respawn), 5, 0, Mathf.Tau, 16, Ui.Blue, 2, true);
            foreach (var station in PracticeLayout.Barriers)
                DrawRect(new(Point(station.Position) - new Vector2(1.5f, 1.5f), new(3, 3)), Ui.Gold);
        }
        for (int i = 0; i < map.Teleports.Count; i++)
        {
            var pair = map.Teleports[i]; var color = i == 0 ? new Color("59ddd0") : i == 1 ? new Color("cea1ff") : new Color("ffd080");
            DrawArc(Point(pair.A), 3, 0, Mathf.Tau, 16, color, 1.5f, true);
            DrawArc(Point(pair.B), 3, 0, Mathf.Tau, 16, color, 1.5f, true);
        }
        foreach (var wall in State.Walls) DrawLine(Point(wall.A), Point(wall.B), Ui.Gold, 2, true);
        foreach (var field in State.Fields.Where(f => f.Kind == "meteor")) DrawArc(Point(field.Position), Math.Max(3, field.Radius * scale), 0, Mathf.Tau, 16, Ui.Red, 1.5f, true);
        if (map.SequentialCapture)
            for (int team = 0; team < 2; team++)
            {
                int count = 0;
                foreach (var point in team == 0 ? State.Points : State.Points.Reverse())
                { if (point.Owner != team) break; count++; }
                var at = map.Spawn(team, 0, count);
                DrawRect(new(Point(new(at.X, at.Y + (count == 0 ? 3.3f : 2.7f))) - new Vector2(2, 2), new(4, 4)), team == 0 ? Ui.Blue : Ui.Red, false, 1);
            }
        foreach (var item in State.Pickups ?? [])
            if (item.Active) DrawRect(new(Point(item.Position) - new Vector2(2, 2), new(4, 4)), RelicVisual.ColorFor(item.Kind));
        if (State.Mode == GameMode.Warfare)
            foreach (var p in State.Points)
            {
                var at = Point(p.Position);
                DrawCircle(at, 6, Ui.Muted.Darkened(0.6f));
                if (Math.Abs(p.Progress) > 0.005f)
                    DrawArc(at, 5, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Math.Abs(p.Progress), 32,
                        p.Progress < 0 ? Ui.Blue : Ui.Red, 3, true);
                if (p.Contested) DrawArc(at, 7, 0, Mathf.Tau, 24, Ui.Text, 1, true);
            }
        if (State.Defense is { } defense)
        {
            DrawRect(new(Point(defense.CastlePosition) - new Vector2(4, 4), new(8, 8)), Ui.Blue);
            foreach (var enemy in defense.Minions) DrawCircle(Point(enemy.Position), 1.6f, Ui.Red);
        }
        foreach (var p in State.Players.Where(p => p.Alive))
        {
            var pos = Point(p.Position); float radius = p.Id == LocalId ? 4 : 3;
            if (p.Team == 0) DrawCircle(pos, radius, Ui.Blue);
            else DrawColoredPolygon([pos + Vector2.Up * radius, pos + Vector2.Right * radius,
                pos + Vector2.Down * radius, pos + Vector2.Left * radius], Ui.Red);
            if (p.Id == LocalId) DrawArc(pos, 6.2f, 0, Mathf.Tau, 20, Ui.Text, 1.5f, true);
        }
        DrawRect(new(Vector2.Zero, Size), Ui.Muted.Darkened(0.5f), false, 1);
    }
}
