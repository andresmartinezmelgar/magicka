using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private bool DrawModernEnergy(BeamSnapshot beam, bool details)
    {
        bool lightning = beam.Shape == SpellShape.Lightning;
        if (!lightning && (beam.Shape != SpellShape.Beam || !beam.Elements.Contains(Element.Arcane))) return false;
        var from = FlameOrigin(beam); var to = World(beam.To, from.Y);
        float length = from.DistanceTo(to);
        if (length < .05f) return true;
        var direction = (to - from) / length;
        var side = Vector3.Up.Cross(direction).Normalized();
        var up = direction.Cross(side).Normalized();
        var strength = SpellVisualStrength.For(beam.Elements);
        if (lightning) DrawElectricChannel(beam, from, to, direction, side, up, strength, details);
        else DrawArcaneChannel(beam, from, to, direction, side, up, strength, details);
        return true;
    }

    private void DrawElectricChannel(BeamSnapshot beam, Vector3 from, Vector3 to, Vector3 direction,
        Vector3 side, Vector3 up, SpellVisualStrength strength, bool details)
    {
        const int segments = 8;
        float tick = MathF.Floor((float)_time * 14);
        var tint = beam.Elements.Contains(Element.Fire) ? new Color("ffbd82")
            : beam.Elements.Contains(Element.Cold) ? new Color("c5f1ff") : new Color("83d9ff");
        var previous = from;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments, envelope = MathF.Sin(t * MathF.PI);
            float amplitude = Math.Min(.10f + strength.Charges * .05f, from.DistanceTo(to) * .08f);
            var next = from.Lerp(to, t) + (side * MathF.Sin(tick * 7.13f + i * 12.71f + beam.Owner * .37f) +
                up * MathF.Sin(tick * 5.7f + i * 8.3f) * .30f) * (envelope * amplitude);
            if (i == segments) next = to;
            // El núcleo de todos los autores se dibuja antes de gastar presupuesto en brillo o ramas.
            BeamSegment(previous, next, details ? new Color(tint, .24f) : new Color("effbff"),
                details ? .065f + strength.Charges * .012f : .016f + strength.Charges * .005f, profile: 7);
            if (details)
            for (int branch = 0; branch < strength.LightningBranches; branch++)
            {
                if (i != 2 + branch * 2) continue;
                float span = Math.Min(.65f, from.DistanceTo(to) * (1 - t) * .7f);
                var tip = next + direction * span + side * (span * .65f * (branch % 2 == 0 ? 1 : -1));
                var middle = next.Lerp(tip, .55f) + up * (span * .12f * MathF.Sin(tick + branch));
                BeamSegment(next, middle, new Color(tint, .65f), .012f, profile: 7);
                BeamSegment(middle, tip, new Color("effbff"), .006f, .002f, profile: 7);
            }
            previous = next;
        }
    }

    private void DrawArcaneChannel(BeamSnapshot beam, Vector3 from, Vector3 to, Vector3 direction,
        Vector3 side, Vector3 up, SpellVisualStrength strength, bool details)
    {
        var primary = Ui.ElementColor(Element.Arcane);
        var modifier = beam.Elements.FirstOrDefault(e => e != Element.Arcane, Element.Arcane);
        var accent = ElementChannelVisuals.IsSteam(beam.Elements) ? new Color("dce8ec")
            : modifier == Element.Cold ? new Color("a4eaff")
            : modifier == Element.Arcane ? primary : Ui.ElementColor(modifier);
        float thickness = MathF.Sqrt(Math.Clamp(beam.Power, SpellBalance.BeamMinimumDamage, 3));
        float radius = strength.BeamRadius * thickness;
        if (!details)
        {
            BeamSegment(from, to, new Color(primary, .18f), radius * 1.25f, profile: 6);
            BeamSegment(from, to, new Color(primary.Lightened(.15f), .65f), radius * .53f, profile: 6);
            BeamSegment(from, to, beam.Interaction == "merged" ? Ui.Text : new Color("f4dfff"),
                (.018f + strength.Charges * .004f) * thickness, profile: 6);
            return;
        }
        for (int strand = 0; strand < strength.Charges; strand++)
        {
            Vector3 previous = from;
            const int pieces = 8;
            for (int i = 1; i <= pieces; i++)
            {
                float t = i / (float)pieces;
                float angle = t * Mathf.Tau * 1.5f - (float)_time * 4 + strand * Mathf.Tau / strength.Charges;
                var offset = (side * MathF.Cos(angle) + up * MathF.Sin(angle)) * MathF.Sin(t * MathF.PI) * radius * .85f;
                if (modifier == Element.Lightning)
                    offset = side * MathF.Sin(MathF.Floor((float)_time * 14) * 3 + i * 7 + strand) * MathF.Sin(t * MathF.PI) * radius;
                var next = i == pieces ? to : from.Lerp(to, t) + offset;
                BeamSegment(previous, next, new Color(strand % 2 == 0 ? accent.Lightened(.2f) : primary, .64f),
                    .009f * thickness, profile: 7);
                previous = next;
            }
        }
        for (int i = 0; i < strength.Charges; i++)
        {
            float t = .08f + Mathf.PosMod((float)_time * .85f + i / (float)strength.Charges, 1) * .80f;
            var at = from.Lerp(to, t);
            float span = Math.Min(.22f, from.DistanceTo(to) * (1 - t));
            BeamSegment(at, at + direction * span, new Color(accent.Lightened(.4f), .72f),
                radius * .45f, radius * .12f, profile: 6);
        }
    }
}
