using Arcana.Core;
using Godot;

namespace Arcana.Client;

internal static class MythicEyeVisuals
{
    internal static readonly Vector2 Size = new(22, 32);
    private static readonly Dictionary<(WizardType, int), ArrayMesh> Meshes = [];
    private static readonly string[] Uniforms = Enumerable.Range(0, MythicEyes.FamilyCount).Select(i => "family_" + i).ToArray();
    private static Shader? _spatial, _canvas;
    internal static ShaderMaterial Material(bool canvas, float seed = 0)
    {
        Shader shader = canvas ? (_canvas ??= GD.Load<Shader>("res://Assets/Wizards/mythic-eyes-2d.gdshader"))
            : (_spatial ??= GD.Load<Shader>("res://Assets/Wizards/mythic-eyes-3d.gdshader"));
        var material = new ShaderMaterial { Shader = shader, RenderPriority = 2 };
        material.SetShaderParameter("seed", seed); return material;
    }
    internal static void SetMix(ShaderMaterial material, ulong mix)
    {
        for (int i = 0; i < MythicEyes.FamilyCount; i++)
        {
            var family = (EquipmentFamily)i;
            var element = RingEffects.ElementFor(family);
            var color = element.HasValue ? Ui.ElementColor(element.Value) : new Color("e8f4ff");
            if (family == EquipmentFamily.Cold) color = color.Lerp(Colors.White, .50f);
            material.SetShaderParameter(Uniforms[i], new Color(color, MythicEyes.Weight(mix, family)));
        }
    }
    internal static void Prepare(WizardSprites.Atlas atlas)
    {
        for (int frame = 0; frame < WizardSprites.Columns * WizardSprites.Rows; frame++)
            if (atlas.Spec.EyeAnchors![frame].Length > 0) Mesh(atlas, frame);
    }
    internal static ArrayMesh Mesh(WizardSprites.Atlas atlas, int frame)
    {
        var key = (atlas.Spec.Type, frame);
        if (Meshes.TryGetValue(key, out var cached)) return cached;
        using var surface = new SurfaceTool(); surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        float pixels = atlas.Spec.PixelSize; var feet = atlas.Spec.Frames[frame];
        var size = Size * pixels;
        void Vertex(Vector3 center, float x, float y, Vector2 uv)
        { surface.SetNormal(Vector3.Back); surface.SetUV(uv); surface.AddVertex(center + new Vector3(x, y, 0)); }
        foreach (var eye in atlas.Spec.EyeAnchors![frame])
        {
            var center = new Vector3((eye.AnchorX - feet.AnchorX) * pixels,
                (feet.AnchorY - eye.AnchorY) * pixels + size.Y * .32f, .018f);
            float x = size.X / 2, y = size.Y / 2;
            Vertex(center, -x, y, new(0, 0)); Vertex(center, -x, -y, new(0, 1)); Vertex(center, x, -y, new(1, 1));
            Vertex(center, -x, y, new(0, 0)); Vertex(center, x, -y, new(1, 1)); Vertex(center, x, y, new(1, 0));
        }
        cached = surface.Commit(); Meshes.Add(key, cached); return cached;
    }
    internal static void ClearCache()
    {
        foreach (var mesh in Meshes.Values) mesh.Dispose(); Meshes.Clear();
        _spatial?.Dispose(); _spatial = null; _canvas?.Dispose(); _canvas = null;
    }
}
