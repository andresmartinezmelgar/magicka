namespace Arcana.Core;

/// <summary>Dos anillos comunicados y una plaza central; geometría idéntica en cliente y servidor.</summary>
internal static class SurvivalLayout
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
        // Extrae las paredes de la unión de rectángulos, incluidos los patios sin suelo.
        var filled = new bool[192, 144];
        for (int x = 0; x < 192; x++) for (int y = 0; y < 144; y++)
            filled[x, y] = floors.Any(f => Math.Abs(x - 95.5f - f.Center.X) < f.Width / 2 &&
                Math.Abs(y - 71.5f - f.Center.Y) < f.Height / 2);
        bool At(int x, int y) => x >= 0 && x < 192 && y >= 0 && y < 144 && filled[x, y];
        var edges = new List<MapEdge>();
        for (int y = 0; y <= 144; y++)
            for (int x = 0; x < 192;)
            {
                if (At(x, y - 1) == At(x, y)) { x++; continue; }
                int start = x++;
                while (x < 192 && At(x, y - 1) != At(x, y)) x++;
                edges.Add(new(new(start - 96, y - 72), new(x - 96, y - 72)));
            }
        for (int x = 0; x <= 192; x++)
            for (int y = 0; y < 144;)
            {
                if (At(x - 1, y) == At(x, y)) { y++; continue; }
                int start = y++;
                while (y < 144 && At(x - 1, y) != At(x, y)) y++;
                edges.Add(new(new(x - 96, start - 72), new(x - 96, y - 72)));
            }
        return new(ArenaMapId.Bastion, "Anillos · supervivencia", 96, 72, floors.ToArray(), [], [], [], 0,
            teleports: [new(new(-76, -52), new(76, 52)), new(new(76, -52), new(-76, 52))], boundary: edges.ToArray());
    }
}
