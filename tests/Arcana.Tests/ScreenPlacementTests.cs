using Arcana.Core;

static class ScreenPlacementTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Joysticks: margen fijo de 24 píxeles para todo el círculo en bordes y esquinas", () =>
        {
            foreach (var screen in new[] { new ScreenRect(0, 0, 640, 450), new(0, 0, 1440, 720), new(30, 20, 1024, 640) })
            foreach (float radius in new[] { 32f, 55f, 82f })
            foreach (float x in new[] { screen.X + 1, screen.Right - 1, screen.X + screen.Width / 2 })
            foreach (float y in new[] { screen.Y + 1, screen.Bottom - 1 })
            {
                var wanted = new ScreenRect(x - radius, y - radius, radius * 2, radius * 2);
                if (!ScreenPlacement.TryPlaceJoystick(wanted, screen, [], out var placed) ||
                    placed.X < screen.X + 24 || placed.Y < screen.Y + 24 ||
                    placed.Right > screen.Right - 24 || placed.Bottom > screen.Bottom - 24)
                    throw new Exception("Un joystick pierde su margen fijo o sale de pantalla.");
                // Desde este origen se alcanza cada dirección completa sin salir del dispositivo.
                float centerX = placed.X + radius, centerY = placed.Y + radius;
                if (centerX - radius < screen.X || centerX + radius > screen.Right ||
                    centerY - radius < screen.Y || centerY + radius > screen.Bottom)
                    throw new Exception("El origen impide completar el recorrido en alguna dirección.");
            }
        });
        test("Joysticks: un contacto con espacio conserva exactamente su origen y evita el HUD", () =>
        {
            ScreenRect screen = new(0, 0, 640, 450), wanted = new(150, 170, 100, 100);
            ScreenRect[] occupied = [new(20, 20, 130, 100), new(440, 260, 150, 150)];
            if (!ScreenPlacement.TryPlaceJoystick(wanted, screen, occupied, out var exact) || exact != wanted)
                throw new Exception("Un contacto válido se desplaza hacia la posición predeterminada.");
            var overlapping = wanted with { X = 105, Y = 75 };
            if (!ScreenPlacement.TryPlaceJoystick(overlapping, screen, occupied, out var safe) ||
                occupied.Any(rect => !ScreenPlacement.Separated(safe, rect)))
                throw new Exception("El joystick se superpone al HUD o al otro joystick.");
            if (ScreenPlacement.TryPlaceJoystick(new(0, 0, 164, 164), new(0, 0, 200, 200), [], out _))
                throw new Exception("Reduce el margen en vez de rechazar un círculo que no cabe.");
        });
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
