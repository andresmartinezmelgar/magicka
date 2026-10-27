using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private readonly Dictionary<EquipmentSlot, (ItemTier Tier, int Level, EquipmentFamily Family)> _halos = [];
    private ColorRect? _aura;
    private ShaderMaterial? _auraMaterial;
    internal int EquippedHaloCount => _halos.Count;
    internal int MythicAuraPieces => _halos.Values.Count(i => i.Tier == ItemTier.Mythic);

    private void RefreshHaloAnimation()
    {
        // Una sola superficie mezcla el poder de las seis piezas. El shader anima
        // todo el contorno con el mismo pulso, sin círculos ni arcos por objeto.
        if (_aura == null)
        {
            _auraMaterial = EquipmentAuraVisuals.Material(true);
            _aura = new ColorRect { Name = "EquipmentMergedAura", Color = Colors.White,
                MouseFilter = MouseFilterEnum.Ignore, ShowBehindParent = true, Material = _auraMaterial };
            AddChild(_aura);
        }
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            if (_halos.TryGetValue(slot, out var item))
                EquipmentAuraVisuals.SetPiece(_auraMaterial!, slot, item.Tier, item.Level);
            else EquipmentAuraVisuals.SetPiece(_auraMaterial!, slot, null);
        }
        EquipmentAuraVisuals.SetMythicCount(_auraMaterial!, MythicAuraPieces);
        _aura.Visible = _halos.Count > 0;
        RefreshMythicEyes();
        RefreshAuraParticles();
        ArrangeAura();
    }

    private void ArrangeAura()
    {
        if (_aura == null || _atlas == null) return;
        var bounds = SpriteBounds();
        // Margen para que el resplandor se disuelva antes de los bordes.
        _aura.Position = bounds.Position - bounds.Size * .14f;
        _aura.Size = bounds.Size * 1.28f;
        ArrangeMythicEyes();
    }

    public override void _ExitTree()
    {
        DisposeAuraParticles();
        _auraMaterial?.Dispose(); _auraMaterial = null;
        _mythicEyeMaterial?.Dispose(); _mythicEyeMaterial = null;
    }
}
