using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private MeshInstance3D? _mythicEquipment;
    private ShaderMaterial? _mythicEquipmentMaterial;
    private WizardType? _mythicEquipmentType;
    private int _mythicEquipmentFrame = -1, _mythicEquipmentMask = -1;
    private EquipmentAppearance? _mythicEquipmentAppearance;
    internal int MythicEquipmentMask { get; private set; }
    internal bool MythicEquipmentVisible => _body.Visible && _mythicEquipment?.Visible == true;
    private bool _equipmentLaboratoryView;
    internal void UseEquipmentLaboratoryView() => _equipmentLaboratoryView = true;

    private void UpdateMythicEquipment(WizardSnapshot player, int frame, float flash)
    {
        int mask = !_compactEnemy && player.Alive ? MythicEquipmentVisuals.Mask(player.Equipment) : 0;
        MythicEquipmentMask = mask;
        if (mask == 0) { if (_mythicEquipment != null) _mythicEquipment.Visible = false; return; }
        if (_mythicEquipment == null)
        {
            _mythicEquipmentMaterial = MythicEquipmentVisuals.Material(false);
            _mythicEquipment = new MeshInstance3D { Name = "MythicEquipment", MaterialOverride = _mythicEquipmentMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _body.AddChild(_mythicEquipment);
        }
        if (_mythicEquipmentFrame != frame || _mythicEquipmentType != _atlas.Spec.Type || _mythicEquipmentMask != mask)
        {
            _mythicEquipmentFrame = frame; _mythicEquipmentType = _atlas.Spec.Type; _mythicEquipmentMask = mask;
            _mythicEquipment.Mesh = MythicEquipmentVisuals.Mesh(_atlas.Spec.Type, frame);
            MythicEquipmentVisuals.Configure(_mythicEquipmentMaterial!, _atlas.Spec.Type, frame, mask);
            var feet = _atlas.Spec.Frames[frame]; float pixels = _atlas.Spec.PixelSize;
            _mythicEquipment.Position = new(-feet.AnchorX * pixels, feet.AnchorY * pixels, 0);
            _mythicEquipment.Scale = new(pixels, -pixels, 1);
        }
        if (_mythicEquipmentAppearance != player.Equipment)
        {
            _mythicEquipmentAppearance = player.Equipment;
            foreach (var slot in EquipmentAuraVisuals.Slots)
                MythicEquipmentVisuals.SetFamily(_mythicEquipmentMaterial!, slot, player.Equipment!.FamilyFor(slot));
        }
        _mythicEquipmentMaterial!.SetShaderParameter("hit_flash", flash);
        _mythicEquipment.Visible = true;
    }

    private void DisposeMythicEquipment()
    {
        if (_mythicEquipment != null) { _mythicEquipment.Mesh = null; _mythicEquipment.MaterialOverride = null; }
        _mythicEquipmentMaterial?.Dispose(); _mythicEquipmentMaterial = null;
    }
}
