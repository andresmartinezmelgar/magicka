using Godot;

namespace Arcana.Client;

/// <summary>La línea fija marca el alcance y el anillo interior el tiempo hasta el golpe.</summary>
internal partial class EnemyAttackWarning : Node3D
{
    private static readonly PlaneMesh Surface = new() { Size = Vector2.One * 2 };
    private ShaderMaterial _skin = null!;

    internal void Build(float radius)
    {
        Name = "EnemyAttackWarning";
        Scale = new(radius, 1, radius);
        _skin = AreaVisuals.WarningSkin();
        AddChild(new MeshInstance3D
        {
            Mesh = Surface, MaterialOverride = _skin, Position = new(0, 0.085f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    internal void ShowRemaining(float seconds, float duration)
    {
        float progress = 1 - Math.Clamp(seconds / Math.Max(duration, 0.001f), 0, 1);
        _skin.SetShaderParameter("progress", progress);
    }
}
