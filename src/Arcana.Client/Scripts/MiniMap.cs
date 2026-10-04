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
