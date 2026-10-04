using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class WizardVisual : Node3D
{
    private Node3D _body = null!;
    private MeshInstance3D _shield = null!, _halo = null!, _ghost = null!;
    private Label3D _name = null!;
    private Node3D _healthRoot = null!;
    private MeshInstance3D _healthFill = null!;
    private MeshInstance3D _shieldFill = null!, _shieldTrack = null!;
    private Label3D _shieldAmount = null!;
    private Color _color;
    public void Build(WizardSnapshot p, bool local)
    {
        _color = p.Team == 0 ? Ui.Blue : Ui.Red;
        Position = ArenaRenderer.World(p.Position);
        _body = new Node3D(); AddChild(_body);
        var cloth = new StandardMaterial3D { AlbedoColor = _color.Darkened(0.18f), Roughness = 1 };
        var dark = new StandardMaterial3D { AlbedoColor = _color.Darkened(0.6f), Roughness = 1 };
        var skin = new StandardMaterial3D { AlbedoColor = new("e0b68f"), Roughness = 1 };
        Mesh(_body, new CylinderMesh { TopRadius = 0.23f, BottomRadius = 0.46f, Height = 1.08f, RadialSegments = 12 }, new(0, 0.75f, 0), cloth);
        Mesh(_body, new CylinderMesh { TopRadius = 0.27f, BottomRadius = 0.27f, Height = 0.1f, RadialSegments = 12 }, new(0, 0.93f, 0), dark);
        Mesh(_body, new SphereMesh { Radius = 0.23f, Height = 0.46f }, new(0, 1.4f, 0), skin);
        Mesh(_body, new BoxMesh { Size = new(0.31f, 0.1f, 0.04f) }, new(0, 1.43f, 0.22f), dark);
        Mesh(_body, new CylinderMesh { TopRadius = 0.49f, BottomRadius = 0.49f, Height = 0.06f, RadialSegments = 16 }, new(0, 1.63f, 0), cloth);
        var hat = Mesh(_body, new CylinderMesh { TopRadius = 0, BottomRadius = 0.38f, Height = 0.72f, RadialSegments = 12 }, new(0, 2, 0), cloth);
        hat.RotationDegrees = new(-8, 0, -9);
        Mesh(_body, new BoxMesh { Size = new(0.19f, 0.17f, 0.35f) }, new(-0.18f, 0.13f, 0.15f), dark);
        Mesh(_body, new BoxMesh { Size = new(0.19f, 0.17f, 0.35f) }, new(0.18f, 0.13f, 0.15f), dark);
        var arm = Mesh(_body, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = 0.6f }, new(0.38f, 1.12f, 0.13f), cloth);
        arm.RotationDegrees = new(22, 0, -26);
        var wood = new StandardMaterial3D { AlbedoColor = new("624a35"), Roughness = 1 };
        Mesh(_body, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 1.65f }, new(0.57f, 0.99f, 0.25f), wood);
        var glow = new StandardMaterial3D { AlbedoColor = Ui.Gold, EmissionEnabled = true, Emission = Ui.Gold, EmissionEnergyMultiplier = 0.9f };
        Mesh(_body, new SphereMesh { Radius = 0.105f, Height = 0.21f }, new(0.57f, 1.88f, 0.25f), glow);
        _halo = Mesh(this, new TorusMesh { InnerRadius = local ? 0.63f : 0.56f, OuterRadius = local ? 0.72f : 0.6f, Rings = 32, RingSegments = 8 }, new(0, 0.05f, 0),
            new StandardMaterial3D { AlbedoColor = _color, EmissionEnabled = true, Emission = _color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        var shieldMat = new StandardMaterial3D { AlbedoColor = new Color(Ui.Shield.R, Ui.Shield.G, Ui.Shield.B, 0.22f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true, Emission = Ui.Shield, EmissionEnergyMultiplier = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _shield = Mesh(this, new SphereMesh { Radius = 0.9f, Height = 2.4f }, new(0, 1.1f, 0), shieldMat);
        _ghost = Mesh(this, new SphereMesh { Radius = 0.4f, Height = 1.5f }, new(0, 0.7f, 0),
            new StandardMaterial3D { AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.14f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha });
        _name = new Label3D { Text = p.Name, Position = new(0, 3.95f, 0), FontSize = 34, PixelSize = 0.013f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = local ? Ui.Text : _color, OutlineModulate = new("060a12"), OutlineSize = 10, NoDepthTest = true };
        AddChild(_name);
        _healthRoot = new Node3D { Position = new(0, 2.6f, 0) }; AddChild(_healthRoot);
        Mesh(_healthRoot, new BoxMesh { Size = new(1.42f, 0.13f, 0.025f) }, Vector3.Zero, new StandardMaterial3D { AlbedoColor = new("121c28"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _healthFill = Mesh(_healthRoot, new BoxMesh { Size = new(1.34f, 0.09f, 0.04f) }, new(0, 0, -0.03f),
            new StandardMaterial3D { AlbedoColor = local ? Ui.Health : _color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldTrack = Mesh(_healthRoot, new BoxMesh { Size = new(1.42f, 0.14f, 0.025f) }, new(0, 0.19f, 0),
            new StandardMaterial3D { AlbedoColor = new("332b0b"), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldFill = Mesh(_healthRoot, new BoxMesh { Size = new(1.34f, 0.10f, 0.04f) }, new(0, 0.19f, -0.03f),
            new StandardMaterial3D { AlbedoColor = Ui.Shield, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        _shieldAmount = new Label3D { Position = new(0, 0.48f, -0.06f), FontSize = 30, PixelSize = 0.012f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = Ui.Shield,
            OutlineModulate = new("060a12"), OutlineSize = 8, NoDepthTest = true };
        _healthRoot.AddChild(_shieldAmount);
    }
    private static MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 position, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = material }; parent.AddChild(node); return node;
    }
    public void Update(WizardSnapshot p, float dt, float time, Camera3D camera)
    {
        var target = ArenaRenderer.World(p.Position);
        float distance = Position.DistanceTo(target);
        Position = distance > 4 ? target : Position.Lerp(target, 1 - Mathf.Exp(-16 * dt));
        float angle = Mathf.Atan2(p.Facing.X, p.Facing.Y);
        _body.Rotation = new(0, Mathf.LerpAngle(_body.Rotation.Y, angle, 1 - Mathf.Exp(-18 * dt)), 0);
        _body.Position = new(0, distance > 0.025f ? Mathf.Sin(time * 14 + p.Id) * 0.035f : 0, 0);
        _body.Visible = p.Alive; _ghost.Visible = !p.Alive; _halo.Visible = p.Alive;
        _shield.Visible = p.Alive && (p.Shield > 0 || p.Frozen > 0);
        if (_shield.MaterialOverride is StandardMaterial3D shieldMat)
        {
            var c = p.Frozen > 0 ? Ui.ElementColor(Element.Cold) : p.Ward.Length > 0 ? Ui.SpellColor(p.Ward) : Ui.Shield;
            c.A = p.Frozen > 0 ? 0.42f : 0.12f + 0.10f * Math.Clamp(p.Shield / Ui.ShieldMaximum(p), 0, 1);
            shieldMat.AlbedoColor = c; shieldMat.Emission = c;
        }
        _shield.Scale = Vector3.One * (1 + Mathf.Sin(time * 3) * 0.03f);
        _name.Text = p.Name + (!p.Alive ? $"  {Mathf.CeilToInt(p.Respawn)}s" : p.Frozen > 0 ? "  CONGELADO" : p.Burning > 0 ? "  FUEGO" : p.Wet > 0 ? "  MOJADO" : "");
        _healthRoot.Visible = p.Alive;
        _healthRoot.LookAt(camera.GlobalPosition);
        float hp = Math.Clamp(p.Health / p.MaxHealth, 0, 1);
        _healthFill.Visible = p.Health > 0;
        _healthFill.Scale = new(hp, 1, 1); _healthFill.Position = new(-(1 - hp) * 0.67f, 0, -0.03f);
        bool shielded = p.Alive && p.Shield > 0;
        _shieldTrack.Visible = _shieldFill.Visible = _shieldAmount.Visible = shielded;
        if (shielded)
        {
            float fraction = Math.Clamp(p.Shield / Ui.ShieldMaximum(p), 0, 1);
            _shieldFill.Scale = new(fraction, 1, 1); _shieldFill.Position = new(-(1 - fraction) * 0.67f, 0.19f, -0.03f);
            _shieldAmount.Text = $"{Math.Ceiling(p.Shield):0}/{Ui.ShieldMaximum(p):0}";
        }
    }
}
