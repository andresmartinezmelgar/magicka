using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private void UpdateEquipment(EquipmentAppearance? appearance)
    {
        if (_equipmentAppearance == appearance) return;
        _equipmentAppearance = appearance;
        _equipmentRoot?.QueueFree(); _equipmentRoot = null;
        if (appearance == null) return;
        _equipmentRoot = new Node3D { Name = "EquipmentTrim" }; _body.AddChild(_equipmentRoot);
        void Trim(ItemTier? tier, Vector3 size, Vector3 at)
        {
            if (tier == null) return;
            var material = new StandardMaterial3D { AlbedoColor = EquipmentPanel.TierColor(tier.Value),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = false };
            Mesh(_equipmentRoot, new BoxMesh { Size = size }, at, material);
        }
        Trim(appearance.Boots, new(0.13f, 0.07f, 0.035f), new(-0.15f, 0.19f, 0.04f));
        Trim(appearance.Boots, new(0.13f, 0.07f, 0.035f), new(0.15f, 0.19f, 0.04f));
        Trim(appearance.Hat, new(0.24f, 0.05f, 0.035f), new(0, 1.63f, 0.04f));
        Trim(appearance.Chest, new(0.075f, 0.18f, 0.035f), new(0, 0.87f, 0.04f));
        Trim(appearance.Weapon, new(0.09f, 0.09f, 0.035f), new(0.43f, 0.95f, 0.04f));
        if (appearance.Family != EquipmentFamily.Mixed)
        {
            var element = appearance.Family == EquipmentFamily.Life ? Element.Life : appearance.Family == EquipmentFamily.Shield ? Element.Shield : Element.Fire;
            Mesh(_equipmentRoot, new SphereMesh { Radius = 0.045f, Height = 0.09f }, new(0.09f, 0.94f, 0.065f),
                new StandardMaterial3D { AlbedoColor = Ui.ElementColor(element), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        }
    }
}
