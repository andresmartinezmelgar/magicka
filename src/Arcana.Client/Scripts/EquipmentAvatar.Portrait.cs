using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private bool _portrait;
    private EquipmentAppearance? _portraitAppearance;
    internal WizardType DisplayedWizardType => _atlas.Spec.Type;

    /// <summary>Retrato sin controles; comparte sprite, aura, partículas y ojos con el inventario.</summary>
    internal void BuildPortrait(WizardType type, EquipmentAppearance? appearance)
    {
        _portrait = true;
        Name = "WaitingWizardPortrait";
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new(96, 116);
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        TextureFilter = TextureFilterEnum.Linear;
        VisibilityChanged += RefreshMoteProcessing;
        Resized += Arrange;
        UpdatePortrait(type, appearance);
    }

    internal void UpdatePortrait(WizardType type, EquipmentAppearance? appearance)
    {
        if (_atlas != null && _atlas.Spec.Type == type && _portraitAppearance == appearance) return;
        _atlas = WizardSprites.Load(type);
        _portraitAppearance = appearance;
        _halos.Clear();
        foreach (var slot in EquipmentAuraVisuals.Slots)
            if (appearance != null && EquipmentAuraVisuals.Tier(appearance, slot) is { } tier)
                _halos[slot] = (tier, 0, appearance.Family);
        RefreshHaloAnimation();
        Arrange();
    }
}
