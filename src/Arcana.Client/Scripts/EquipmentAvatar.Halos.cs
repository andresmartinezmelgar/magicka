using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private readonly Dictionary<EquipmentSlot, (ItemTier Tier, int Level)> _halos = [];
    private GradientTexture2D? _haloTexture;
    private Gradient? _haloGradient;
    private readonly Vector2[] _electricArc = new Vector2[9];
    private readonly Vector2[] _electricBranch = new Vector2[4];
    private double _electricClock, _electricRedraw;
    internal int EquippedHaloCount => _halos.Count;

    private void RefreshHaloAnimation() => SetProcess(_halos.Count > 0 && IsVisibleInTree());

    public override void _Process(double delta)
    {
        // Solo el avatar visible anima sus piezas. La geometría y el degradado se reutilizan.
        _electricClock = (_electricClock + delta) % 3600;
        _electricRedraw += delta;
        if (_electricRedraw < 1.0 / 24) return;
        _electricRedraw %= 1.0 / 24;
        QueueRedraw();
    }

    private void DrawEquipmentHalos(bool foreground)
    {
        if (_halos.Count == 0) return;
        if (_haloTexture == null)
        {
            // Un solo degradado compartido por las seis zonas, también durante la animación.
            _haloGradient = new Gradient
            {
                Offsets = [0, .36f, .58f, .72f, .86f, 1],
                Colors = [new(1, 1, 1, 0), new(1, 1, 1, .03f), new(1, 1, 1, .22f),
                    new(1, 1, 1, .30f), new(1, 1, 1, .09f), new(1, 1, 1, 0)]
            };
            _haloTexture = new GradientTexture2D { Gradient = _haloGradient, Width = 128, Height = 128,
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new(.5f, .5f), FillTo = new(1, .5f) };
        }
        var bounds = SpriteBounds();
        foreach (var (slot, item) in _halos)
        {
            float tier = (int)item.Tier;
            float strength = .38f + .14f * tier + .06f * item.Level / 10;
            float spread = 1 + .035f * tier + .025f * item.Level / 10;
            var color = new Color(EquipmentPanel.TierColor(item.Tier), strength * (foreground ? .65f : 1));
            void Halo(Vector2 center, Vector2 radii, int zone = 0)
            {
                radii *= bounds.Size * spread;
                center = bounds.Position + center * bounds.Size;
                DrawTextureRect(_haloTexture, new Rect2(center - radii, radii * 2), false, color);
                if (foreground) DrawHaloElectricity(center, radii, item.Tier, item.Level, (int)slot * 3 + zone);
            }
            switch (slot)
            {
                case EquipmentSlot.Hat: Halo(new(.52f, .25f), new(.30f, .26f)); break;
                case EquipmentSlot.Chest: Halo(new(.52f, .67f), new(.24f, .23f)); break;
                case EquipmentSlot.Weapon: Halo(new(.28f, .56f), new(.105f, .40f)); break;
                case EquipmentSlot.Boots:
                    Halo(new(.43f, .86f), new(.13f, .115f)); Halo(new(.65f, .86f), new(.13f, .115f), 1); break;
                default:
                    Halo(slot == EquipmentSlot.GoldRing ? new(.32f, .62f) : new(.68f, .65f), new(.10f, .10f)); break;
            }
        }
    }

    private void DrawHaloElectricity(Vector2 center, Vector2 radii, ItemTier quality, int level, int seed)
    {
        int tier = (int)quality;
        int count = 1 + (tier + 1) / 2;
        float upgrade = level / 10f, time = (float)_electricClock;
        int pattern = (int)(_electricClock * 12);
        float scale = Math.Clamp(SpriteBounds().Size.Y / 280, .8f, 1.3f);
        float width = scale * (1 + .08f * tier + .12f * upgrade);
        var tint = EquipmentPanel.TierColor(quality);
        for (int arc = 0; arc < count; arc++)
        {
            float offset = seed * 1.71f + arc * Mathf.Tau / count;
            float angle = offset + time * (.42f + .035f * tier);
            float length = .48f + .09f * tier + .12f * upgrade;
            float pulse = .86f + .14f * Mathf.Sin(time * 2.3f + offset);
            for (int i = 0; i < _electricArc.Length; i++)
            {
                float along = i / (float)(_electricArc.Length - 1);
                float at = angle + along * length;
                var normal = new Vector2(Mathf.Cos(at), Mathf.Sin(at));
                float jitter = Mathf.Sin(i * 97.13f + pattern * 11.39f + offset * 17.1f) *
                    Mathf.Sin(along * Mathf.Pi) * scale * (1.35f + .2f * tier + .5f * upgrade);
                _electricArc[i] = center + normal * radii + normal * jitter;
            }
            var glow = new Color(tint, (.14f + .035f * tier + .04f * upgrade) * pulse);
            var core = new Color(tint.Lerp(Colors.White, .60f), (.38f + .09f * tier + .05f * upgrade) * pulse);
            DrawPolyline(_electricArc, glow, width * 3.4f, true);
            DrawPolyline(_electricArc, core, width, true);
            if (tier < (int)ItemTier.Platinum || arc != 0) continue;
            var outward = new Vector2(Mathf.Cos(angle + length * .5f), Mathf.Sin(angle + length * .5f));
            var tangent = new Vector2(-outward.Y, outward.X);
            float reach = scale * (4 + tier + 3 * upgrade);
            _electricBranch[0] = _electricArc[4];
            _electricBranch[1] = _electricBranch[0] + outward * reach * .35f + tangent * scale * 2;
            _electricBranch[2] = _electricBranch[0] + outward * reach * .7f - tangent * scale;
            _electricBranch[3] = _electricBranch[0] + outward * reach;
            DrawPolyline(_electricBranch, glow, width * 2.6f, true);
            DrawPolyline(_electricBranch, new Color(core, core.A * .75f), width * .8f, true);
        }
    }

    public override void _ExitTree()
    {
        VisibilityChanged -= RefreshHaloAnimation;
        _haloTexture?.Dispose(); _haloTexture = null;
        _haloGradient?.Dispose(); _haloGradient = null;
    }
}
