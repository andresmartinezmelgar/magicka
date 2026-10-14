namespace Arcana.Core;

/// <summary>
/// Cálculos de rejilla independientes de Godot y del sistema operativo.
/// Devuelve siempre al menos una columna y nunca sobrepasa el máximo especificado.
/// </summary>
public static class ResponsiveLayout
{
    public static int Columns(float availableWidth, float minimumItemWidth, int maxColumns, float spacing = 0)
    {
        if (!float.IsFinite(availableWidth) || availableWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        if (!float.IsFinite(minimumItemWidth) || minimumItemWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumItemWidth));
        if (maxColumns < 1) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        if (!float.IsFinite(spacing) || spacing < 0)
            throw new ArgumentOutOfRangeException(nameof(spacing));
        return Math.Clamp((int)Math.Floor((availableWidth + spacing) / (minimumItemWidth + spacing)), 1, maxColumns);
    }

    public static int Rows(int items, int columns)
    {
        if (items < 0) throw new ArgumentOutOfRangeException(nameof(items));
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        return items / columns + (items % columns == 0 ? 0 : 1);
    }
}
