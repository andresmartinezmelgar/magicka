using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual
{
    private MeshInstance3D? _mythicEyes;
    private ShaderMaterial? _mythicEyeMaterial;
    private ulong _mythicMix;
    private int _mythicFrame = -1;
    private WizardType? _mythicType;
    internal int MythicEyeCount { get; private set; }
    private void UpdateMythicEyes(WizardSnapshot player, int frame)
    {
        MythicEyeCount = player.Alive && MythicEyes.Active(player.Equipment) ? _atlas.Spec.EyeAnchors![frame].Length : 0;
        if (MythicEyeCount == 0) { if (_mythicEyes != null) _mythicEyes.Visible = false; return; }
        if (_mythicEyes == null)
        {
            _mythicEyeMaterial = MythicEyeVisuals.Material(false, player.Id * .37f);
            _mythicEyes = new MeshInstance3D { Name = "MythicEyes", MaterialOverride = _mythicEyeMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _body.AddChild(_mythicEyes);
        }
        ulong mix = player.Equipment!.MythicFamilies;
        if (_mythicMix != mix) { _mythicMix = mix; MythicEyeVisuals.SetMix(_mythicEyeMaterial!, mix); }
        if (_mythicFrame != frame || _mythicType != _atlas.Spec.Type)
        {
            _mythicFrame = frame; _mythicType = _atlas.Spec.Type;
            _mythicEyes.Mesh = MythicEyeVisuals.Mesh(_atlas, frame);
        }
        _mythicEyes.Visible = true;
    }
}
