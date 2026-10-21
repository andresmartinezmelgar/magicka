using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Imagen del modelo real, renderizada una sola vez.</summary>
public partial class BestiaryPortrait : SubViewportContainer
{
    private ShaderMaterial? _cloth;
    public void Build(MinionKind kind)
    {
        Name = "BestiaryPortrait_" + kind;
        CustomMinimumSize = new(Ui.IsMobile ? 88 : 112, Ui.IsMobile ? 100 : 124);
        Stretch = true; MouseFilter = MouseFilterEnum.Ignore;
        TooltipText = DefenseRules.MinionName(kind);
        var viewport = new SubViewport { Name = "PortraitViewport", Size = new(160, 160),
            OwnWorld3D = true, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        AddChild(viewport);
        var root = new Node3D(); viewport.AddChild(root);
        root.AddChild(new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("10263f"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.7f
        } });
        root.AddChild(new DirectionalLight3D { RotationDegrees = new(-45, -30, 0), LightEnergy = 1.2f });
        bool caster = MinionArt.CasterType(kind) != null;
        float height = caster ? 1.3f : MinionArt.BodyHeight(kind);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal,
            Size = kind == MinionKind.Boss ? 5.3f : kind == MinionKind.Oracle ? 4.1f : caster ? 3.5f : 3.2f, Current = true,
            Position = new(4, height + 2.3f, 5) };
        root.AddChild(camera); camera.LookAt(new(0, height, 0));
        if (MinionArt.CasterType(kind) is { } type)
        {
            var atlas = WizardSprites.Load(type); _cloth = WizardSprites.Material(atlas, 1);
            root.AddChild(new Sprite3D { Name = "PortraitModel", Texture = atlas.Texture,
                Hframes = WizardSprites.Columns, Vframes = WizardSprites.Rows, Frame = 0,
                PixelSize = atlas.Spec.PixelSize, Offset = atlas.Offset(0), Basis = camera.Basis,
                MaterialOverride = _cloth, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        else root.AddChild(new MeshInstance3D { Name = "PortraitModel", Mesh = MinionArt.Shape(kind), Position = new(0, height, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = MinionArt.ColorFor(kind), Roughness = 0.8f,
                ShadingMode = kind is MinionKind.Prism or MinionKind.Oracle ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel } });
        if (!caster) MinionArt.Decorate(root, kind, kind == MinionKind.Prism ? Element.Fire : null,
            color => new StandardMaterial3D { AlbedoColor = kind == MinionKind.Prism ? color.Darkened(0.18f) : color, Roughness = 0.8f,
                ShadingMode = kind == MinionKind.Prism ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel });
        if (kind == MinionKind.Prism) TooltipText += " · ejemplo vulnerable a Fuego; su color indica el elemento que acepta.";
        if (kind is MinionKind.Captain or MinionKind.Boss or MinionKind.Sentinel or MinionKind.Warlock or MinionKind.Cryomancer)
            root.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.88f, OuterRadius = 0.97f, Rings = 20, RingSegments = 6 },
                Position = new(0, 0.06f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = MinionArt.ColorFor(kind),
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
    }
    public override void _ExitTree() { _cloth?.Dispose(); _cloth = null; }
}
