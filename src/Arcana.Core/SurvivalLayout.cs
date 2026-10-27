namespace Arcana.Core;

/// <summary>Conserva el terreno antiguo para continuar guardados anteriores.</summary>
internal static partial class SurvivalLayout
{
    internal static ArenaDefinition Create()
    {
        var floors = new List<FloorSection>();
        void Room(float x, float y, float w, float h) => floors.Add(new(new(x - 96, y - 72), w, h));
        void Link(float x1, float y1, float x2, float y2) =>
            Room((x1 + x2) / 2, (y1 + y2) / 2, Math.Abs(x2 - x1) + 8, Math.Abs(y2 - y1) + 8);
        foreach (var (x, y) in new[] { (20, 20), (172, 20), (172, 124), (20, 124) }) Room(x, y, 26, 26);
        foreach (var (x, y) in new[] { (48, 40), (144, 40), (144, 104), (48, 104) }) Room(x, y, 20, 20);
        Room(96, 72, 34, 28);
        foreach (var (left, top, right, bottom) in new[] { (20, 20, 172, 124), (48, 40, 144, 104) })
        {
            Link(left, top, right, top); Link(right, top, right, bottom);
            Link(right, bottom, left, bottom); Link(left, bottom, left, top);
        }
        Link(96, 20, 96, 124); Link(20, 72, 172, 72);
        return Build(floors, 192, 144,
            [new(new(-76, -52), new(76, 52)), new(new(76, -52), new(-76, 52))],
            SurvivalMapSpec.Legacy, "Supervivencia");
    }

    private static ArenaDefinition Build(List<FloorSection> floors, int width, int height,
        TeleportPair[] portals, SurvivalMapSpec spec, string name, SurvivalMapStructure? structure = null,
        Action? navigationCheckpoint = null)
    {
        // Extrae las paredes de la unión de rectángulos, incluidos los patios sin suelo.
        int halfWidth = width / 2, halfHeight = height / 2;
        var filled = new bool[width, height];
        foreach (var floor in floors)
            for (int x = (int)(floor.Center.X - floor.Width / 2) + halfWidth;
                x < floor.Center.X + floor.Width / 2 + halfWidth; x++)
                for (int y = (int)(floor.Center.Y - floor.Height / 2) + halfHeight;
                    y < floor.Center.Y + floor.Height / 2 + halfHeight; y++)
                    filled[x, y] = true;
        bool At(int x, int y) => x >= 0 && x < width && y >= 0 && y < height && filled[x, y];
        var edges = new List<MapEdge>();
        for (int y = 0; y <= height; y++)
            for (int x = 0; x < width;)
            {
                if (At(x, y - 1) == At(x, y)) { x++; continue; }
                int start = x++;
                while (x < width && At(x, y - 1) != At(x, y)) x++;
                edges.Add(new(new(start - halfWidth, y - halfHeight), new(x - halfWidth, y - halfHeight)));
            }
        for (int x = 0; x <= width; x++)
            for (int y = 0; y < height;)
            {
                if (At(x - 1, y) == At(x, y)) { y++; continue; }
                int start = y++;
                while (y < height && At(x - 1, y) != At(x, y)) y++;
                edges.Add(new(new(x - halfWidth, start - halfHeight), new(x - halfWidth, y - halfHeight)));
            }
        return new(ArenaMapId.Bastion, name, halfWidth, halfHeight, floors.ToArray(), [], [], [], 0,
            teleports: portals, boundary: edges.ToArray(), survivalMap: spec, survivalStructure: structure,
            navigationCheckpoint: navigationCheckpoint);
    }
}
