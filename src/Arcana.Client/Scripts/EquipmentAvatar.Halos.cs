using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private readonly Dictionary<EquipmentSlot, (ItemTier Tier, int Level)> _halos = [];
    private ColorRect? _aura;
    private ShaderMaterial? _auraMaterial;
    internal int EquippedHaloCount => _halos.Count;

    private void RefreshHaloAnimation()
    {
        // Una sola superficie mezcla el poder de las seis piezas. El shader anima
        // todo el contorno con el mismo pulso, sin círculos ni arcos por objeto.
        if (_aura == null)
        {
            _auraMaterial = new ShaderMaterial
                { Shader = GD.Load<Shader>("res://Assets/Wizards/equipment-aura.gdshader") };
            _aura = new ColorRect { Name = "EquipmentMergedAura", Color = Colors.White,
                MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Material = _auraMaterial };
            AddChild(_aura);
        }
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var tint = Colors.Transparent;
            if (_halos.TryGetValue(slot, out var item))
                tint = new Color(EquipmentPanel.TierColor(item.Tier), .38f + .14f * (int)item.Tier + .06f * item.Level / 10);
            _auraMaterial!.SetShaderParameter("piece_" + (int)slot, tint);
        }
        _aura.Visible = _halos.Count > 0;
        ArrangeAura();
    }

    private void ArrangeAura()
    {
        if (_aura == null || _atlas == null) return;
        var bounds = SpriteBounds();
        // Margen para que el resplandor se disuelva antes de los bordes.
        _aura.Position = bounds.Position - bounds.Size * .14f;
        _aura.Size = bounds.Size * 1.28f;
    }

    public override void _ExitTree()
    {
        _auraMaterial?.Dispose(); _auraMaterial = null;
    }
}
