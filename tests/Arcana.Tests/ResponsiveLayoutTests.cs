using Arcana.Core;

static class ResponsiveLayoutTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Responsive: escritorio distribuye cuatro tarjetas sin desbordar", () =>
        {
            if (ResponsiveLayout.Columns(1200, 185, 4, 16) != 4)
                throw new Exception("La rejilla de escritorio no alcanza cuatro columnas.");
        });
        test("Responsive: móvil reduce columnas al estrechar ventana", () =>
        {
            if (ResponsiveLayout.Columns(500, 185, 4, 16) != 2 ||
                ResponsiveLayout.Columns(300, 185, 4, 16) != 1 ||
                ResponsiveLayout.Columns(785, 185, 4, 16) != 3)
                throw new Exception("La rejilla no respeta las dimensiones disponibles.");
        });
        test("Responsive: formulario de salas mantiene pares etiqueta-campo", () =>
        {
            foreach (float width in new[] { 320f, 600f, 900f, 1300f })
            {
                int columns = ResponsiveLayout.Columns(width, 270, 3, 18) * 2;
                if (columns is not (2 or 4 or 6))
                    throw new Exception("Las etiquetas han quedado separadas de sus controles.");
            }
        });
        test("Responsive: navegación contempla filas incompletas", () =>
        {
            if (ResponsiveLayout.Rows(7, 7) != 1 || ResponsiveLayout.Rows(7, 4) != 2 ||
                ResponsiveLayout.Rows(7, 3) != 3 || ResponsiveLayout.Rows(0, 3) != 0)
                throw new Exception("Número de filas de navegación incorrecto.");
        });
        test("Responsive: rechaza medidas inválidas", () =>
        {
            foreach (var action in new Action[]
            {
                () => ResponsiveLayout.Columns(float.NaN, 100, 3),
                () => ResponsiveLayout.Columns(100, 0, 3),
                () => ResponsiveLayout.Columns(100, 20, 0),
                () => ResponsiveLayout.Columns(100, 20, 2, -1),
                () => ResponsiveLayout.Rows(-1, 2),
                () => ResponsiveLayout.Rows(2, 0)
            })
            {
                bool rejected = false;
                try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
                if (!rejected) throw new Exception("Se aceptó un parámetro inválido.");
            }
        });
    }
}
