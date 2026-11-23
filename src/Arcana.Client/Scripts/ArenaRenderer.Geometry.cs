using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private StandardMaterial3D Material(Color color, float glow = 0, bool unshaded = false)
    {
        var key = (color, glow, unshaded);
        if (_materials.TryGetValue(key, out var existing)) return existing;
        var material = new StandardMaterial3D
        {
            AlbedoColor = color, Roughness = 0.82f,
            Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
            EmissionEnabled = glow > 0, Emission = color, EmissionEnergyMultiplier = glow,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _materials[key] = material; return material;
    }

    private MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 position, Color color, float glow = 0, bool unshaded = false)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = Material(color, glow, unshaded) };
        parent.AddChild(node); return node;
    }

    private MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color color) => Mesh(parent, new BoxMesh { Size = size }, pos, color);

    private MeshInstance3D Ring(Node3D parent, float radius, float width, Vector3 pos, Color color) =>
        Mesh(parent, new TorusMesh { InnerRadius = radius - width, OuterRadius = radius, Rings = 48, RingSegments = 8 }, pos, color, 0.6f);

    private void BuildMap()
    {
        foreach (var step in PrepareMap(_map, true)) step();
    }

    internal IEnumerable<Action> PrepareMap(ArenaDefinition definition, bool force = false)
    {
        if (!force && _map.Id == definition.Id && _map.SurvivalMap == definition.SurvivalMap && _mapPrepared) yield break;
        Node3D? rootMap = null;
        yield return () =>
        {
            Clear(); _map = definition; _mapPrepared = false;
            if (_mapRoot != null) { RemoveChild(_mapRoot); _mapRoot.QueueFree(); }
            _survivalSectors.Clear();
            _preparedSurvivalSectors.Clear(); _survivalWindow = null; SurvivalSectorBuilds = 0;
            _points.Clear(); _portals.Clear(); _frontlineSpawns.Clear(); _practiceCasterLabel = null;
            rootMap = new Node3D { Name = "MapGeometry" }; _mapRoot = rootMap; AddChild(rootMap);
        };
        // La enumeración prepara trabajos; los nodos se crean al ejecutar cada paso.
        if (definition.Id != ArenaMapId.Bastion)
            foreach (var step in PrepareScenery(() => rootMap!, definition)) yield return step;
        else yield return () => PrepareSurvivalScenery(rootMap!);
        yield return () => { BuildMapDetails(rootMap!); _mapPrepared = true; };
    }

    private void BuildMapDetails(Node3D rootMap)
    {
        if (_map.IsPractice) { BuildPracticeMap(rootMap); return; }
        if (_map.Id == ArenaMapId.Bastion) { BuildFrontline(rootMap); return; }
        for (int team = 0; team < 2; team++)
        {
            var color = team == 0 ? Ui.Blue : Ui.Red;
            float x = _map.Spawn(team, 0).X;
            Ring(rootMap, 2.7f, 0.10f, new(x, 0.05f, 0), color);
            Ring(rootMap, 2.5f, 0.045f, new(x, 0.05f, 0), color);
            for (int i = -1; i <= 1; i += 2)
            {
                Mesh(rootMap, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.5f, Height = 1.2f }, new(x, 0.6f, i * 3.5f), new("424e58"));
                Mesh(rootMap, new SphereMesh { Radius = 0.24f, Height = 0.48f }, new(x, 1.4f, i * 3.5f), color, 1.1f);
            }
        }
        foreach (var p in _map.ControlPoints.Select((position, id) => (position, id)))
        {
            var visual = new CapturePointVisual(); rootMap.AddChild(visual); visual.Build(p.id, p.position, _map.Id); _points[p.id] = visual;
        }
        BuildFrontline(rootMap);
    }
}
