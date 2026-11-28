using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Atlas compartidos. El anclaje de cada pose mantiene los pies en el suelo.</summary>
internal static class WizardSprites
{
    public const int Columns = WizardDirectionalAtlas.Columns, Rows = WizardDirectionalAtlas.Rows;
    public const int Poses = WizardDirectionalAtlas.Poses, Directions = WizardDirectionalAtlas.Directions;
    internal const int SourceColumns = 4, SourceRows = 4;
    private static readonly Dictionary<WizardType, Atlas> Cache = [];
    private static AtlasSpec[]? _specs;
    private static Shader? _shader;

    internal sealed record FrameSpec(float AnchorX, float AnchorY);
    internal sealed record AtlasSpec(WizardType Type, string Texture, int Width, int Height, float PixelSize,
        FrameSpec[] Frames, FrameSpec[]? CastOrigins = null, FrameSpec[][]? EyeAnchors = null);
    internal sealed record Atlas(Texture2D Texture, AtlasSpec Spec, AtlasSpec SourceSpec, Texture2D SourceTexture)
    {
        public Vector2 Offset(int frame) => new(Spec.Width / (Columns * 2f) - Spec.Frames[frame].AnchorX,
            Spec.Frames[frame].AnchorY - Spec.Height / (Rows * 2f));
    }

    public static Atlas Load(WizardType type)
    {
        if (Cache.TryGetValue(type, out var cached)) return cached;
        _specs ??= JsonSerializer.Deserialize<AtlasSpec[]>(Godot.FileAccess.GetFileAsString("res://Assets/Wizards/atlases.json"))
            ?? throw new InvalidDataException("Falta el índice de sprites de magos.");
        var spec = _specs.Single(s => s.Type == type);
        ValidateSource(spec);
        Image pixels;
        Texture2D original;
        FrameSpec[]? staff = null;
        if (type == WizardType.Balanced)
        {
            var parts = WizardOutfitParts.Load();
            spec = spec with { CastOrigins = parts.CastOrigins(default) };
            staff = parts.StaffAnchors(default);
            pixels = parts.Assemble(spec, default);
            original = ImageTexture.CreateFromImage(pixels);
        }
        else
        {
            var source = ResourceLoader.Load<Texture2D>(spec.Texture)
                ?? throw new InvalidDataException($"No se pudo cargar {spec.Texture}.");
            pixels = source.GetImage() ?? throw new InvalidDataException($"No se pudo leer {spec.Texture}.");
            original = source;
        }
        try
        {
            using var art = WizardDirectionalAtlas.Bake(pixels, spec, spec.CastOrigins!, staff, out var rendered);
            cached = new(ImageTexture.CreateFromImage(art), rendered, spec, original); Cache.Add(type, cached);
        }
        catch { if (type == WizardType.Balanced) original.Dispose(); throw; }
        finally { pixels.Dispose(); }
        MythicEyeVisuals.Prepare(cached);
        Diagnostics.Info("sprites.loaded", "Atlas direccional de mago cargado.", new Dictionary<string, object?>
            { ["wizardType"] = type, ["width"] = cached.Spec.Width, ["height"] = cached.Spec.Height,
                ["frames"] = cached.Spec.Frames.Length, ["directions"] = Directions });
        return cached;
    }

    private static void ValidateSource(AtlasSpec spec)
    {
        if (spec.Width <= 0 || spec.Height <= 0 || spec.Frames.Length != SourceColumns * SourceRows ||
            !float.IsFinite(spec.PixelSize) || spec.PixelSize <= 0 ||
            spec.Frames.Any(p => p == null || !float.IsFinite(p.AnchorX) || !float.IsFinite(p.AnchorY) ||
                p.AnchorX < 0 || p.AnchorY < 0 || p.AnchorX > spec.Width / (float)SourceColumns || p.AnchorY > spec.Height / (float)SourceRows) ||
            spec.CastOrigins is not { Length: SourceRows } origins || origins.Any(p => p == null ||
                !float.IsFinite(p.AnchorX) || !float.IsFinite(p.AnchorY) || p.AnchorX < 0 || p.AnchorY < 0 ||
                p.AnchorX >= spec.Width / (float)SourceColumns || p.AnchorY >= spec.Height / (float)SourceRows))
            throw new InvalidDataException($"Atlas de mago inválido: {spec.Type}.");
        if (spec.EyeAnchors is not { Length: SourceColumns * SourceRows } eyes ||
            eyes.Where((p, i) => p == null || p.Length != (i / SourceColumns == 2 ? 0 : i / SourceColumns == 0 ? 2 : 1)).Any() ||
            eyes.Any(p => p.Any(e => e == null || !float.IsFinite(e.AnchorX) || !float.IsFinite(e.AnchorY) ||
                e.AnchorX < 0 || e.AnchorY < 0 || e.AnchorX >= spec.Width / (float)SourceColumns || e.AnchorY >= spec.Height / (float)SourceRows)))
            throw new InvalidDataException($"Anclajes de ojos inválidos: {spec.Type}.");
    }

    public static ShaderMaterial Material(Texture2D texture, int team)
    {
        _shader ??= ResourceLoader.Load<Shader>("res://Assets/Wizards/team-cloth.gdshader")
            ?? throw new InvalidDataException("Falta el shader de colores de equipo.");
        var material = new ShaderMaterial { Shader = _shader };
        SetTexture(material, texture);
        material.SetShaderParameter("team_color", team == 0 ? Ui.Blue : Ui.Red);
        return material;
    }

    public static void SetTexture(ShaderMaterial material, Texture2D texture)
    {
        using var value = Variant.From(texture);
        material.SetShaderParameter("atlas", value);
    }

    public static void ClearCache()
    {
        foreach (var atlas in Cache.Values) { atlas.Texture.Dispose(); atlas.SourceTexture.Dispose(); }
        Cache.Clear();
        WizardOutfitParts.ClearCache();
        _shader?.Dispose(); _shader = null;
    }

}
