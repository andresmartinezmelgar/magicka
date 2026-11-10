using Godot;

namespace Arcana.Client;

/// <summary>La línea fija marca el alcance y el anillo interior el tiempo hasta el golpe.</summary>
internal partial class EnemyAttackWarning : Node3D
{
    private static readonly TorusMesh Outline = new() { InnerRadius = 0.91f, OuterRadius = 1, Rings = 36, RingSegments = 4 };
    private static readonly TorusMesh Line = new() { InnerRadius = 0.94f, OuterRadius = 0.98f, Rings = 36, RingSegments = 4 };
    private static readonly StandardMaterial3D Dark = Surface(new("29111c"));
    private static readonly StandardMaterial3D Danger = Surface(new("ff613d"));
    private static readonly StandardMaterial3D Timer = Surface(new("ffe0a1"));
    private MeshInstance3D _progress = null!;

    private static StandardMaterial3D Surface(Color color) => new()
    {
        AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = 8
    };

    internal void Build(float radius)
    {
        Name = "EnemyAttackWarning";
        Scale = new(radius, 1, radius);
        AddRing(Outline, Dark, 0.11f);
        AddRing(Line, Danger, 0.13f);
        _progress = AddRing(Line, Timer, 0.15f);
    }

    private MeshInstance3D AddRing(Mesh mesh, Material material, float height)
    {
        var ring = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = material, Position = new(0, height, 0), Scale = new(1, 0.35f, 1),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(ring);
        return ring;
    }

    internal void ShowRemaining(float seconds, float duration)
    {
        float progress = 1 - Math.Clamp(seconds / duration, 0, 1);
        float radius = 0.23f + progress * 0.73f;
        _progress.Scale = new(radius, 0.35f, radius);
    }
}
