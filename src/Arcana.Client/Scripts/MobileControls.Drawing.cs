using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class MobileControls
{
    public override void _Draw()
    {
        if (EditorMode || !Settings.Hidden("Move")) DrawStick(_moveCenter, _move, "MOVER", Ui.Blue, MoveRadius);
        if (EditorMode || !Settings.Hidden("Aim")) DrawStick(_aimCenter, _aimKnob, _pendingMagick >= 0 ? "SUELTA: " + SpellBalance.Magicks[_pendingMagick].Name.ToUpperInvariant() : _directionalAimPending
            ? _aimedSpellShape == SpellShape.Shards ? "SUELTA: HIELO" : "SUELTA: ESCUDO"
            : "APUNTAR / LANZAR", Ui.Gold, AimRadius);
    }

    private void DrawStick(Vector2 center, Vector2 direction, string caption, Color color, float radius)
    {
        float scale = radius / Radius;
        DrawCircle(center, radius, new Color(0.035f, 0.055f, 0.09f, 0.86f));
        DrawArc(center, radius - 3, 0, Mathf.Tau, 48, new Color(color, 0.92f), 6, true);
        DrawCircle(center + direction * (radius - 27 * scale), 27 * scale, new Color(color, 0.68f));
        var font = ThemeDB.FallbackFont;
        int fontSize = Math.Max(8, (int)(18 * scale));
        float available = radius * 2 - 12 * scale;
        while (fontSize > 8 && font.GetStringSize(caption, fontSize: fontSize).X > available) fontSize--;
        while (caption.Length > 1 && font.GetStringSize(caption, fontSize: fontSize).X > available) caption = caption[..^2] + "…";
        float textWidth = font.GetStringSize(caption, fontSize: fontSize).X;
        DrawString(font, center + new Vector2(-textWidth / 2, -radius + 28 * scale), caption, fontSize: fontSize, modulate: color);
    }
}
