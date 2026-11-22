using Godot;

namespace Arcana.Client;

/// <summary>Ilustraciones vectoriales de tienda, ajustadas al cuadrado y sin texturas externas.</summary>
internal partial class StoreIllustration : Control
{
    internal string ThemeKind { get; init; } = "gold";
    internal int Level { get; init; }
    internal bool Forever { get; init; }
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; Resized += QueueRedraw; }
    public override void _Draw()
    {
        float scale = Mathf.Min(Size.X, Size.Y) / 104f;
        DrawSetTransform((Size - Vector2.One * 100 * scale) / 2, 0, Vector2.One * scale);
        Color color = ThemeKind == "gold" ? new("efcb78") : Forever ? new("f4d08a") : new("b698ff");
        DrawCircle(new(50, 50), 44, new Color(color, .06f));
        DrawCircle(new(50, 50), 36, new Color(color, .08f));
        if (ThemeKind == "gold")
        {
            if (Level >= 2)
            {
                DrawColoredPolygon([new(22, 42), new(34, 30), new(73, 30), new(82, 42), new(82, 73), new(22, 73)], new("644322"));
                DrawPolyline([new(22, 42), new(82, 42), new(82, 73), new(22, 73), new(22, 42)], color, 2, true);
                DrawLine(new(37, 34), new(37, 71), new("c69748"), 4, true);
                DrawLine(new(68, 34), new(68, 71), new("c69748"), 4, true);
                DrawRect(new(48, 44, 10, 15), new("ffe3a0"));
            }
            for (int stack = 0; stack <= Math.Min(Level + 1, 3); stack++)
                for (int coin = 0; coin < 2 + (stack + Level) % 3; coin++)
                {
                    var center = new Vector2(23 + stack * 17, 79 - coin * 5);
                    DrawCircle(center + new Vector2(0, 2), 9, new("8e612c"));
                    DrawCircle(center, 8, new("e8b64d"));
                    DrawArc(center, 5.6f, 0, Mathf.Tau, 20, new("ffe3a0"), 1, true);
                }
        }
        else
        {
            Vector2[] shield = [new(50, 16), new(79, 27), new(76, 57), new(66, 73), new(50, 86), new(34, 73), new(24, 57), new(21, 27)];
            DrawColoredPolygon(shield, new("322c50"));
            DrawPolyline(shield.Append(shield[0]).ToArray(), color, 2.5f, true);
            if (Forever)
            {
                Vector2[] infinity = Enumerable.Range(0, 65).Select(i =>
                { float t = i * Mathf.Tau / 64; return new Vector2(50 + 21 * Mathf.Cos(t), 49 + 12 * Mathf.Sin(2 * t)); }).ToArray();
                DrawPolyline(infinity, color, 3, true);
            }
            else
            {
                DrawArc(new(50, 48), 19, 0, Mathf.Tau, 40, color, 2, true);
                string value = Level == 6 ? "6" : "1"; var font = ThemeDB.FallbackFont;
                var size = font.GetStringSize(value, fontSize: 23);
                DrawString(font, new(50 - size.X / 2, 56), value, fontSize: 23, modulate: color);
            }
        }
        foreach (var point in new[] { new Vector2(18, 22), new Vector2(83, 18), new Vector2(88, 66) })
        {
            DrawLine(point - new Vector2(0, 3), point + new Vector2(0, 3), color, 1.2f, true);
            DrawLine(point - new Vector2(3, 0), point + new Vector2(3, 0), color, 1.2f, true);
        }
    }
}
