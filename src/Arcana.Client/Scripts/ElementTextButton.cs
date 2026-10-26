using Godot;

namespace Arcana.Client;

/// <summary>Conserva el texto y las medidas nativas del botón; solo sustituye el dibujo de su título.</summary>
public partial class ElementTextButton : Button
{
    private static readonly string[] FontColors = ["font_color", "font_hover_color", "font_pressed_color",
        "font_hover_pressed_color", "font_focus_color", "font_disabled_color", "font_outline_color"];
    private readonly Dictionary<string, (Color Color, bool Override)> _colors = [];
    private ElementTextLabel? _caption;
    private string? _lastText;
    private bool _hasElements, _refreshing;

    public override void _Notification(int what)
    {
        if (_refreshing || (long)what is not (NotificationDraw or NotificationThemeChanged or NotificationResized)) return;
        _refreshing = true;
        try { RefreshCaption(); }
        finally { _refreshing = false; }
    }

    private void RefreshCaption()
    {
        if (_colors == null) return;
        if (_lastText != Text)
        {
            _lastText = Text; _hasElements = ElementText.Parse(Text).Length > 0;
        }
        if (!_hasElements || Icon != null)
        {
            if (_caption != null) _caption.Hide();
            foreach (var (name, original) in _colors)
                if (original.Override) base.AddThemeColorOverride(name, original.Color);
                else RemoveThemeColorOverride(name);
            _colors.Clear();
            return;
        }
        foreach (string name in FontColors)
        {
            Color current = GetThemeColor(name);
            if (current.A == 0 && _colors.ContainsKey(name)) continue;
            _colors[name] = (current, HasThemeColorOverride(name));
            base.AddThemeColorOverride(name, Colors.Transparent);
        }
        if (_caption == null)
        {
            _caption = Ui.Label("", GetThemeFontSize("font_size"));
            AddChild(_caption);
        }
        var style = GetThemeStylebox("normal");
        _caption.Position = new(style.GetContentMargin(Side.Left), style.GetContentMargin(Side.Top));
        _caption.Size = (Size - style.GetMinimumSize()).Max(Vector2.Zero);
        _caption.AutowrapMode = AutowrapMode;
        _caption.HorizontalAlignment = Alignment;
        _caption.VerticalAlignment = VerticalAlignment.Center;
        _caption.TextOverrunBehavior = TextOverrunBehavior;
        _caption.AddThemeFontSizeOverride("font_size", GetThemeFontSize("font_size"));
        _caption.AddThemeFontOverride("normal_font", GetThemeFont("font"));
        string state = Disabled ? "font_disabled_color" : IsPressed() ? "font_pressed_color"
            : IsHovered() ? "font_hover_color" : "font_color";
        _caption.AddThemeColorOverride("font_color", _colors[state].Color);
        _caption.Modulate = new Color(1, 1, 1, Disabled ? .45f : 1);
        _caption.Text = Text; _caption.Show();
    }

    public override GodotObject _MakeCustomTooltip(string forText) => string.IsNullOrEmpty(forText) ? null! : Ui.ElementTooltip(forText);
}
