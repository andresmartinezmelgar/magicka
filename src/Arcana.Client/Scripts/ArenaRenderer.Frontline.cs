using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private readonly List<Node3D> _portals = [];
    private readonly List<Node3D> _frontlineSpawns = [];

    private void BuildFrontline(Node3D root)
    {
        _portals.Clear(); _frontlineSpawns.Clear();
        for (int i = 0; i < _map.Teleports.Count; i++)
        {
            var color = i == 0 ? new Color("59ddd0") : i == 1 ? new Color("cea1ff") : new Color("ffd080");
            foreach (var at in new[] { _map.Teleports[i].A, _map.Teleports[i].B })
            {
                var portal = new Node3D { Name = $"TeleportPortal{i}_{(at == _map.Teleports[i].A ? "A" : "B")}", Position = World(at) }; root.AddChild(portal);
                Ring(portal, 1.25f, 0.13f, new(0, 0.11f, 0), color);
                Ring(portal, 0.90f, 0.045f, new(0, 0.14f, 0), color);
                Mesh(portal, new CylinderMesh { TopRadius = 0.78f, BottomRadius = 0.78f, Height = 0.04f },
                    new(0, 0.07f, 0), new Color(color, 0.24f), 0.3f, true);
                for (int n = 0; n <= i; n++)
                    Mesh(portal, new PrismMesh { Size = new(0.22f, 0.42f, 0.22f) }, new((n - i * 0.5f) * 0.4f, 0.35f, 1.6f), color, 0.5f);
                _portals.Add(portal);
            }
        }
        if (!_map.SequentialCapture) return;
        for (int team = 0; team < 2; team++)
        {
            var spawn = new Node3D { Name = team == 0 ? "BlueForwardSpawn" : "RedForwardSpawn" }; root.AddChild(spawn);
            Ring(spawn, 2.1f, 0.14f, new(0, 0.08f, 0), team == 0 ? Ui.Blue : Ui.Red);
            Ring(spawn, 1.8f, 0.04f, new(0, 0.08f, 0), team == 0 ? Ui.Blue : Ui.Red);
            _frontlineSpawns.Add(spawn);
        }
    }

    private void UpdateFrontline(WorldSnapshot state, float time)
    {
        foreach (var portal in _portals)
        {
            portal.Visible = NearSurvival(new(portal.Position.X, portal.Position.Z));
            if (portal.Visible) portal.Scale = Vector3.One * (1 + Mathf.Sin(time * 3) * 0.04f);
        }
        for (int team = 0; team < _frontlineSpawns.Count; team++)
        {
            int count = 0;
            for (int i = 0; i < state.Points.Length; i++)
            {
                int index = team == 0 ? i : state.Points.Length - 1 - i;
                if (state.Points[index].Owner != team) break;
                count++;
            }
            var at = _map.Spawn(team, 0, count);
            _frontlineSpawns[team].Position = World(new(at.X, at.Y + (count == 0 ? 3.3f : 2.7f)));
        }
    }
}
