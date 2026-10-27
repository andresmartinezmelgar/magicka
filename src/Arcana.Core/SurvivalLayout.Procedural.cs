namespace Arcana.Core;

internal static partial class SurvivalLayout
{
    internal static ArenaDefinition Create(SurvivalMapSpec spec)
    {
        // Azar propio: generar el terreno nunca consume la secuencia de la horda o del botín.
        var random = new SurvivalRandom(spec.Seed);
        var floors = new List<FloorSection>();
        var centers = new V2[9];
        for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
        {
            int index = row * 3 + col;
            var center = index == 4 ? V2.Zero : new V2((col - 1) * 56 + random.Next(-3, 4) * 2,
                (row - 1) * 38 + random.Next(-2, 3) * 2);
            centers[index] = center;
            // Bordes enteros para que suelo, colisiones y sectores usen la misma unión exacta.
            floors.Add(new(center, index == 4 ? 36 : random.Next(11, 16) * 2,
                index == 4 ? 30 : random.Next(9, 14) * 2));
        }
        var links = new List<(int A, int B)>();
        for (int row = 0; row < 3; row++) for (int col = 0; col < 3; col++)
        {
            int at = row * 3 + col;
            if (col < 2) links.Add((at, at + 1));
            if (row < 2) links.Add((at, at + 3));
        }
        for (int i = links.Count - 1; i > 0; i--)
        { int j = random.Next(i + 1); (links[i], links[j]) = (links[j], links[i]); }
        int[] parent = Enumerable.Range(0, 9).ToArray();
        int Root(int at) { while (parent[at] != at) at = parent[at]; return at; }
        void Corridor(V2 a, V2 b, float width) => floors.Add(new((a + b) / 2,
            Math.Abs(a.X - b.X) + width, Math.Abs(a.Y - b.Y) + width));
        void Connect(int a, int b)
        {
            V2 from = centers[a], to = centers[b];
            V2 bend = random.Next(2) == 0 ? new(from.X, to.Y) : new(to.X, from.Y);
            float width = random.Next(4, 7) * 2;
            Corridor(from, bend, width); Corridor(bend, to, width);
        }
        var extra = new List<(int A, int B)>();
        foreach (var link in links)
        {
            int a = Root(link.A), b = Root(link.B);
            if (a == b) { extra.Add(link); continue; }
            parent[a] = b; Connect(link.A, link.B);
        }
        // El árbol garantiza acceso a las nueve salas; los ciclos ofrecen rutas de escape.
        int loops = random.Next(2, 5);
        foreach (var link in extra.Take(loops)) Connect(link.A, link.B);
        TeleportPair[] portals = [new(centers[0], centers[8]), new(centers[2], centers[6])];
        return Build(floors, 160, 120, portals, spec, "Laberinto · supervivencia");
    }
}
