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
        Vector2 Point(V2 p) => new((p.X / 44 + 0.5f) * Size.X, (p.Y / 32 + 0.5f) * Size.Y);
        DrawRect(new(Vector2.Zero, Size), new("14232d"));
        foreach (var obstacle in ArenaMap.Obstacles) DrawCircle(Point(obstacle.Position), obstacle.Radius / 44 * Size.X, new("526a70"));
        if (State.Mode == GameMode.Warfare)
            foreach (var p in State.Points) DrawCircle(Point(p.Position), 6, p.Owner == 0 ? Ui.Blue.Darkened(0.3f) : p.Owner == 1 ? Ui.Red.Darkened(0.3f) : Ui.Muted.Darkened(0.5f));
        foreach (var p in State.Players.Where(p => p.Alive))
        {
            var pos = Point(p.Position); DrawCircle(pos, p.Id == LocalId ? 3.8f : 2.8f, p.Team == 0 ? Ui.Blue : Ui.Red);
            if (p.Id == LocalId) DrawArc(pos, 5.8f, 0, Mathf.Tau, 20, Ui.Gold, 1.2f, true);
        }
        DrawRect(new(Vector2.Zero, Size), Ui.Muted.Darkened(0.5f), false, 1);
    }
}
