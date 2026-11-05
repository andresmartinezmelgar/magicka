using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private MeshInstance2D? _mythicEquipment;
    private ShaderMaterial? _mythicEquipmentMaterial;
    internal int MythicEquipmentMask { get; private set; }

    private void RefreshMythicEquipment()
    {
        int mask = 0;
        foreach (var (slot, item) in _halos)
            if (item.Tier == ItemTier.Mythic) mask |= 1 << (int)slot;
        MythicEquipmentMask = mask;
        if (mask == 0) { if (_mythicEquipment != null) _mythicEquipment.Visible = false; return; }
        if (_mythicEquipment == null)
        {
            _mythicEquipmentMaterial = MythicEquipmentVisuals.Material(true);
            _mythicEquipment = new MeshInstance2D { Name = "MythicEquipment", Texture = MythicEquipmentVisuals.Texture,
                Material = _mythicEquipmentMaterial };
            AddChild(_mythicEquipment);
            // Encima del dibujo del mago, debajo de botones, indicadores y ojos.
            MoveChild(_mythicEquipment, 0);
        }
        _mythicEquipment.Mesh = MythicEquipmentVisuals.Mesh(_atlas.Spec.Type, _previewFrame);
        MythicEquipmentVisuals.Configure(_mythicEquipmentMaterial!, _atlas.Spec.Type, _previewFrame, mask);
        foreach (var (slot, item) in _halos) MythicEquipmentVisuals.SetFamily(_mythicEquipmentMaterial!, slot, item.Family);
        _mythicEquipment.Visible = true;
        ArrangeMythicEquipment();
    }

    private void ArrangeMythicEquipment()
    {
        if (_mythicEquipment == null || _atlas == null) return;
        var bounds = SpriteBounds();
        _mythicEquipment.Position = bounds.Position;
        _mythicEquipment.Scale = bounds.Size / new Vector2(_atlas.Spec.Width / 4f, _atlas.Spec.Height / 4f);
    }

    private void DisposeMythicEquipment()
    {
        if (_mythicEquipment != null) { _mythicEquipment.Mesh = null; _mythicEquipment.Texture = null; _mythicEquipment.Material = null; }
        _mythicEquipmentMaterial?.Dispose(); _mythicEquipmentMaterial = null;
    }
}
