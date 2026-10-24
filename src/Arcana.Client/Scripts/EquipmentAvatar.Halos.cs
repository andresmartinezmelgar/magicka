using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private readonly Dictionary<EquipmentSlot, (ItemTier Tier, int Level)> _halos = [];
    private GradientTexture2D? _haloTexture;
    private Gradient? _haloGradient;
    internal int EquippedHaloCount => _halos.Count;

    private void DrawEquipmentHalos(bool foreground)
    {
        if (_halos.Count == 0) return;
        if (_haloTexture == null)
        {
            // Un solo degradado compartido por las seis zonas; se redibuja únicamente al cambiar equipo o tamaño.
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
            void Halo(Vector2 center, Vector2 radii)
            {
                radii *= bounds.Size * spread;
                DrawTextureRect(_haloTexture, new Rect2(bounds.Position + center * bounds.Size - radii, radii * 2), false, color);
            }
            switch (slot)
            {
                case EquipmentSlot.Hat: Halo(new(.52f, .25f), new(.30f, .26f)); break;
                case EquipmentSlot.Chest: Halo(new(.52f, .67f), new(.24f, .23f)); break;
                case EquipmentSlot.Weapon: Halo(new(.28f, .56f), new(.105f, .40f)); break;
                case EquipmentSlot.Boots:
                    Halo(new(.43f, .86f), new(.13f, .115f)); Halo(new(.65f, .86f), new(.13f, .115f)); break;
                default:
                    Halo(slot == EquipmentSlot.GoldRing ? new(.32f, .62f) : new(.68f, .65f), new(.10f, .10f)); break;
            }
        }
    }

    public override void _ExitTree()
    {
        _haloTexture?.Dispose(); _haloTexture = null;
        _haloGradient?.Dispose(); _haloGradient = null;
    }
}
