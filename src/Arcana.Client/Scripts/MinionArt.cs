using Arcana.Core;
using Godot;

namespace Arcana.Client;

// El combate y los retratos consultan la misma apariencia.
internal static class MinionArt
{
    public static WizardType? CasterType(MinionKind kind) => kind switch
    {
        MinionKind.Mage or MinionKind.Warlock => WizardType.Pyromancer,
        MinionKind.Shaman => WizardType.Geomancer,
        MinionKind.Cryomancer => WizardType.Cryomancer,
        _ => null
    };
    public static Godot.Mesh Shape(MinionKind kind) => kind switch
    {
        MinionKind.Boss => new CapsuleMesh { Radius = 0.9f, Height = 2.7f, RadialSegments = 16, Rings = 6 },
        MinionKind.Brute => new BoxMesh { Size = new(1.8f, 1.9f, 1.8f) },
        MinionKind.Sentinel => new CapsuleMesh { Radius = 0.6f, Height = 1.75f, RadialSegments = 12, Rings = 4 },
        MinionKind.Prism => new CylinderMesh { TopRadius = 0, BottomRadius = 0.7f, Height = 1.15f, RadialSegments = 6 },
        MinionKind.Oracle => new SphereMesh { Radius = 0.65f, Height = 1.3f, RadialSegments = 12, Rings = 6 },
        MinionKind.Tank => new BoxMesh { Size = new(1.6f, 1.45f, 1.6f) },
        MinionKind.Archer => new BoxMesh { Size = new(0.95f, 0.7f, 0.95f) },
        MinionKind.Runner => new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.45f, Height = 1.1f, RadialSegments = 12 },
        MinionKind.Bomber => new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 12, Rings = 8 },
        _ => new CylinderMesh { TopRadius = kind == MinionKind.Captain ? 0.8f : 0.55f,
            BottomRadius = kind == MinionKind.Captain ? 0.8f : 0.55f, Height = 0.65f, RadialSegments = 16 }
    };
    public static float BodyHeight(MinionKind kind) => kind switch
    { MinionKind.Boss => 1.45f, MinionKind.Brute => 1.05f, MinionKind.Sentinel => 1.05f, MinionKind.Tank => 0.8f, MinionKind.Prism => 1.3f, MinionKind.Oracle => 1.6f, _ => 0.6f };
    public static Color ColorFor(MinionKind kind) => kind switch
    {
        MinionKind.Boss => new("d6ae57"), MinionKind.Tank => new("5c9d91"), MinionKind.Archer => new("d6a36b"), MinionKind.Captain => new("a36de0"),
        MinionKind.Runner => new("d5ee69"), MinionKind.Brute => new("8796af"), MinionKind.Bomber => new("ff913d"),
        MinionKind.Sentinel => new("7199b5"), MinionKind.Warlock => new("ff685d"), MinionKind.Cryomancer => new("91e5ff"),
        MinionKind.Prism => Ui.ElementColor(Element.Fire), MinionKind.Oracle => new("b6a3ff"), _ => new("db636b")
    };

    internal sealed record Details(Node3D Root, MeshInstance3D[] Tinted);
    public static Details Decorate(Node3D parent, MinionKind kind, Element? weakness, Func<Color, Material> material)
    {
        var root = new Node3D { Name = "MinionDetails" }; parent.AddChild(root);
        var tinted = new List<MeshInstance3D>();
        MeshInstance3D Part(string name, Godot.Mesh shape, Vector3 at, Color color, Vector3 rotation = default)
        {
            var mesh = new MeshInstance3D { Name = name, Mesh = shape, Position = at, Rotation = rotation,
                MaterialOverride = material(color), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            root.AddChild(mesh); return mesh;
        }
        if (kind == MinionKind.Prism)
        {
            var color = Ui.ElementColor(weakness ?? Element.Fire);
            tinted.Add(Part("PrismLower", new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0, Height = 0.7f, RadialSegments = 6 }, new(0, 0.375f, 0), color));
            Part("PrismBelt", new TorusMesh { InnerRadius = 0.68f, OuterRadius = 0.76f, Rings = 12, RingSegments = 4 }, new(0, 0.725f, 0), new("26374b"));
        }
        else if (kind == MinionKind.Sentinel)
        {
            Part("KnightHelmet", new SphereMesh { Radius = 0.43f, Height = 0.64f, RadialSegments = 12, Rings = 4 }, new(0, 2.1f, 0), new("adc6dd"));
            Part("KnightVisor", new BoxMesh { Size = new(0.55f, 0.1f, 0.12f) }, new(0, 2.12f, 0.39f), new("1c2940"));
            Part("KnightShield", new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.75f, Height = 0.18f, RadialSegments = 6 }, new(0.67f, 1.17f, 0.55f), new("314b80"), new(MathF.PI / 2, 0, 0));
            Part("KnightShieldRune", new BoxMesh { Size = new(0.14f, 0.8f, 0.12f) }, new(0.67f, 1.17f, 0.69f), Ui.Gold);
            Part("KnightSword", new BoxMesh { Size = new(0.12f, 1.35f, 0.13f) }, new(-0.78f, 1.02f, 0.3f), new("cad8e9"));
            Part("KnightSwordGuard", new BoxMesh { Size = new(0.48f, 0.1f, 0.17f) }, new(-0.78f, 0.72f, 0.3f), Ui.Gold);
        }
        else if (kind == MinionKind.Boss)
        {
            Part("ColossusHead", new SphereMesh { Radius = 0.6f, Height = 0.85f, RadialSegments = 12, Rings = 4 }, new(0, 2.95f, 0), new("d6ae57"));
            foreach (int side in new[] { -1, 1 })
            {
                Part("ColossusFist" + side, new SphereMesh { Radius = 0.5f, Height = 1, RadialSegments = 10, Rings = 4 }, new(side * 1.16f, 1.15f, 0.25f), new("806348"));
                Part("ColossusHorn" + side, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.17f, Height = 0.8f, RadialSegments = 6 }, new(side * 0.48f, 3.4f, 0), new("ebdfbb"), new(0, 0, -side * 0.45f));
            }
        }
        else if (kind == MinionKind.Oracle)
        {
            for (int i = 0; i < 3; i++)
                Part("OracleOrbit" + i, new TorusMesh { InnerRadius = 0.88f, OuterRadius = 0.95f, Rings = 20, RingSegments = 4 },
                    new(0, 1.6f, 0), i == 0 ? Ui.Gold : new("8dbdff"), new(i * 0.7f, 0, i * 0.9f));
            Part("OracleCrown", new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.4f, Height = 0.7f, RadialSegments = 5 }, new(0, 2.53f, 0), Ui.Gold);
        }
        return new(root, tinted.ToArray());
    }
}
