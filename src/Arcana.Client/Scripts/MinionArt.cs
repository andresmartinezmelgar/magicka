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
        MinionKind.Boss => new BoxMesh { Size = new(2.6f, 2.7f, 2.6f) },
        MinionKind.Brute => new BoxMesh { Size = new(1.8f, 1.9f, 1.8f) },
        MinionKind.Sentinel => new BoxMesh { Size = new(2.1f, 2.3f, 2.1f) },
        MinionKind.Tank => new BoxMesh { Size = new(1.6f, 1.45f, 1.6f) },
        MinionKind.Archer => new BoxMesh { Size = new(0.95f, 0.7f, 0.95f) },
        MinionKind.Runner => new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.45f, Height = 1.1f, RadialSegments = 12 },
        MinionKind.Bomber => new SphereMesh { Radius = 0.6f, Height = 1.2f, RadialSegments = 12, Rings = 8 },
        _ => new CylinderMesh { TopRadius = kind == MinionKind.Captain ? 0.8f : 0.55f,
            BottomRadius = kind == MinionKind.Captain ? 0.8f : 0.55f, Height = 0.65f, RadialSegments = 16 }
    };
    public static float BodyHeight(MinionKind kind) => kind switch
    { MinionKind.Boss => 1.4f, MinionKind.Brute => 1.05f, MinionKind.Sentinel => 1.17f, MinionKind.Tank => 0.8f, _ => 0.6f };
    public static Color ColorFor(MinionKind kind) => kind switch
    {
        MinionKind.Boss => new("d6ae57"), MinionKind.Tank => new("5c9d91"), MinionKind.Archer => new("d6a36b"), MinionKind.Captain => new("a36de0"),
        MinionKind.Runner => new("d5ee69"), MinionKind.Brute => new("8796af"), MinionKind.Bomber => new("ff913d"),
        MinionKind.Sentinel => new("ddbb67"), MinionKind.Warlock => new("ff685d"), MinionKind.Cryomancer => new("91e5ff"), _ => new("db636b")
    };
}
