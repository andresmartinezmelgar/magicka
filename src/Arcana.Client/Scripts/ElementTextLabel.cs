using Godot;

namespace Arcana.Client;

/// <summary>Colores por palabra con las medidas, alineación y texto plano de las etiquetas del juego.</summary>
public partial class ElementTextLabel : Control
{
    private readonly Label _measure = new() { Visible = false, MouseFilter = MouseFilterEnum.Ignore };
    private readonly RichTextLabel _content = new()
    {
        BbcodeEnabled = true, FitContent = false, ScrollActive = false, SelectionEnabled = false,
        MouseFilter = MouseFilterEnum.Ignore, FocusMode = FocusModeEnum.None, AutowrapMode = TextServer.AutowrapMode.Off
    };
    private string _plainText = "";
    private bool _updatingTheme;
    private Color? _semanticColor;
    internal Color? SemanticColor
    {
        get => _semanticColor;
        set { if (_semanticColor == value) return; _semanticColor = value; RenderText(); }
    }
    internal string FormattedText => _content.Text;
    internal Color PlainColor => _content.GetThemeColor("default_color");
    public string Text
    {
        get => _plainText;
        set
        {
            if (_plainText == value) return;
            _plainText = value; _measure.Text = value;
            RenderText(); UpdateMinimumSize();
        }
    }
    public TextServer.AutowrapMode AutowrapMode
    {
        get => _measure.AutowrapMode;
        set { _content.AutowrapMode = _measure.AutowrapMode = value; UpdateMinimumSize(); }
    }
    public new string TooltipText
    {
        get => base.TooltipText;
        set { base.TooltipText = value; MouseFilter = string.IsNullOrEmpty(value) ? MouseFilterEnum.Ignore : MouseFilterEnum.Pass; }
    }
    public HorizontalAlignment HorizontalAlignment { get => _content.HorizontalAlignment; set => _content.HorizontalAlignment = value; }
    public VerticalAlignment VerticalAlignment { get => _content.VerticalAlignment; set => _content.VerticalAlignment = value; }
    public bool ClipText { get => _measure.ClipText; set { _measure.ClipText = value; RenderText(); UpdateMinimumSize(); } }
    public TextServer.OverrunBehavior TextOverrunBehavior
    {
        get => _measure.TextOverrunBehavior;
        set { _measure.TextOverrunBehavior = value; RenderText(); UpdateMinimumSize(); }
    }
    public int MaxLinesVisible { get => _measure.MaxLinesVisible; set { _measure.MaxLinesVisible = value; UpdateMinimumSize(); } }
    public ElementTextLabel()
    {
        HorizontalAlignment = _measure.HorizontalAlignment; VerticalAlignment = _measure.VerticalAlignment;
        SizeFlagsHorizontal = _measure.SizeFlagsHorizontal; SizeFlagsVertical = _measure.SizeFlagsVertical;
        ThemeTypeVariation = "Label"; MouseFilter = MouseFilterEnum.Ignore; FocusMode = FocusModeEnum.None;
        AddChild(_measure); AddChild(_content);
        _measure.MinimumSizeChanged += UpdateMinimumSize;
        Resized += () => { _measure.Size = _content.Size = Size; RenderText(); UpdateMinimumSize(); };
    }
    public override Vector2 _GetMinimumSize() => _measure?.GetMinimumSize() ?? Vector2.Zero;
    public string GetParsedText() => _content.GetParsedText();
    public int GetLineCount() => _content.GetLineCount();
    public int GetVisibleLineCount() => _content.GetVisibleLineCount();
    public new void AddThemeFontSizeOverride(StringName name, int size)
    {
        base.AddThemeFontSizeOverride(name, size);
        _content.AddThemeFontSizeOverride(name == "font_size" ? "normal_font_size" : name, size);
        _measure.AddThemeFontSizeOverride(name, size); UpdateMinimumSize();
    }
    public new void AddThemeFontOverride(StringName name, Font font)
    {
        base.AddThemeFontOverride(name == "normal_font" ? "font" : name, font);
        _content.AddThemeFontOverride(name == "font" ? "normal_font" : name, font);
        _measure.AddThemeFontOverride(name == "normal_font" ? "font" : name, font); UpdateMinimumSize();
    }
    public new void AddThemeColorOverride(StringName name, Color color)
    {
        base.AddThemeColorOverride(name, color);
        _content.AddThemeColorOverride(name == "font_color" ? "default_color" : name, color);
    }
    public new void AddThemeStyleboxOverride(StringName name, StyleBox style)
    {
        base.AddThemeStyleboxOverride(name, style); _content.AddThemeStyleboxOverride(name, style); _measure.AddThemeStyleboxOverride(name, style); UpdateMinimumSize();
    }
    public new void AddThemeConstantOverride(StringName name, int value)
    {
        base.AddThemeConstantOverride(name, value);
        _content.AddThemeConstantOverride(name == "line_spacing" ? "line_separation" : name, value);
        _measure.AddThemeConstantOverride(name, value); UpdateMinimumSize();
    }
    public override void _Notification(int what)
    {
        if (what != NotificationThemeChanged || _measure == null || _content == null || _updatingTheme) return;
        _updatingTheme = true;
        try
        {
            var font = GetThemeFont("font"); int size = GetThemeFontSize("font_size");
            _measure.AddThemeFontOverride("font", font); _content.AddThemeFontOverride("normal_font", font);
            _measure.AddThemeFontSizeOverride("font_size", size); _content.AddThemeFontSizeOverride("normal_font_size", size);
            _content.AddThemeColorOverride("default_color", GetThemeColor("font_color"));
            _content.AddThemeConstantOverride("line_separation", _measure.GetThemeConstant("line_spacing"));
            RenderText();
        }
        finally { _updatingTheme = false; }
    }
    private void RenderText()
    {
        string visible = _plainText;
        if (AutowrapMode == TextServer.AutowrapMode.Off && TextOverrunBehavior != TextServer.OverrunBehavior.NoTrimming && Size.X > 0)
        {
            var font = GetThemeFont("font"); int size = GetThemeFontSize("font_size");
            float width = Math.Max(0, Size.X - _measure.GetThemeStylebox("normal").GetMinimumSize().X);
            if (font.GetStringSize(visible, fontSize: size).X > width)
            {
                int low = 0, high = visible.Length;
                while (low < high)
                {
                    int middle = (low + high + 1) / 2;
                    if (font.GetStringSize(visible[..middle] + "…", fontSize: size).X <= width) low = middle;
                    else high = middle - 1;
                }
                if (low > 0 && char.IsHighSurrogate(visible[low - 1])) low--;
                visible = visible[..low] + "…";
            }
        }
        _content.Text = ElementText.ToBbcode(visible, e => (_semanticColor ?? Ui.ElementTextColor(e)).ToHtml(false));
    }
    public override GodotObject _MakeCustomTooltip(string forText) => string.IsNullOrEmpty(forText) ? null! : Ui.ElementTooltip(forText);
}
