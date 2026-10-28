using Arcana.Core;
using Godot;
using System.Globalization;

namespace Arcana.Client;

/// <summary>Barras verticales con ejes comunes y categorías legibles, sin proceso por fotograma.</summary>
public partial class StatisticsBarChart : Control
{
    public readonly record struct Part(string Label, double Value, Color Color);
    public readonly record struct Category(string Label, double Value, Color Color, Part[]? Parts = null, MinionKind? Enemy = null);
    public Category[] Categories { get; set; } = [];
    public float CategoryWidth { get; init; } = 80;
    public string HorizontalTitle { get; init; } = "Elemento";
    public string ValueTitle { get; init; } = "Total";
    public bool? Logarithmic { get; init; }
    private const float Left = 72, Right = 20, Top = 28, Bottom = 146;
    private readonly Dictionary<int, BestiaryPortrait> _portraits = [];
    internal IReadOnlyDictionary<int, BestiaryPortrait> EnemyPortraits => _portraits;
    private float PlotBottom => _portraits.Count > 0 ? 228 : Bottom;
    internal static string ShortLabel(string label) => label switch
    {
        "Hechizos directos" or "Ataques directos" => "Daño",
        "Áreas" or "Áreas de hechizos" => "Área",
        "Auras" or "Auras pasivas" => "Aura",
        "Quemaduras" => "Quemadura",
        "Sin desglose" => "Sin detalle",
        "Curación" or "Escudo vital" or "Regeneración" or "Anillo" or "Personal" or "Barrera" or "Área" or "Aura" => label,
        "Vida" => "Curación",
        "Escudo" => "Absorción",
        _ => "Daño"
    };
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");

    public StatisticsBarChart()
    {
        CustomMinimumSize = new(0, 340); SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Pass; Resized += () => { PositionPortraits(); QueueRedraw(); };
    }
    public override void _Ready()
    { CustomMinimumSize = new(Left + Right + Categories.Length * CategoryWidth, CustomMinimumSize.Y); PreparePortraits(); }
    internal void ReplaceCategories(Category[] categories)
    {
        Categories = categories;
        CustomMinimumSize = new(Left + Right + Categories.Length * CategoryWidth, CustomMinimumSize.Y);
        if (IsInsideTree()) PreparePortraits();
        QueueRedraw();
    }
    private void PreparePortraits()
    {
        if (_portraits.Count == Categories.Count(c => c.Enemy.HasValue) &&
            _portraits.All(p => p.Key < Categories.Length && Categories[p.Key].Enemy == p.Value.Kind)) return;
        foreach (var portrait in _portraits.Values) { RemoveChild(portrait); portrait.QueueFree(); }
        _portraits.Clear();
        for (int i = 0; i < Categories.Length; i++)
            if (Categories[i].Enemy is { } kind) _portraits[i] = BestiaryPortrait.AddTo(this, kind);
        PositionPortraits();
    }
    private void PositionPortraits()
    {
        float slot = Math.Max(1, Size.X - Left - Right) / Math.Max(1, Categories.Length);
        foreach (var pair in _portraits)
            pair.Value.Position = new(Left + (pair.Key + .5f) * slot - 36, Size.Y - PlotBottom + 12);
    }
    internal MatchStatisticsScale DataScale => new(Categories.SelectMany(c => c.Parts is { Length: > 0 } parts ? parts.Select(p => p.Value) : new[] { c.Value }), Logarithmic);
    internal double ScaleMaximum => DataScale.Maximum;
    private static string Short(double n) => n >= 1_000_000 ? (n / 1_000_000).ToString("0.#", Spanish) + " M"
        : n >= 1000 ? (n / 1000).ToString("0.#", Spanish) + " k" : n.ToString("0.##", Spanish);
    public override void _Draw()
    {
        var font = GetThemeDefaultFont(); const int textSize = 14;
        float width = Math.Max(1, Size.X - Left - Right), height = Math.Max(1, Size.Y - Top - PlotBottom);
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
            void Bar(double value, float x, Color color, float available, string label)
            {
                float barHeight = (float)scale.Normalize(value) * height;
                float barWidth = Math.Min(40, available * 0.72f);
                if (value > 0) DrawRect(new(x - barWidth / 2, Top + height - barHeight, barWidth, barHeight), color);
                if (value > 0) DrawString(font, new(x - available / 2, Top + height - barHeight - 6), Short(value),
                    HorizontalAlignment.Center, available, 11, Ui.Text);
                if (value <= 0) return;
                string caption = ShortLabel(label);
                const int captionSize = 12;
                float textWidth = font.GetStringSize(caption, fontSize: captionSize).X;
                bool inside = barHeight >= textWidth + 12;
                float baseline = Top + height;
                // Las barras pequeñas mantienen su altura real: su etiqueta va debajo del eje.
                float textY = inside ? baseline - (barHeight - textWidth) / 2 : baseline + textWidth + 8;
                DrawSetTransform(new(x + 4, textY), -Mathf.Pi / 2);
                var ink = inside ? new Color("101820") : Ui.Text;
                DrawStringOutline(font, Vector2.Zero, caption, HorizontalAlignment.Left, -1, captionSize, 2,
                    inside ? new Color(1, 1, 1, .55f) : ArcanaMenuChrome.Backdrop);
                DrawString(font, Vector2.Zero, caption, HorizontalAlignment.Left, -1, captionSize, ink);
                DrawSetTransform(Vector2.Zero);
            }
            if (category.Parts is { Length: > 0 } parts)
            {
                float available = (slot - 12) / parts.Length;
                for (int j = 0; j < parts.Length; j++)
                    Bar(parts[j].Value, center - (slot - 12) / 2 + (j + .5f) * available, parts[j].Color, available, parts[j].Label);
            }
            else Bar(category.Value, center, category.Color, slot * .75f, category.Label);
            var lines = new List<string>(); string line = "";
            foreach (string word in category.Label.Split(' '))
            {
                if (line.Length > 0 && font.GetStringSize(line + " " + word, fontSize: textSize).X > slot - 10)
                { lines.Add(line); line = word; }
                else line += (line.Length == 0 ? "" : " ") + word;
            }
            lines.Add(line);
            for (int j = 0; j < lines.Count; j++)
                DrawString(font, new(center - slot / 2 + 4, Top + height + 100 + j * 17), lines[j], HorizontalAlignment.Center, slot - 8, textSize, Ui.Text);
        }
        DrawString(font, new(Left, Size.Y - 8), HorizontalTitle, HorizontalAlignment.Left, width, 14, Ui.Gold);
    }
    public override string _GetTooltip(Vector2 position)
    {
        if (Categories.Length == 0) return "";
        int index = Math.Clamp((int)((position.X - Left) / Math.Max(1, Size.X - Left - Right) * Categories.Length), 0, Categories.Length - 1);
        var c = Categories[index];
        string detail = c.Parts == null ? "" : string.Join("\n", c.Parts.Select(p => p.Label + ": " + StatisticsPresentation.Number(p.Value) + " · " + StatisticsPresentation.Percent(p.Value, c.Value))) + "\n";
        return c.Label + "\n" + detail + ValueTitle + ": " + StatisticsPresentation.Number(c.Value) + "\n" +
            StatisticsPresentation.Percent(c.Value, Categories.Sum(c => c.Value)) + " del total de esta gráfica";
    }
}
