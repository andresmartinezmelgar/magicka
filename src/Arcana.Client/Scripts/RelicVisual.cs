using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class RelicVisual : Node3D
{
    private MeshInstance3D _gem = null!;
    private Label3D _label = null!;
    internal bool Available => _gem.Visible;
    public static Color ColorFor(BuffKind kind) => kind switch
    {
        BuffKind.Power => new("ff7650"), BuffKind.Protection => Ui.Shield, BuffKind.Regeneration => Ui.Health,
        BuffKind.Vitality => new("ff5f95"), BuffKind.Speed => new("67ddff"), BuffKind.Energy => Ui.Energy, _ => new("b8ffc4")
    };
    public void Build(PickupSnapshot item)
    {
        Position = ArenaRenderer.World(item.Position); var color = ColorFor(item.Kind);
        var pad = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.65f, OuterRadius = 0.75f, Rings = 24, RingSegments = 6 },
            Position = new(0, 0.035f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(pad);
        _gem = new MeshInstance3D { Mesh = new PrismMesh { Size = new(0.65f, 0.85f, 0.65f) }, Position = new(0, 0.85f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 1.1f } };
        AddChild(_gem);
        _label = new Label3D { FontSize = 28, PixelSize = 0.011f, Position = new(0, 1.65f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = color, OutlineSize = 6, OutlineModulate = new("06101c") };
        AddChild(_label);
    }
    public void Update(PickupSnapshot item, float time)
    {
        Position = ArenaRenderer.World(item.Position);
        _gem.Visible = item.Active;
        _gem.Position = new(0, 0.85f + MathF.Sin(time * 2.2f + item.Id) * 0.13f, 0);
        _gem.Rotation = new(0, time * 1.4f, 0);
        _label.Text = RelicBuffs.Name(item.Kind) + (item.Active ? "" : $" · {Math.Ceiling(item.Respawn):0}s");
        _label.Modulate = item.Active ? ColorFor(item.Kind) : Ui.Muted;
    }
}
