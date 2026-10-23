using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

/// <summary>Barras verticales con ejes comunes y categorías legibles, sin proceso por fotograma.</summary>
public partial class StatisticsBarChart : Control
{
    public readonly record struct Category(string Label, double Value, Color Color);
    public Category[] Categories { get; init; } = [];
    public float CategoryWidth { get; init; } = 80;
    public string HorizontalTitle { get; init; } = "Elemento";
    public string ValueTitle { get; init; } = "Total";
    public bool? Logarithmic { get; init; }
    private const float Left = 72, Right = 20, Top = 28, Bottom = 64;
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public StatisticsBarChart()
    {
        CustomMinimumSize = new(0, 340); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass; Resized += QueueRedraw;
    }
    public override void _Ready() => CustomMinimumSize = new(Left + Right + Categories.Length * CategoryWidth, CustomMinimumSize.Y);
    internal MatchStatisticsScale DataScale => new(Categories.Select(c => c.Value), Logarithmic);
    internal double ScaleMaximum => DataScale.Maximum;
    private static string Short(double n) => n >= 1_000_000 ? (n / 1_000_000).ToString("0.#", Spanish) + " M"
        : n >= 1000 ? (n / 1000).ToString("0.#", Spanish) + " k" : n.ToString("0.##", Spanish);
    public override void _Draw()
    {
        var font = GetThemeDefaultFont(); const int textSize = 14;
        float width = Math.Max(1, Size.X - Left - Right), height = Math.Max(1, Size.Y - Top - Bottom);
        var scale = DataScale;
        DrawString(font, new(Left, 18), scale.Logarithmic ? "Puntos · escala logarítmica" : "Puntos · escala lineal", fontSize: textSize, modulate: Ui.Muted);
        for (int tick = 0; tick <= 4; tick++)
        {
            float y = Top + height * (1 - tick / 4f);
            DrawLine(new(Left, y), new(Size.X - Right, y), ArcanaMenuChrome.Border.Darkened(0.45f));
            DrawString(font, new(0, y + 5), Short(scale.ValueAt(tick / 4.0)), HorizontalAlignment.Right, Left - 8, textSize, Ui.Muted);
        }
        DrawLine(new(Left, Top), new(Left, Top + height), Ui.Muted);
        DrawLine(new(Left, Top + height), new(Size.X - Right, Top + height), Ui.Muted);
        float slot = width / Math.Max(1, Categories.Length);
        for (int i = 0; i < Categories.Length; i++)
        {
            var category = Categories[i]; float center = Left + (i + 0.5f) * slot;
            void Bar(double value, float x, Color color)
            {
                float barHeight = (float)scale.Normalize(value) * height;
                float barWidth = Math.Min(40, slot * 0.55f);
                if (value > 0) DrawRect(new(x - barWidth / 2, Top + height - barHeight, barWidth, barHeight), color);
                DrawString(font, new(x - slot * 0.25f, Top + height - barHeight - 6), Short(value),
                    HorizontalAlignment.Center, slot * 0.5f, 12, Ui.Text);
            }
            Bar(category.Value, center, category.Color);
            var lines = new List<string>(); string line = "";
            foreach (string word in category.Label.Split(' '))
            {
                if (line.Length > 0 && font.GetStringSize(line + " " + word, fontSize: textSize).X > slot - 10)
                { lines.Add(line); line = word; }
                else line += (line.Length == 0 ? "" : " ") + word;
            }
            lines.Add(line);
            for (int j = 0; j < lines.Count; j++)
                DrawString(font, new(center - slot / 2 + 4, Top + height + 22 + j * 17), lines[j], HorizontalAlignment.Center, slot - 8, textSize, Ui.Text);
        }
        DrawString(font, new(Left, Size.Y - 8), HorizontalTitle, HorizontalAlignment.Left, width, 14, Ui.Gold);
    }
    public override string _GetTooltip(Vector2 position)
    {
        if (Categories.Length == 0) return "";
        int index = Math.Clamp((int)((position.X - Left) / Math.Max(1, Size.X - Left - Right) * Categories.Length), 0, Categories.Length - 1);
        var c = Categories[index];
        return c.Label + "\n" + ValueTitle + ": " + StatisticsPresentation.Number(c.Value) + "\n" +
            StatisticsPresentation.Percent(c.Value, Categories.Sum(c => c.Value)) + " del total de esta gráfica";
    }
}
