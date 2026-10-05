using System.Text.Json;
using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Atlas compartidos. El anclaje de cada pose mantiene los pies en el suelo.</summary>
internal static class WizardSprites
{
    public const int Columns = 4, Rows = 4;
    private static readonly Dictionary<WizardType, Atlas> Cache = [];
    private static AtlasSpec[]? _specs;
    private static Shader? _shader;

    internal sealed record FrameSpec(float AnchorX, float AnchorY);
    internal sealed record AtlasSpec(WizardType Type, string Texture, int Width, int Height, float PixelSize, FrameSpec[] Frames);
    internal sealed record Atlas(Texture2D Texture, AtlasSpec Spec)
    {
        public Vector2 Offset(int frame) => new(Spec.Width / 8f - Spec.Frames[frame].AnchorX,
            Spec.Frames[frame].AnchorY - Spec.Height / 8f);
    }

    public static Atlas Load(WizardType type)
    {
        if (Cache.TryGetValue(type, out var cached)) return cached;
        _specs ??= JsonSerializer.Deserialize<AtlasSpec[]>(Godot.FileAccess.GetFileAsString("res://Assets/Wizards/atlases.json"))
            ?? throw new InvalidDataException("Falta el índice de sprites de magos.");
        var spec = _specs.Single(s => s.Type == type);
        var texture = ResourceLoader.Load<Texture2D>(spec.Texture)
            ?? throw new InvalidDataException($"No se pudo cargar {spec.Texture}.");
        if (texture.GetWidth() != spec.Width || texture.GetHeight() != spec.Height || spec.Frames.Length != Columns * Rows || spec.PixelSize <= 0)
            throw new InvalidDataException($"Atlas de mago inválido: {type}.");
        cached = new(texture, spec); Cache.Add(type, cached);
        Diagnostics.Info("sprites.loaded", "Atlas de mago cargado.", new Dictionary<string, object?>
            { ["wizardType"] = type, ["width"] = spec.Width, ["height"] = spec.Height, ["frames"] = spec.Frames.Length });
        return cached;
    }

    public static ShaderMaterial Material(Atlas atlas, int team)
    {
        _shader ??= ResourceLoader.Load<Shader>("res://Assets/Wizards/team-cloth.gdshader")
            ?? throw new InvalidDataException("Falta el shader de colores de equipo.");
        var material = new ShaderMaterial { Shader = _shader };
        SetAtlas(material, atlas);
        material.SetShaderParameter("team_color", team == 0 ? Ui.Blue : Ui.Red);
        return material;
    }

    public static void SetAtlas(ShaderMaterial material, Atlas atlas)
    {
        using var value = Variant.From(atlas.Texture);
        material.SetShaderParameter("atlas", value);
    }

    public static void ClearCache()
    {
        foreach (var atlas in Cache.Values) atlas.Texture.Dispose();
        Cache.Clear();
        _shader?.Dispose(); _shader = null;
    }

    // Histeresis junto a las diagonales: no alternar de fila por ruido de red/apuntado.
    public static int FacingRow(V2 facing, int previous)
    {
        float x = Math.Abs(facing.X), y = Math.Abs(facing.Y);
        if (x + y < 0.001f || Math.Abs(x - y) < 0.10f * Math.Max(x, y)) return previous;
        return x > y ? facing.X < 0 ? 1 : 3 : facing.Y < 0 ? 2 : 0;
    }
}
