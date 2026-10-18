using Arcana.Core;

static class ScreenPlacementTests
{
    public static void Run(Action<string, Action> test)
    {
        test("HUD: mantiene ocho píxeles entre elementos y con los bordes", () =>
        {
            ScreenRect screen = new(0, 0, 640, 450), obstacle = new(120, 80, 150, 100);
            foreach (var wanted in new[] { new ScreenRect(150, 100, 90, 60), new(-30, -20, 90, 60), new(630, 440, 90, 60) })
            {
                if (!ScreenPlacement.TryPlace(wanted, screen, [obstacle], out var result) ||
                    !ScreenPlacement.Separated(result, obstacle) || result.X < 8 || result.Y < 8 || result.Right > 632 || result.Bottom > 442)
                    throw new Exception("Se aceptó una superposición o un borde sin margen.");
            }
        });
        test("HUD: encuentra el hueco más próximo entre varios obstáculos", () =>
        {
            ScreenRect screen = new(0, 0, 110, 100), wanted = new(40, 36, 20, 20);
            ScreenRect[] obstacles = [new(0, 0, 25, 100), new(70, 0, 40, 100), new(30, 20, 35, 30)];
            if (!ScreenPlacement.TryPlace(wanted, screen, obstacles, out var placed)) throw new Exception("No encuentra el hueco inferior.");
            float Distance(ScreenRect rect) => (rect.X - wanted.X) * (rect.X - wanted.X) + (rect.Y - wanted.Y) * (rect.Y - wanted.Y);
            for (int x = 8; x <= 82; x++) for (int y = 8; y <= 72; y++)
            {
                ScreenRect candidate = new(x, y, 20, 20);
                if (obstacles.All(obstacle => ScreenPlacement.Separated(candidate, obstacle)) && Distance(candidate) < Distance(placed) - .01f)
                    throw new Exception("El ajuste se aleja más de lo necesario.");
            }
        });
        test("HUD: conserva posiciones válidas y no inventa huecos cuando no caben", () =>
        {
            ScreenRect screen = new(0, 0, 100, 100), wanted = new(50, 50, 20, 20);
            if (!ScreenPlacement.TryPlace(wanted, screen, [], out var kept) || kept != wanted ||
                ScreenPlacement.TryPlace(wanted, screen, [screen], out _) ||
                ScreenPlacement.TryPlace(new(0, 0, 100, 100), screen, [], out _) ||
                ScreenPlacement.TryPlace(new(float.NaN, 0, 10, 10), screen, [], out _))
                throw new Exception("La colocación no respeta posiciones, límites o valores finitos.");
        });
    }
}
