using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private MeshInstance3D? _equipmentAura;
    private ShaderMaterial? _equipmentAuraMaterial;
    private EquipmentAppearance? _auraAppearance;
    private int _equipmentMythicCount;
    internal int MythicAuraPieces { get; private set; }
    internal bool EquipmentAuraVisible => _equipmentAura?.Visible == true && _body.Visible;

    private void UpdateEquipmentAura(WizardSnapshot player)
    {
        bool active = !_compactEnemy && player.Alive && EquipmentAuraVisuals.HasPieces(player.Equipment);
        if (!active)
        {
            MythicAuraPieces = 0;
            if (_equipmentAura != null) _equipmentAura.Visible = false;
            return;
        }
        if (_equipmentAura == null)
        {
            _equipmentAuraMaterial = EquipmentAuraVisuals.Material(false);
            _equipmentAura = new MeshInstance3D { Name = "EquipmentMergedAura", Mesh = EquipmentAuraVisuals.Quad,
                MaterialOverride = _equipmentAuraMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _body.AddChild(_equipmentAura);
        }
        if (_auraAppearance != player.Equipment)
        {
            _auraAppearance = player.Equipment;
            _equipmentMythicCount = 0;
            foreach (var slot in EquipmentAuraVisuals.Slots)
            {
                var tier = EquipmentAuraVisuals.Tier(_auraAppearance!, slot);
                EquipmentAuraVisuals.SetPiece(_equipmentAuraMaterial!, slot, tier);
                if (tier == ItemTier.Mythic) _equipmentMythicCount++;
            }
            EquipmentAuraVisuals.SetMythicCount(_equipmentAuraMaterial!, _equipmentMythicCount);
        }
        // Recupera el contador también al reaparecer sin cambiar de equipo.
        MythicAuraPieces = _equipmentMythicCount;
        float pixels = _atlas.Spec.PixelSize;
        var scale = new Vector3(_atlas.Spec.Width / 4f * pixels * 1.28f,
            _atlas.Spec.Height / 4f * pixels * 1.28f, 1);
        var position = new Vector3(_sprite.Offset.X * pixels, _sprite.Offset.Y * pixels, -.012f);
        if (_equipmentAura.Scale != scale) _equipmentAura.Scale = scale;
        if (_equipmentAura.Position != position) _equipmentAura.Position = position;
        _equipmentAura.Visible = true;
    }
}
