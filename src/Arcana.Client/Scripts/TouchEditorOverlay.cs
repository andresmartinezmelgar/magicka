using Godot;

namespace Arcana.Client;

internal partial class TouchEditorOverlay : Control
{
    internal const float GripSize = 20;
    internal Rect2 Target;
    internal Rect2 Grip
    {
        get
        {
            float side = Math.Min(GripSize, Math.Min(Target.Size.X, Target.Size.Y) * .4f);
            return new(Target.End - Vector2.One * side, Vector2.One * side);
        }
    }
    public override void _Draw()
    {
        DrawRect(Target.Grow(3), Ui.Gold, false, 2);
        DrawRect(Grip, new Color("243a55")); DrawRect(Grip, Ui.Gold, false, 2);
        DrawLine(Grip.Position + Grip.Size * new Vector2(.25f, .75f), Grip.Position + Grip.Size * new Vector2(.75f, .25f), Ui.Gold, 2, true);
    }
}
