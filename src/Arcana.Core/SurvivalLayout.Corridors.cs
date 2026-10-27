namespace Arcana.Core;

internal static partial class SurvivalLayout
{
    private sealed partial class RandomizedTerrain
    {
        internal void ConnectRooms()
        {
            // Grafo libre: todas las salas pueden ser vecinas, con preferencia aleatoria por las próximas.
            var links = new List<(int A, int B, int Cost)>();
            for (int a = 0; a < _rooms.Count; a++) for (int b = a + 1; b < _rooms.Count; b++)
            {
                int dx = _rooms[a].X - _rooms[b].X, dy = _rooms[a].Y - _rooms[b].Y;
                links.Add((a, b, (dx * dx + dy * dy) * random.Next(65, 151)));
            }
            var parent = Enumerable.Range(0, _rooms.Count).ToArray();
            int Root(int at) { while (parent[at] != at) at = parent[at]; return at; }
            var extra = new List<(int A, int B)>();
            foreach (var link in links.OrderBy(p => p.Cost).ThenBy(p => p.A).ThenBy(p => p.B))
            {
                budget(); int a = Root(link.A), b = Root(link.B);
                if (a == b) { extra.Add((link.A, link.B)); continue; }
                parent[a] = b; Connect(link.A, link.B);
            }
            // Los ciclos no tienen un cupo fijo y pueden cruzar otras rutas o ensanchar salas.
            for (int i = extra.Count - 1; i > 0; i--)
            { int j = random.Next(i + 1); (extra[i], extra[j]) = (extra[j], extra[i]); }
            int loops = random.Next(1, _rooms.Count / 2 + 2);
            foreach (var link in extra.Take(loops)) { Connect(link.A, link.B); _loops++; }
        }

        private void Connect(int a, int b)
        {
            var from = _rooms[a]; var to = _rooms[b]; int style = random.Next(5);
            _connections++; _corridorShapes |= 1 << style;
            switch (style)
            {
                case 0: Stroke(from.X, from.Y, to.X, to.Y); break;
                case 1:
                    if (random.Next(2) == 0)
                    { Stroke(from.X, from.Y, from.X, to.Y); Stroke(from.X, to.Y, to.X, to.Y); }
                    else
                    { Stroke(from.X, from.Y, to.X, from.Y); Stroke(to.X, from.Y, to.X, to.Y); }
                    break;
                case 2:
                    int x = ClampX((from.X + to.X) / 2 + random.Next(-12, 13));
                    int y = ClampY((from.Y + to.Y) / 2 + random.Next(-10, 11));
                    if (random.Next(2) == 0)
                    { Stroke(from.X, from.Y, x, from.Y); Stroke(x, from.Y, x, to.Y); Stroke(x, to.Y, to.X, to.Y); }
                    else
                    { Stroke(from.X, from.Y, from.X, y); Stroke(from.X, y, to.X, y); Stroke(to.X, y, to.X, to.Y); }
                    break;
                case 3: Curve(from, to); break;
                default: Wander(from, to); break;
            }
        }

        private static int ClampX(int x) => Math.Clamp(x, Margin + 4, Columns - Margin - 4);
        private static int ClampY(int y) => Math.Clamp(y, Margin + 4, Rows - Margin - 4);

        private void Brush(int cx, int cy, int radius)
        {
            for (int x = cx - radius; x < cx + radius; x++) for (int y = cy - radius; y < cy + radius; y++)
            {
                int dx = 2 * (x - cx) + 1, dy = 2 * (y - cy) + 1;
                if (dx * dx + dy * dy <= 4 * radius * radius) Fill(x, y);
            }
        }

        private void Stroke(int x0, int y0, int x1, int y1)
        {
            int steps = Math.Max(1, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            int radius = random.Next(2, 5), nextWidth = random.Next(3, 10);
            for (int i = 0; i <= steps; i++)
            {
                if ((i & 15) == 0) budget();
                if (i == nextWidth) { radius = random.Next(2, 5); nextWidth += random.Next(3, 10); }
                Brush(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps, radius);
            }
        }

        private void Curve(Room from, Room to)
        {
            int spread = Math.Max(4, (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y)) / 2);
            int ax = ClampX((from.X * 2 + to.X) / 3 + random.Next(-spread, spread + 1));
            int ay = ClampY((from.Y * 2 + to.Y) / 3 + random.Next(-spread, spread + 1));
            int bx = ClampX((from.X + to.X * 2) / 3 + random.Next(-spread, spread + 1));
            int by = ClampY((from.Y + to.Y * 2) / 3 + random.Next(-spread, spread + 1));
            const int samples = 96; int previousX = from.X, previousY = from.Y;
            for (int i = 1; i <= samples; i++)
            {
                // Bézier cúbica entera: idéntica en ARM64 y x64, sin trigonometría dependiente del runtime.
                long t = i, s = samples - i, divisor = samples * samples * samples;
                int x = (int)((s * s * s * from.X + 3 * s * s * t * ax + 3 * s * t * t * bx + t * t * t * to.X) / divisor);
                int y = (int)((s * s * s * from.Y + 3 * s * s * t * ay + 3 * s * t * t * by + t * t * t * to.Y) / divisor);
                if (x == previousX && y == previousY) continue;
                Stroke(previousX, previousY, x, y); previousX = x; previousY = y;
            }
        }

        private void Wander(Room from, Room to)
        {
            int x = from.X, y = from.Y, limit = (Math.Abs(to.X - x) + Math.Abs(to.Y - y)) * 3 + 24;
            int radius = random.Next(2, 5), nextWidth = random.Next(3, 10);
            for (int i = 0; i < limit && (x != to.X || y != to.Y); i++)
            {
                if ((i & 15) == 0) budget();
                if (i == nextWidth) { radius = random.Next(2, 5); nextWidth += random.Next(3, 10); }
                Brush(x, y, radius);
                if (random.Next(4) == 0)
                { if (random.Next(2) == 0) x = ClampX(x + random.Next(-1, 2)); else y = ClampY(y + random.Next(-1, 2)); }
                else if (random.Next(Math.Abs(to.X - x) + Math.Abs(to.Y - y)) < Math.Abs(to.X - x)) x += Math.Sign(to.X - x);
                else y += Math.Sign(to.Y - y);
            }
            // El paseo tiene un límite de trabajo; el remate siempre alcanza la sala de destino.
            Stroke(x, y, to.X, to.Y);
        }
    }
}
