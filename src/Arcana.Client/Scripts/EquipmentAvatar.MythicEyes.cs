using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class EquipmentAvatar
{
    private readonly List<ColorRect> _mythicEyes = [];
    private ShaderMaterial? _mythicEyeMaterial;
    private ulong _mythicMix;
    internal bool MythicEyesVisible => _mythicEyes.Count == 2 && _mythicEyes.All(e => e.Visible);
    private void RefreshMythicEyes()
    {
        bool active = _halos.Count == MythicEyes.SlotCount && _halos.Values.All(i => i.Tier == ItemTier.Mythic);
        if (!active) { foreach (var eye in _mythicEyes) eye.Visible = false; return; }
        if (_mythicEyeMaterial == null)
        {
            _mythicEyeMaterial = MythicEyeVisuals.Material(true);
            for (int i = 0; i < 2; i++)
            {
                var eye = new ColorRect { Name = "MythicEye" + i, Color = Colors.White, Material = _mythicEyeMaterial,
                    MouseFilter = MouseFilterEnum.Ignore };
                AddChild(eye); _mythicEyes.Add(eye);
            }
        }
        ulong mix = _portraitAppearance?.MythicFamilies ?? 0;
        if (mix == 0)
            foreach (var item in _halos.Values) mix += 1UL << ((int)item.Family * 4);
        if (_mythicMix != mix) { _mythicMix = mix; MythicEyeVisuals.SetMix(_mythicEyeMaterial, mix); }
        foreach (var eye in _mythicEyes) eye.Visible = true;
        ArrangeMythicEyes();
    }
    private void ArrangeMythicEyes()
    {
        if (_mythicEyes.Count == 0 || _atlas == null) return;
        var bounds = SpriteBounds(); var size = bounds.Size / new Vector2(_atlas.Spec.Width / 4f, _atlas.Spec.Height / 4f);
        var anchors = _atlas.Spec.EyeAnchors![0];
        for (int i = 0; i < _mythicEyes.Count; i++)
        {
            _mythicEyes[i].Size = MythicEyeVisuals.Size * size;
            _mythicEyes[i].Position = bounds.Position + new Vector2(anchors[i].AnchorX, anchors[i].AnchorY) * size - _mythicEyes[i].Size * new Vector2(.5f, .82f);
        }
    }
}
