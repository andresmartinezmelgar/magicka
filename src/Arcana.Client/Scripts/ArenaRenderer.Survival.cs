using Arcana.Core;
using Godot;

namespace Arcana.Client;

public partial class ArenaRenderer
{
    private const int SurvivalSectorSize = 24;
    private readonly Dictionary<(int X, int Y), Node3D> _survivalSectors = [];
    private readonly Dictionary<(int X, int Y), Node3D> _preparedSurvivalSectors = [];
    private (int MinX, int MaxX, int MinY, int MaxY)? _survivalWindow;
    internal int PreparedSurvivalSectors => _preparedSurvivalSectors.Count;
    internal int SurvivalSectorBuilds { get; private set; }
    private V2 _survivalCenter;
    private float _survivalHalfX, _survivalHalfY;
    internal int VisibleSurvivalSectors => _survivalSectors.Count;
    internal int TotalSurvivalSectors => 48;
    internal IReadOnlyCollection<(int X, int Y)> SurvivalSectorKeys => _survivalSectors.Keys;

    private bool NearSurvival(V2 position, float margin = 4) => _map.Id != ArenaMapId.Bastion ||
        Math.Abs(position.X - _survivalCenter.X) <= _survivalHalfX + margin &&
        Math.Abs(position.Y - _survivalCenter.Y) <= _survivalHalfY + margin;
    private bool NearSurvivalSegment(V2 a, V2 b) => _map.Id != ArenaMapId.Bastion ||
        Math.Min(a.X, b.X) <= _survivalCenter.X + _survivalHalfX + 4 && Math.Max(a.X, b.X) >= _survivalCenter.X - _survivalHalfX - 4 &&
        Math.Min(a.Y, b.Y) <= _survivalCenter.Y + _survivalHalfY + 4 && Math.Max(a.Y, b.Y) >= _survivalCenter.Y - _survivalHalfY - 4;

    private void UpdateSurvivalGeometry(V2 center)
    {
        if (_map.Id != ArenaMapId.Bastion) return;
        _survivalCenter = center;
        var viewport = GetViewport().GetVisibleRect().Size;
        // Margen conservador para ambas relaciones de aspecto, paredes y seguimiento suave.
        _survivalHalfX = Math.Max(_zoom, Camera.Size) * Math.Max(1, viewport.X / Math.Max(1, viewport.Y)) / 2 + 12;
        _survivalHalfY = Math.Max(_zoom, Camera.Size) * 0.7f + 14;
        int minX = Math.Max(-4, (int)MathF.Floor((center.X - _survivalHalfX) / SurvivalSectorSize));
        int maxX = Math.Min(3, (int)MathF.Floor((center.X + _survivalHalfX) / SurvivalSectorSize));
        int minY = Math.Max(-3, (int)MathF.Floor((center.Y - _survivalHalfY) / SurvivalSectorSize));
        int maxY = Math.Min(2, (int)MathF.Floor((center.Y + _survivalHalfY) / SurvivalSectorSize));
        var window = (minX, maxX, minY, maxY);
        if (_survivalWindow == window) return;
        _survivalWindow = window;
        var required = new HashSet<(int X, int Y)>();
        for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
        {
            var key = (x, y); required.Add(key);
            if (!_survivalSectors.ContainsKey(key))
            {
                var sector = PreparedSurvivalSector(x, y);
                sector.Visible = true; _survivalSectors[key] = sector;
            }
        }
        foreach (var key in _survivalSectors.Keys.Where(k => !required.Contains(k)).ToArray())
        {
            _survivalSectors[key].Visible = false;
            _survivalSectors.Remove(key);
        }
    }

    private Node3D PreparedSurvivalSector(int x, int y)
    {
        if (_preparedSurvivalSectors.TryGetValue((x, y), out var sector)) return sector;
        sector = BuildSurvivalSector(x, y); sector.Visible = false;
        _preparedSurvivalSectors[(x, y)] = sector; SurvivalSectorBuilds++;
        return sector;
    }

    private IEnumerable<(string Name, Action Work)> PrepareSurvivalSectors()
    {
        for (int x = -4; x <= 3; x++) for (int y = -3; y <= 2; y++)
        {
            int sx = x, sy = y;
            yield return ("Preparando los sectores y portales…", () =>
            {
                PreparedSurvivalSector(sx, sy);
                var center = new V2((sx + 0.5f) * SurvivalSectorSize, (sy + 0.5f) * SurvivalSectorSize);
                UpdateSurvivalGeometry(center); FocusLoadingView(center);
                foreach (var portal in _portals) portal.Visible = NearSurvival(new(portal.Position.X, portal.Position.Z));
            });
        }
    }

    private Node3D BuildSurvivalSector(int x, int y)
    {
        var root = new Node3D { Name = $"SurvivalSector_{x}_{y}" }; _mapRoot!.AddChild(root);
        float left = x * SurvivalSectorSize, top = y * SurvivalSectorSize;
        bool Floor(int cx, int cy) => _map.Floors.Any(f =>
            Math.Abs(left + cx + 0.5f - f.Center.X) < f.Width / 2 && Math.Abs(top + cy + 0.5f - f.Center.Y) < f.Height / 2);
        // Fusiona las celdas del suelo: sin caras superpuestas ni geometría del mapa completo.
        var rectangles = new Dictionary<(int Start, int End), (int Row, int Height)>();
        void AddFloor((int Start, int End) span, (int Row, int Height) area)
        {
            var tile = Mesh(root, new PlaneMesh { Size = new(span.End - span.Start, area.Height) },
                new(left + (span.Start + span.End) / 2f, 0, top + area.Row + area.Height / 2f), new("354751"));
            tile.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        for (int row = 0; row <= SurvivalSectorSize; row++)
        {
            var spans = new HashSet<(int Start, int End)>();
            for (int col = 0; row < SurvivalSectorSize && col < SurvivalSectorSize;)
            {
                if (!Floor(col, row)) { col++; continue; }
                int start = col++;
                while (col < SurvivalSectorSize && Floor(col, row)) col++;
                spans.Add((start, col));
            }
            foreach (var span in rectangles.Keys.Where(s => !spans.Contains(s)).ToArray())
            { AddFloor(span, rectangles[span]); rectangles.Remove(span); }
            foreach (var span in spans)
                rectangles[span] = rectangles.TryGetValue(span, out var area) ? (area.Row, area.Height + 1) : (row, 1);
        }
        foreach (var edge in _map.Boundary)
        {
            bool horizontal = edge.A.Y == edge.B.Y;
            if (horizontal)
            {
                if (edge.A.Y < top || edge.A.Y >= top + SurvivalSectorSize) continue;
                float a = Math.Max(left, Math.Min(edge.A.X, edge.B.X)), b = Math.Min(left + SurvivalSectorSize, Math.Max(edge.A.X, edge.B.X));
                if (b <= a) continue;
                Wall(new(b - a, 1.1f, 0.45f), new((a + b) / 2, 0.5f, edge.A.Y));
            }
            else
            {
                if (edge.A.X < left || edge.A.X >= left + SurvivalSectorSize) continue;
                float a = Math.Max(top, Math.Min(edge.A.Y, edge.B.Y)), b = Math.Min(top + SurvivalSectorSize, Math.Max(edge.A.Y, edge.B.Y));
                if (b <= a) continue;
                Wall(new(0.45f, 1.1f, b - a), new(edge.A.X, 0.5f, (a + b) / 2));
            }
        }
        void Wall(Vector3 size, Vector3 at)
        {
            var wall = Box(root, size, at, new("667880")); wall.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            Box(root, new(size.X, 0.09f, size.Z), at + Vector3.Up * 0.59f, new("9bb5b6")).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        return root;
    }
}
