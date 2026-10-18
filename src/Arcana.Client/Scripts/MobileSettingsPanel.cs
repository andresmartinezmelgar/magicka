using Arcana.Core;
using Godot;

namespace Arcana.Client;

/// <summary>Ajustes locales con edición directa del HUD, sin enviar entradas al juego.</summary>
public partial class MobileSettingsPanel : Control
{
    private TouchSettings _settings = new();
    private TouchSettings _beforeEdit = new();
    private Control _form = null!, _editorBar = null!;
    private VBoxContainer _sliders = null!;
    private Label _status = null!;
    private MobileControls _preview = null!;
    public event Action<TouchSettings>? Saved;
    public event Action? Canceled;
    internal TouchSettings Configuration => _preview.Visible ? _preview.Settings : _settings;

    public void Build(TouchSettings settings)
    {
        Name = "MobileSettings"; Theme = Ui.Theme(); Ui.Full(this); MouseFilter = MouseFilterEnum.Stop;
        _settings = settings.Normalize();
        var backdrop = new ColorRect { Color = new Color("081424") }; AddChild(backdrop); Ui.Full(backdrop);
        _preview = new MobileControls { EditorMode = true, Visible = false }; AddChild(_preview); _preview.Configure(_settings);
        var margin = new MarginContainer(); _form = margin; AddChild(margin); Ui.Full(margin);
        foreach (string side in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + side, 16);
        var panel = new PanelContainer(); margin.AddChild(panel);
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); panel.AddChild(box);
        box.AddChild(Ui.Label("CONTROLES TÁCTILES", 24, Ui.Gold));
        var help = Ui.Label("Ajustes de este dispositivo. Puedes cambiar cada posición arrastrando los botones y los dos joysticks.", 16, Ui.Muted);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(help);
        var scroll = new ContentScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        box.AddChild(scroll); Ui.VisibleVerticalScroll(scroll);
        _sliders = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _sliders.AddThemeConstantOverride("separation", 8); scroll.AddChild(_sliders);
        BuildFields();
        var edit = Ui.Button("Mover botones y joysticks", StartEditing); edit.Name = "TouchEditPositions"; box.AddChild(edit);
        _status = Ui.Label("", 15, Ui.Gold); _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; box.AddChild(_status);
        var footer = new HBoxContainer(); box.AddChild(footer);
        foreach (var button in new[] { Ui.Button("Restablecer", () => { _settings = new(); BuildFields(); }),
            Ui.Button("Cancelar", () => Canceled?.Invoke()), Ui.Button("Guardar", Save) })
        { button.SizeFlagsHorizontal = SizeFlags.ExpandFill; button.CustomMinimumSize = new(0, 48); footer.AddChild(button); }
        var bar = new PanelContainer { Name = "TouchEditorToolbar", Position = new(12, 12), Size = new(550, 100), Visible = false }; _editorBar = bar; AddChild(bar);
        bar.MinimumSizeChanged += () => Callable.From(FitEditorToolbar).CallDeferred();
        var tools = new VBoxContainer(); bar.AddChild(tools);
        var instruction = Ui.Label("Arrastra cada control hasta su posición", 16, Ui.Gold);
        instruction.AutowrapMode = TextServer.AutowrapMode.WordSmart; tools.AddChild(instruction);
        var actions = new HBoxContainer(); tools.AddChild(actions);
        var done = Ui.Button("Listo", FinishEditing); done.Name = "TouchEditorDone"; actions.AddChild(done);
        actions.AddChild(Ui.Button("Deshacer posiciones", () => { _settings = _beforeEdit; FinishEditing(false); }));
        foreach (var button in actions.GetChildren().OfType<Button>())
        { button.CustomMinimumSize = new(0, 44); button.AddThemeFontSizeOverride("font_size", 16); }
        _preview.PointerBlocked = point => _editorBar.Visible && _editorBar.GetGlobalRect().HasPoint(point);
    }
    private void BuildFields()
    {
        foreach (var child in _sliders.GetChildren()) { _sliders.RemoveChild(child); child.QueueFree(); }
        void Slider(string name, string title, double min, double max, double value, Action<float> set)
        {
            var label = Ui.Label(title + $" · {value:0.00}", 17); _sliders.AddChild(label);
            var slider = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = .01, Value = value, CustomMinimumSize = new(0, 40) };
            slider.ValueChanged += next => { label.Text = title + $" · {next:0.00}"; set((float)next); }; _sliders.AddChild(slider);
        }
        Slider("TouchSize", "Tamaño de botones y joysticks", .75, 1.3, _settings.Size, v => _settings = _settings with { Size = v });
        Slider("TouchSpacing", "Separación entre botones", .8, 1.25, _settings.Spacing, v => _settings = _settings with { Spacing = v, Positions = new() });
        Slider("TouchDeadZone", "Zona muerta del joystick", .05, .35, _settings.DeadZone, v => _settings = _settings with { DeadZone = v });
        Slider("TouchSensitivity", "Sensibilidad del alcance de traslación y meteoro", .6, 1.8, _settings.Sensitivity, v => _settings = _settings with { Sensitivity = v });
        var handed = new CheckBox { Name = "TouchLeftHanded", Text = "Modo zurdo · movimiento a la derecha", ButtonPressed = _settings.LeftHanded, CustomMinimumSize = new(0, 46) };
        handed.Toggled += left => _settings = _settings with { LeftHanded = left, Positions = new() }; _sliders.AddChild(handed);
        var note = Ui.Label("Cambiar separación o modo zurdo restablece las posiciones personalizadas. Agua, hielo y rocas mantienen su marcador a distancia fija.", 14, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart; _sliders.AddChild(note);
    }
    private void StartEditing()
    {
        _beforeEdit = _settings.Normalize(); _preview.Configure(_settings);
        float width = Math.Min(550, GetViewport().GetVisibleRect().Size.X * .53f);
        _editorBar.Size = new(width, 100);
        _editorBar.Position = new(_settings.LeftHanded ? GetViewport().GetVisibleRect().Size.X - width - 12 : 12, 12);
        _form.Hide(); _preview.Show(); _editorBar.Show();
    }
    private void FinishEditing() => FinishEditing(true);
    private void FitEditorToolbar()
    {
        if (!IsInsideTree() || !_editorBar.Visible) return;
        _editorBar.Size = new(_editorBar.Size.X, _editorBar.GetCombinedMinimumSize().Y);
    }
    private void FinishEditing(bool apply)
    {
        if (apply) _settings = _preview.Settings.Normalize();
        _preview.Hide(); _editorBar.Hide(); _form.Show();
    }
    private void Save()
    {
        _settings = _settings.Normalize();
        if (!DevicePreferences.Write("touch-controls", _settings)) { _status.Text = "No se pudieron guardar los controles. Vuelve a intentarlo."; return; }
        Saved?.Invoke(_settings);
    }
    public void HandleBack() { if (_editorBar.Visible) FinishEditing(); else Canceled?.Invoke(); }
}
