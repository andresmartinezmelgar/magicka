using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Cristales verticales y marcas rúnicas sincronizados con la explosión; sin cola de meteorito.</summary>
public partial class OracleStrikeVisual : Node3D
{
    private Node3D _crystal = null!;
    private EnemyAttackWarning _warning = null!;
    private static readonly StandardMaterial3D Skin = new()
    { AlbedoColor = new("c7b0ff"), EmissionEnabled = true, Emission = new("734fc1"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
    public void Build(FieldSnapshot field)
    {
        Position = ArenaRenderer.World(field.Position);
        MeshInstance3D Piece(Node3D parent, Godot.Mesh mesh, Vector3 position, Material material)
        {
            var part = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            parent.AddChild(part); return part;
        }
        _warning = new EnemyAttackWarning(); AddChild(_warning); _warning.Build(field.Radius);
        _crystal = new Node3D(); AddChild(_crystal);
        Piece(_crystal, new CylinderMesh { TopRadius = 0, BottomRadius = 0.42f, Height = 0.8f, RadialSegments = 5 }, new(0, 0.3f, 0), Skin);
        Piece(_crystal, new CylinderMesh { TopRadius = 0.42f, BottomRadius = 0, Height = 1.2f, RadialSegments = 5 }, new(0, -0.7f, 0), Skin);
        Update(field, 0);
    }
    public void Update(FieldSnapshot field, float time)
    {
        float remaining = Math.Clamp((field.Lifetime - 0.2f) / 2, 0, 1);
        _crystal.Position = new(0, 1.3f + remaining * remaining * 10, 0);
        _crystal.Rotation = new(0, time * 2, 0);
        _warning.ShowRemaining(field.Lifetime - 0.2f, 2);
    }
}
