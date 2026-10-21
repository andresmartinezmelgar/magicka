using Godot;
using System.Globalization;

namespace Arcana.Client;

/// <summary>Curva de medias agregadas, sin muestreo ni conexiones adicionales.</summary>
public partial class StatisticsTrendChart : Control
{
    public readonly record struct Sample(int Difficulty, double Health);
    public Sample[] Points { get; init; } = [];
    public StatisticsTrendChart()
    {
        CustomMinimumSize = new(0, Ui.IsMobile ? 180 : 230); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass; Resized += QueueRedraw;
    }
    private Vector2 Point(Sample sample, double maximum)
    {
        int first = Points[0].Difficulty, last = Points[^1].Difficulty;
        return new(70 + (float)((last == first ? 0.5 : (sample.Difficulty - first) / (double)(last - first)) * Math.Max(1, Size.X - 92)),
            15 + (float)((1 - sample.Health / maximum) * (Size.Y - 55)));
    }
    public override void _Draw()
    {
        if (Points.Length == 0) return;
        var font = GetThemeDefaultFont(); int fontSize = Ui.IsMobile ? 16 : 14;
        string Number(double value) => value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.GetCultureInfo("es-ES"));
        double maximum = Math.Max(1, Points.Max(p => p.Health));
        for (int i = 0; i <= 4; i++)
        {
            float y = 15 + i * (Size.Y - 55) / 4;
            DrawLine(new(70, y), new(Size.X - 22, y), new("36516c"), 1);
            string tick = Number(maximum * (4 - i) / 4); var width = font.GetStringSize(tick, fontSize: fontSize).X;
            // Acota los rótulos grandes sin invadir la curva ni salir de la tarjeta.
            float textScale = Math.Min(1, 62 / Math.Max(1, width));
            DrawSetTransform(new(64 - width * textScale, y + 5), 0, new(textScale, 1));
            DrawString(font, Vector2.Zero, tick, fontSize: fontSize, modulate: Ui.Muted); DrawSetTransform(Vector2.Zero);
        }
        DrawLine(new(70, 15), new(70, Size.Y - 40), Ui.Muted, 1);
        for (int i = 1; i < Points.Length; i++) DrawLine(Point(Points[i - 1], maximum), Point(Points[i], maximum), Ui.Health, 3, true);
        foreach (var sample in Points) DrawCircle(Point(sample, maximum), 4, Ui.Health, antialiased: true);
        foreach (int i in Enumerable.Range(0, Math.Min(5, Points.Length)).Select(n => Points.Length == 1 ? 0 : (int)Math.Round(n * (Points.Length - 1) / (double)(Math.Min(5, Points.Length) - 1))).Distinct())
        {
            string tick = Points[i].Difficulty.ToString(CultureInfo.InvariantCulture);
            DrawString(font, new(Point(Points[i], maximum).X - font.GetStringSize(tick, fontSize: fontSize).X / 2, Size.Y - 18), tick, fontSize: fontSize, modulate: Ui.Muted);
        }
    }
    public override string _GetTooltip(Vector2 atPosition)
    {
        if (Points.Length == 0) return "";
        double maximum = Math.Max(1, Points.Max(p => p.Health));
        var closest = Points.MinBy(sample => Math.Abs(Point(sample, maximum).X - atPosition.X));
        return $"Dificultad {closest.Difficulty}: {closest.Health.ToString("N1", CultureInfo.GetCultureInfo("es-ES"))} de vida inicial media";
    }
}
