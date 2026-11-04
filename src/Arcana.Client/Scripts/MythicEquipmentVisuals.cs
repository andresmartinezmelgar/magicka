using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Piezas articuladas por pose. Una textura y 80 mallas compartidas para ambos renderizadores.</summary>
internal static class MythicEquipmentVisuals
{
    private const string IndexPath = "res://Assets/Wizards/mythic-equipment.json";
    private static readonly Dictionary<(WizardType, int), ArrayMesh> Meshes = [];
    private static AssetSpec? _spec;
    private static Texture2D? _texture;
    private static Shader? _canvas, _spatial;

    internal sealed record Region(int X, int Y, int Width, int Height);
    internal sealed record SpriteSpec(EquipmentSlot Slot, Region[] Views);
    internal sealed record Bone(float X, float Y, float Width, float Height, float Angle = 0);
    internal sealed record Pose(Bone Hat, Bone Chest, Bone LeftBoot, Bone RightBoot,
        Bone? Weapon, Bone GoldRing, Bone MagicRing, float[] Beard);
    internal sealed record Rig(WizardType Type, Pose[] Frames);
    internal sealed record AssetSpec(string Texture, int Width, int Height, SpriteSpec[] Sprites, Rig[] Rigs);

    private static AssetSpec Spec => _spec ??= JsonSerializer.Deserialize<AssetSpec>(Godot.FileAccess.GetFileAsString(IndexPath))
        ?? throw new InvalidDataException("Falta el índice del equipo mítico articulado.");
    internal static Texture2D Texture => _texture ??= GD.Load<Texture2D>(Spec.Texture)
        ?? throw new InvalidDataException("Falta el atlas del equipo mítico articulado.");
    internal static Pose Frame(WizardType type, int frame) => Spec.Rigs.Single(r => r.Type == type).Frames[frame];

    internal static int Mask(EquipmentAppearance? gear)
    {
        if (gear == null) return 0;
        int mask = 0;
        foreach (var slot in EquipmentAuraVisuals.Slots)
            if (EquipmentAuraVisuals.Tier(gear, slot) == ItemTier.Mythic) mask |= 1 << (int)slot;
        return mask;
    }

    internal static ShaderMaterial Material(bool canvas) => new()
    {
        Shader = canvas ? (_canvas ??= GD.Load<Shader>("res://Assets/Wizards/mythic-equipment-2d.gdshader"))
            : (_spatial ??= GD.Load<Shader>("res://Assets/Wizards/mythic-equipment-3d.gdshader")),
        RenderPriority = 1
    };

    internal static void Configure(ShaderMaterial material, WizardType type, int frame, int mask)
    {
        using var texture = Variant.From(Texture);
        material.SetShaderParameter("equipment_atlas", texture);
        material.SetShaderParameter("equipped_mask", mask);
        var beard = Frame(type, frame).Beard;
        material.SetShaderParameter("beard", new Vector4(beard[0], beard[1], beard[2], beard[3]));
    }

    internal static void Prepare(WizardSprites.Atlas atlas)
    {
        var spec = Spec;
        if (Texture.GetWidth() != spec.Width || Texture.GetHeight() != spec.Height ||
            spec.Sprites.Length != 6 || spec.Sprites.Select(s => s.Slot).Distinct().Count() != 6 ||
            spec.Sprites.Any(s => !Enum.IsDefined(s.Slot) || s.Views.Length != WizardSprites.Rows ||
                s.Views.Any(r => r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0 ||
                    r.X + r.Width > spec.Width || r.Y + r.Height > spec.Height)) ||
            spec.Rigs.Length != Enum.GetValues<WizardType>().Length ||
            spec.Rigs.Select(r => r.Type).Distinct().Count() != spec.Rigs.Length)
            throw new InvalidDataException("Atlas de equipo mítico inválido.");
        var rig = spec.Rigs.Single(r => r.Type == atlas.Spec.Type);
        if (rig.Frames.Length != WizardSprites.Columns * WizardSprites.Rows)
            throw new InvalidDataException("Faltan poses del equipo mítico.");
        foreach (var pose in rig.Frames)
        {
            if (pose.Beard.Length != 4 || pose.Beard.Any(v => !float.IsFinite(v)) || pose.Beard[2] < 0 || pose.Beard[3] < 0 ||
                Parts(pose).Any(p => !float.IsFinite(p.Bone.X) || !float.IsFinite(p.Bone.Y) ||
                    !float.IsFinite(p.Bone.Width) || !float.IsFinite(p.Bone.Height) || !float.IsFinite(p.Bone.Angle) ||
                    p.Bone.Width <= 0 || p.Bone.Height <= 0 || p.Bone.X < 0 || p.Bone.Y < 0 ||
                    p.Bone.X >= atlas.Spec.Width / 4f || p.Bone.Y >= atlas.Spec.Height / 4f))
                throw new InvalidDataException("Anclajes del equipo mítico inválidos.");
        }
        for (int frame = 0; frame < rig.Frames.Length; frame++) Mesh(atlas.Spec.Type, frame);
    }

    private static IEnumerable<(EquipmentSlot Slot, Bone Bone)> Parts(Pose pose)
    {
        yield return (EquipmentSlot.Chest, pose.Chest);
        yield return (EquipmentSlot.Boots, pose.LeftBoot);
        yield return (EquipmentSlot.Boots, pose.RightBoot);
        yield return (EquipmentSlot.Hat, pose.Hat);
        if (pose.Weapon != null) yield return (EquipmentSlot.Weapon, pose.Weapon);
        yield return (EquipmentSlot.GoldRing, pose.GoldRing);
        yield return (EquipmentSlot.MagicRing, pose.MagicRing);
    }

    internal static ArrayMesh Mesh(WizardType type, int frame)
    {
        var key = (type, frame);
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        int layer = 0;
        foreach (var (slot, bone) in Parts(Frame(type, frame)))
        {
            var region = Spec.Sprites.Single(s => s.Slot == slot).Views[frame / WizardSprites.Columns];
            var uvMin = new Vector2(region.X / (float)Spec.Width, region.Y / (float)Spec.Height);
            var uvSize = new Vector2(region.Width / (float)Spec.Width, region.Height / (float)Spec.Height);
            float z = .008f + layer++ * .0001f;
            void Vertex(float x, float y, Vector2 uv)
            {
                var point = new Vector2(x * bone.Width, y * bone.Height).Rotated(Mathf.DegToRad(bone.Angle)) + new Vector2(bone.X, bone.Y);
                surface.SetColor(new((int)slot / 8f, 0, 0, 1));
                surface.SetNormal(Vector3.Back); surface.SetUV(uvMin + uv * uvSize);
                surface.AddVertex(new(point.X, point.Y, z));
            }
            Vertex(-.5f, -.5f, new(0, 0)); Vertex(-.5f, .5f, new(0, 1)); Vertex(.5f, .5f, new(1, 1));
            Vertex(-.5f, -.5f, new(0, 0)); Vertex(.5f, .5f, new(1, 1)); Vertex(.5f, -.5f, new(1, 0));
        }
        cached = surface.Commit(); Meshes.Add(key, cached); return cached;
    }

    internal static void ClearCache()
    {
        foreach (var mesh in Meshes.Values) mesh.Dispose(); Meshes.Clear();
        _texture?.Dispose(); _texture = null; _spec = null;
        _canvas?.Dispose(); _canvas = null; _spatial?.Dispose(); _spatial = null;
    }
}
