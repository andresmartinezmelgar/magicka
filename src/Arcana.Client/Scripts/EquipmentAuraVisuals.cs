using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal static class EquipmentAuraVisuals
{
    private static Shader? _canvas, _spatial;
    private static QuadMesh? _quad;
    internal static readonly EquipmentSlot[] Slots = Enum.GetValues<EquipmentSlot>();
    private static readonly string[] Pieces = Enumerable.Range(0, 6).Select(i => "piece_" + i).ToArray();
    internal static QuadMesh Quad => _quad ??= new QuadMesh { Size = Vector2.One };
    internal static ShaderMaterial Material(bool canvas) => new()
    {
        Shader = canvas ? (_canvas ??= GD.Load<Shader>("res://Assets/Wizards/equipment-aura.gdshader"))
            : (_spatial ??= GD.Load<Shader>("res://Assets/Wizards/equipment-aura-3d.gdshader")),
        RenderPriority = -1
    };
    internal static ItemTier? Tier(EquipmentAppearance gear, EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Boots => gear.Boots, EquipmentSlot.Hat => gear.Hat, EquipmentSlot.Chest => gear.Chest,
        EquipmentSlot.Weapon => gear.Weapon, EquipmentSlot.GoldRing => gear.GoldRing, _ => gear.MagicRing
    };
    internal static bool HasPieces(EquipmentAppearance? gear) => gear != null &&
        (gear.Boots.HasValue || gear.Hat.HasValue || gear.Chest.HasValue || gear.Weapon.HasValue ||
            gear.GoldRing.HasValue || gear.MagicRing.HasValue);
    internal static void SetPiece(ShaderMaterial material, EquipmentSlot slot, ItemTier? tier, int level = 0)
    {
        var tint = tier.HasValue ? new Color(EquipmentPanel.TierColor(tier.Value),
            .38f + .14f * (int)tier.Value + .06f * Math.Clamp(level, 0, 10) / 10) : Colors.Transparent;
        material.SetShaderParameter(Pieces[(int)slot], tint);
    }
    internal static void SetMythicCount(ShaderMaterial material, int count) =>
        material.SetShaderParameter("mythic_power", count == 0 ? 0f : .6f + .4f * count / 6);
    internal static void ClearCache()
    {
        _canvas?.Dispose(); _canvas = null; _spatial?.Dispose(); _spatial = null;
        _quad?.Dispose(); _quad = null;
    }
}
