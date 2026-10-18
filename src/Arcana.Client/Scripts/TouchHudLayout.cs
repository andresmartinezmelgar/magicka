using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal readonly record struct TouchHudArea(string Name, Rect2 Bounds);

internal static class TouchHudLayout
{
    internal static ScreenRect Rect(Rect2 rect) => new(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
    internal static Rect2 Rect(ScreenRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    internal static void Apply(MobileControls controls, Hud hud, List<TouchHudArea> areas)
    {
        hud.AppendTouchAreas(areas);
        controls.SetHudAreas(areas, hud.PreferredTouchMap);
        if (controls.MapRectangle is { } map) hud.PlaceTouchMap(map);
    }
}
